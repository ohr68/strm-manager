using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace StrmManager.Modules.Catalog.IntegrationTests.Infrastructure;

/// <summary>
/// Proves the app's GlobalExceptionHandler actually converts an unexpected exception
/// into a 500 ProblemDetails response, without adding a permanent "throw" endpoint to
/// the production app: an IStartupFilter injects one extra, test-only endpoint into a
/// dedicated WebApplicationFactory instance for this test class only.
/// </summary>
public class GlobalExceptionHandlerTests : IClassFixture<ApiWebApplicationFactory>
{
    private const string ThrowingPath = "/__test/throws-unexpected-exception";

    private readonly HttpClient _client;

    public GlobalExceptionHandlerTests(ApiWebApplicationFactory factory)
    {
        WebApplicationFactory<Program> throwingFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddSingleton<IStartupFilter>(new ThrowingEndpointStartupFilter(ThrowingPath))));

        _client = throwingFactory.CreateClient();
    }

    [Fact]
    public async Task UnexpectedException_IsConvertedToProblemDetailsResponse()
    {
        HttpResponseMessage response = await _client.GetAsync(ThrowingPath);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status500InternalServerError, problem.Status);
        Assert.False(string.IsNullOrWhiteSpace(problem.Title));
    }

    private sealed class ThrowingEndpointStartupFilter(string path) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            app =>
            {
                next(app);

                app.Use(async (context, nextMiddleware) =>
                {
                    if (context.Request.Path == path)
                    {
                        throw new InvalidOperationException("Simulated unexpected failure for GlobalExceptionHandler testing.");
                    }

                    await nextMiddleware(context);
                });
            };
    }
}
