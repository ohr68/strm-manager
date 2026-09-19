using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.Catalog.Application.Playback;
using StrmManager.Modules.Catalog.Infrastructure;

namespace StrmManager.Modules.Catalog.UnitTests.Playback;

/// <summary>
/// The stable playback URL a movie's .strm contains: {PublicBaseUrl}/media/{movieId}/stream. It is built from configuration and
/// the movie's id alone - so what is valid, what is refused, and how it is normalized are all decided here, with no request, no
/// HttpContext and no provider data anywhere near it.
/// </summary>
public class PlaybackUrlBuilderTests
{
    private static readonly Guid Movie = Guid.Parse("7c1f0c1e-38a4-4d0e-9d7b-2f6a1e5b9c30");

    private static PlaybackUrlBuilder BuilderFor(string? baseUrl) =>
        new(Options.Create(new PlaybackOptions { PublicBaseUrl = baseUrl }));

    // ---- valid ----

    [Theory]
    [InlineData("http://strm-manager:8080", "http://strm-manager:8080")]
    [InlineData("http://192.168.1.10:8101", "http://192.168.1.10:8101")]
    [InlineData("https://media.example.com", "https://media.example.com")]
    [InlineData("HTTP://Strm-Manager:8080", "http://strm-manager:8080")]      // scheme and host are case-insensitive
    [InlineData("http://[::1]:8080", "http://[::1]:8080")]
    public void AValidHttpOrHttpsBase_BuildsExactlyBasePlusTheMediaRoute(string configured, string expectedBase)
    {
        Result<string> result = BuilderFor(configured).Build(Movie);

        Assert.True(result.IsSuccess);
        Assert.Equal($"{expectedBase}/media/{Movie}/stream", result.Value);
    }

    [Theory]
    [InlineData("https://media.example.com/")]
    [InlineData("https://media.example.com//")]
    [InlineData("https://media.example.com///")]
    [InlineData("  https://media.example.com/  ")]
    public void ATrailingSlash_OrSurroundingWhitespace_NeverProducesADoubleSlash(string configured)
    {
        string url = BuilderFor(configured).Build(Movie).Value;

        Assert.Equal($"https://media.example.com/media/{Movie}/stream", url);
        Assert.DoesNotContain("com//", url, StringComparison.Ordinal);                                   // no double slash after the host
        Assert.Equal(1, url.Split("/media/", StringSplitOptions.None).Length - 1);                       // the route appears exactly once
        Assert.DoesNotContain("//", url[url.IndexOf("://", StringComparison.Ordinal)..].Replace("://", "", StringComparison.Ordinal), StringComparison.Ordinal); // and no "//" anywhere past the scheme
    }

    [Theory]
    [InlineData("https://host/strm", "https://host/strm")]
    [InlineData("https://host/strm/", "https://host/strm")]
    [InlineData("http://host:8080/apps/strm-manager/", "http://host:8080/apps/strm-manager")]
    public void AnIntentionalBasePath_IsKept(string configured, string expectedBase)
    {
        Assert.Equal($"{expectedBase}/media/{Movie}/stream", BuilderFor(configured).Build(Movie).Value);
    }

    [Fact]
    public void TheRoute_IsExactlyMediaGuidStream_UsingTheLowercaseHyphenatedGuid()
    {
        string url = BuilderFor("http://strm-manager:8080").Build(Guid.Parse("ABCDEF01-2345-6789-ABCD-EF0123456789")).Value;

        Assert.Equal("http://strm-manager:8080/media/abcdef01-2345-6789-abcd-ef0123456789/stream", url);
    }

    [Fact]
    public void TheUrl_IsDeterministic_AndDiffersOnlyByTheMovieId()
    {
        PlaybackUrlBuilder builder = BuilderFor("https://media.example.com");
        Guid other = Guid.NewGuid();

        Assert.Equal(builder.Build(Movie).Value, builder.Build(Movie).Value);
        Assert.Equal(
            builder.Build(Movie).Value.Replace(Movie.ToString(), "X", StringComparison.Ordinal),
            builder.Build(other).Value.Replace(other.ToString(), "X", StringComparison.Ordinal));
    }

    // ---- refused ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    [InlineData("/media")]                       // relative
    [InlineData("strm-manager:8080")]            // no scheme
    [InlineData("relative/path")]
    [InlineData("//strm-manager:8080")]          // scheme-relative
    [InlineData("ftp://strm-manager:8080")]
    [InlineData("file:///var/strm")]
    [InlineData("ws://strm-manager:8080")]
    [InlineData("javascript:alert(1)")]
    [InlineData("not a url")]
    [InlineData("http://")]
    [InlineData("https:///nohost")]
    [InlineData("http://user:secret@strm-manager:8080")]   // credentials would be written into every .strm
    [InlineData("http://strm-manager:8080/?api_key=abc")]  // a query would swallow the appended route
    [InlineData("http://strm-manager:8080/#frag")]
    public void AMissingOrUnusableBase_IsRefused_WithTheSingleStableError(string? configured)
    {
        Result<string> result = BuilderFor(configured).Build(Movie);

        Assert.True(result.IsFailure);
        Assert.Equal(PlaybackErrors.PublicBaseUrlInvalid, result.Error);
        Assert.Equal(ErrorType.Failure, result.Error.Type);
    }

    [Theory]
    [InlineData("http://user:secret@strm-manager:8080")]
    [InlineData("ftp://leaky.example.invalid/SECRET-PATH")]
    [InlineData("http://strm-manager:8080/?api_key=SECRETKEY")]
    public void TheError_NeverEchoesTheConfiguredValue(string configured)
    {
        Error error = BuilderFor(configured).Build(Movie).Error;

        foreach (string piece in new[] { "secret", "SECRET", "leaky", "api_key", "strm-manager:8080" })
        {
            Assert.DoesNotContain(piece, error.Description, StringComparison.Ordinal);
            Assert.DoesNotContain(piece, error.Code, StringComparison.Ordinal);
        }

        Assert.Contains("Playback:PublicBaseUrl", error.Description, StringComparison.Ordinal); // it does name the setting to fix
    }

    // ---- no request / provider input ----

    [Fact]
    public void TheBuilder_TakesNothingButConfiguration_NoHttpContextAndNoProviderData()
    {
        Type[] constructorParameters = typeof(PlaybackUrlBuilder).GetConstructors().Single().GetParameters().Select(p => p.ParameterType).ToArray();
        Type[] buildParameters = typeof(IPlaybackUrlBuilder).GetMethod(nameof(IPlaybackUrlBuilder.Build))!.GetParameters().Select(p => p.ParameterType).ToArray();

        Assert.Equal([typeof(IOptions<PlaybackOptions>)], constructorParameters);
        Assert.Equal([typeof(Guid)], buildParameters); // the movie id, and nothing a provider or a request could supply
    }

    [Fact]
    public void PlaybackOptions_HoldsOnlyPublicBaseUrl_AndIsNotValidatedAtStartup()
    {
        Assert.Equal(["PublicBaseUrl"], typeof(PlaybackOptions).GetProperties().Select(p => p.Name));
        Assert.DoesNotContain(
            typeof(PlaybackOptions).GetProperty(nameof(PlaybackOptions.PublicBaseUrl))!.GetCustomAttributes(inherit: true),
            a => a.GetType().Namespace == "System.ComponentModel.DataAnnotations"); // no [Required]: startup must not depend on it
    }

    // ---- configuration binding: the same path a deployment uses ----

    private static ServiceProvider CatalogModuleOver(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddCatalogModule(configuration);
        return services.BuildServiceProvider();
    }

    private static IConfiguration ConfigurationOf(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Database"] = "Data Source=:memory:" }
                .Concat(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))))
            .Build();

    [Fact]
    public void ThePublicBaseUrlSetting_IsReadFromPlaybackColonPublicBaseUrl_ByTheRealRegistration()
    {
        using ServiceProvider provider = CatalogModuleOver(ConfigurationOf(("Playback:PublicBaseUrl", "https://media.example.com/base/")));

        string url = provider.GetRequiredService<IPlaybackUrlBuilder>().Build(Movie).Value;

        Assert.Equal($"https://media.example.com/base/media/{Movie}/stream", url);
    }

    [Fact]
    public void ThePublicBaseUrlSetting_CanBeSuppliedByAnEnvironmentVariable()
    {
        // Playback__PublicBaseUrl is what a container's environment carries; the configuration system maps "__" to ":".
        // Set only for this test and restored: nothing else in this project reads that variable.
        const string variable = "Playback__PublicBaseUrl";
        string? previous = Environment.GetEnvironmentVariable(variable);
        Environment.SetEnvironmentVariable(variable, "http://from-environment.example:9090");

        try
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Database"] = "Data Source=:memory:" })
                .AddEnvironmentVariables()
                .Build();

            using ServiceProvider provider = CatalogModuleOver(configuration);

            Assert.Equal(
                $"http://from-environment.example:9090/media/{Movie}/stream",
                provider.GetRequiredService<IPlaybackUrlBuilder>().Build(Movie).Value);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
        }
    }

    [Fact]
    public void TheApplication_CanBeBuiltAndStartedWithoutThePublicBaseUrl_AndOnlyBuildingAUrlIsRefused()
    {
        // No "Playback" section at all: options resolution (which ValidateOnStart would force at startup) must not throw...
        using ServiceProvider provider = CatalogModuleOver(ConfigurationOf());

        Assert.Null(provider.GetRequiredService<IOptions<PlaybackOptions>>().Value.PublicBaseUrl);

        // ...and only the act of building a playback URL is refused.
        Assert.Equal(PlaybackErrors.PublicBaseUrlInvalid, provider.GetRequiredService<IPlaybackUrlBuilder>().Build(Movie).Error);
    }
}
