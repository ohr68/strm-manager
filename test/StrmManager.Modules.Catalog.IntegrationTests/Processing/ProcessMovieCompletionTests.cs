using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.Catalog.Domain.Movies;
using StrmManager.Modules.Catalog.Domain.Shared;
using StrmManager.Modules.Catalog.Domain.SourceAttempts;
using StrmManager.Modules.Catalog.Domain.StrmFiles;
using StrmManager.Modules.Catalog.Infrastructure.Database;
using StrmManager.Modules.Catalog.IntegrationTests.Infrastructure;
using StrmManager.Modules.MediaProcessing.Application.Streams;
using static StrmManager.Modules.Catalog.IntegrationTests.Processing.MovieCandidates;
using static StrmManager.Modules.Catalog.IntegrationTests.Processing.ProcessMovieTestSupport;

namespace StrmManager.Modules.Catalog.IntegrationTests.Processing;

/// <summary>
/// Movie STRM completion, end to end over the REAL FileSystemStrmWriter and a throwaway temp root:
/// after the first approved candidate the .strm is written at movies/{Title} ({Year}) [imdbid-{ImdbId}]/
/// {Title} ({Year}).strm with the stable playback URL as its only content (never the candidate's provider URL, ADR-015),
/// the StrmFile is stored, the movie is Completed and the response says so - and never contains the URL. A handled writer failure is a
/// non-retryable Error with no false StrmFile and nothing left Validating.
/// </summary>
public class ProcessMovieCompletionTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ProcessMovieHost _host = new(factory);

    private static ProcessMovieResponse Parse(string body) =>
        JsonSerializer.Deserialize<ProcessMovieResponse>(body, JsonSerializerOptions.Web)!;

    private void ProvideApproved(StreamCandidate candidate)
    {
        _host.Provider = _ => Result.Success<IReadOnlyList<StreamCandidate>>([candidate]);
        _host.Validate = _ => Approval;
    }

    private async Task<StrmFile?> StrmFileAsync(Guid movieId)
    {
        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<CatalogDbContext>()
            .Set<StrmFile>().AsNoTracking().SingleOrDefaultAsync(f => f.MovieId == movieId);
    }

    [Fact]
    public async Task ProcessMovie_ApprovedCandidate_WritesTheMovieStrmAtTheExpectedPathWithTheStablePlaybackUrlAsContent()
    {
        string imdbId = NextImdbId();
        Guid movieId = await SeedMovieAsync(_host.Services, MediaStatus.Pending, title: "Completion Movie", year: 2024, imdbId: imdbId);
        StreamCandidate candidate = Candidate("Source A", year: 2024);
        ProvideApproved(candidate);

        (HttpResponseMessage response, string body) = await _host.ProcessAsync(movieId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        ProcessMovieResponse result = Parse(body);
        Assert.Equal("Completed", result.Status);
        Assert.Equal(1, result.Attempts);
        Assert.Equal(new SelectedSourceResponse("FrostStream", "Source A"), result.SelectedSource);
        Assert.Null(result.Reason);

        // The exact layout, relative to the configured root.
        string expectedRelative = Path.Combine("movies", $"Completion Movie (2024) [imdbid-{imdbId}]", "Completion Movie (2024).strm");
        Assert.NotNull(result.StrmPath);
        Assert.Equal(Path.GetFullPath(Path.Combine(_host.StrmRoot, expectedRelative)), Path.GetFullPath(result.StrmPath));
        Assert.Equal(expectedRelative, Path.GetRelativePath(_host.StrmRoot, result.StrmPath));

        // The file exists and holds exactly the STABLE playback URL - never the provider's candidate URL (compared without printing it).
        Assert.True(File.Exists(result.StrmPath));
        string strmContent = await File.ReadAllTextAsync(result.StrmPath);
        Assert.Equal($"{ApiWebApplicationFactory.TestPublicBaseUrl}/media/{movieId}/stream", strmContent);
        Assert.False(strmContent.Contains(candidate.Url, StringComparison.Ordinal), "the .strm must not contain the candidate URL");
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(result.StrmPath)!, "*.tmp")); // atomic write left nothing behind
    }

    [Fact]
    public async Task ProcessMovie_ApprovedCandidate_StoresTheStrmFileAndCompletesTheMovie_NeverLeavingItValidating()
    {
        Guid movieId = await SeedMovieAsync(_host.Services, MediaStatus.Pending, year: 2025);
        ProvideApproved(Candidate("Source A", year: 2025));

        (_, string body) = await _host.ProcessAsync(movieId);

        ProcessMovieResponse result = Parse(body);

        StrmFile? strmFile = await StrmFileAsync(movieId);
        Assert.NotNull(strmFile);
        Assert.Equal(movieId, strmFile.MovieId);
        Assert.Null(strmFile.EpisodeId);
        Assert.Equal(result.StrmPath, strmFile.Path);

        Movie movie = await LoadMovieAsync(_host.Services, movieId);
        DateTime finishedAtUtc = _host.Time.GetUtcNow().UtcDateTime;
        Assert.Equal(MediaStatus.Completed, movie.Status);
        Assert.Equal(1, movie.AttemptCount);
        Assert.Equal(finishedAtUtc, movie.LastAttemptAtUtc);
        Assert.Null(movie.NextAttemptAtUtc);
        Assert.Null(movie.LastError);

        SourceAttempt attempt = Assert.Single(await _host.AttemptsAsync(movieId));
        Assert.Equal(SourceAttemptResult.Approved, attempt.Result);
        Assert.Equal(TimeSpan.FromMinutes(101), attempt.Duration);
    }

    [Fact]
    public async Task ProcessMovie_ApprovedCandidate_ResponseAndLogsNeverContainTheUrl()
    {
        Guid movieId = await SeedMovieAsync(_host.Services, MediaStatus.Pending, year: 2025);
        ProvideApproved(Candidate("Source A", year: 2025));

        (_, string body) = await _host.ProcessAsync(movieId);

        Assert.DoesNotContain("media.example.test", body, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretMarker, body, StringComparison.Ordinal);
        Assert.All(_host.Logs.Lines, line =>
        {
            Assert.DoesNotContain("media.example.test", line, StringComparison.Ordinal);
            Assert.DoesNotContain(SecretMarker, line, StringComparison.Ordinal);
        });
        Assert.NotEmpty(_host.Logs.Lines);
    }

    [Fact]
    public async Task ProcessMovie_TwoDifferentMoviesWithTheSameTitleAndYear_GetSeparateFolders()
    {
        Guid first = await SeedMovieAsync(_host.Services, MediaStatus.Pending, title: "Twin Title", year: 2019, imdbId: NextImdbId());
        Guid second = await SeedMovieAsync(_host.Services, MediaStatus.Pending, title: "Twin Title", year: 2019, imdbId: NextImdbId());
        ProvideApproved(Candidate("Source A", year: 2019));

        ProcessMovieResponse firstResult = Parse((await _host.ProcessAsync(first)).Body);
        ProcessMovieResponse secondResult = Parse((await _host.ProcessAsync(second)).Body);

        Assert.Equal("Completed", firstResult.Status);
        Assert.Equal("Completed", secondResult.Status);
        Assert.NotEqual(firstResult.StrmPath, secondResult.StrmPath); // the IMDb id in the folder keeps them apart
        Assert.True(File.Exists(firstResult.StrmPath));
        Assert.True(File.Exists(secondResult.StrmPath));
    }

    [Fact]
    public async Task ProcessMovie_AStrmFileRowAlreadyExists_IsUpdatedNotDuplicated()
    {
        Guid movieId = await SeedMovieAsync(_host.Services, MediaStatus.Pending, year: 2025);
        await using (AsyncServiceScope scope = _host.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<IStrmFileRepository>().Insert(StrmFile.ForMovie(movieId, "/old/stale/path.strm", SeededAtUtc));
            await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().SaveChangesAsync();
        }

        ProvideApproved(Candidate("Source A", year: 2025));

        (_, string body) = await _host.ProcessAsync(movieId);

        ProcessMovieResponse result = Parse(body);
        await using AsyncServiceScope verify = _host.Services.CreateAsyncScope();
        List<StrmFile> rows = await verify.ServiceProvider.GetRequiredService<CatalogDbContext>()
            .Set<StrmFile>().Where(f => f.MovieId == movieId).ToListAsync();
        StrmFile only = Assert.Single(rows);
        Assert.Equal(result.StrmPath, only.Path);
        Assert.Equal(SeededAtUtc, only.CreatedAtUtc);
    }

    // --- Idempotency ---

    [Fact]
    public async Task ProcessMovie_AlreadyCompletedMovie_IsIdempotent_NoProviderNoFfprobeAndNoRewrite()
    {
        Guid movieId = await SeedMovieAsync(_host.Services, MediaStatus.Pending, year: 2025);
        ProvideApproved(Candidate("Source A", year: 2025));
        ProcessMovieResponse first = Parse((await _host.ProcessAsync(movieId)).Body);
        Assert.Equal("Completed", first.Status);

        // Mark the file so a rewrite would be visible, and count calls from here on.
        await File.WriteAllTextAsync(first.StrmPath!, "left alone");
        int providerCalls = _host.Events.Count(e => e == "external:stream-provider");
        int writerCalls = _host.Events.Count(e => e == "external:strm-writer");
        int validatorCalls = _host.Validated.Count;
        _host.Provider = _ => throw new InvalidOperationException("The provider must not be called for a Completed movie.");

        (HttpResponseMessage response, string body) = await _host.ProcessAsync(movieId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        ProcessMovieResponse second = Parse(body);
        Assert.Equal("Completed", second.Status);
        Assert.Equal(first.StrmPath, second.StrmPath);
        Assert.Equal(1, second.Attempts); // the movie's recorded attempt count

        Assert.Equal(providerCalls, _host.Events.Count(e => e == "external:stream-provider"));
        Assert.Equal(writerCalls, _host.Events.Count(e => e == "external:strm-writer"));
        Assert.Equal(validatorCalls, _host.Validated.Count);
        Assert.Equal("left alone", await File.ReadAllTextAsync(first.StrmPath!)); // not rewritten
        Assert.Single(await _host.AttemptsAsync(movieId)); // and no new attempt recorded
    }

    // --- Writer failure ---

    [Fact]
    public async Task ProcessMovie_WhenTheStrmCannotBeWritten_MarksAnUnretryableError_WithNoFalseStrmFile_AndNothingLeftValidating()
    {
        Guid movieId = await SeedMovieAsync(_host.Services, MediaStatus.Pending, year: 2025);
        ProvideApproved(Candidate("Source A", year: 2025));

        // A regular FILE where the "movies" directory must go: the real writer genuinely fails.
        Directory.CreateDirectory(_host.StrmRoot);
        string blocker = Path.Combine(_host.StrmRoot, "movies");
        bool blockerAlreadyThere = Directory.Exists(blocker);
        Assert.False(File.Exists(blocker));

        try
        {
            if (blockerAlreadyThere)
            {
                // Another test in this class already created movies/ - use a fresh root-relative failure instead.
                Directory.Delete(blocker, recursive: true);
            }

            await File.WriteAllTextAsync(blocker, "not a directory");

            (HttpResponseMessage response, string body) = await _host.ProcessAsync(movieId);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode); // an Error outcome is reported in the body, as for Episode
            ProcessMovieResponse result = Parse(body);
            Assert.Equal("Error", result.Status);
            Assert.Null(result.StrmPath);
            Assert.StartsWith("Failed to write .strm file:", result.Reason, StringComparison.Ordinal);
            Assert.Equal(1, result.Attempts);

            Movie movie = await LoadMovieAsync(_host.Services, movieId);
            Assert.Equal(MediaStatus.Error, movie.Status); // not Completed, and not left Validating
            Assert.Null(movie.NextAttemptAtUtc); // non-retryable: a filesystem problem is not hammered, as for Episode
            Assert.Equal(1, movie.AttemptCount);
            Assert.StartsWith("Failed to write .strm file:", movie.LastError, StringComparison.Ordinal);
            Assert.Null(await StrmFileAsync(movieId)); // no false StrmFile

            // The approved evaluation that led here is still recorded.
            SourceAttempt attempt = Assert.Single(await _host.AttemptsAsync(movieId));
            Assert.Equal(SourceAttemptResult.Approved, attempt.Result);

            // No URL anywhere: response, stored error, attempt, logs.
            string[] texts = [body, movie.LastError!, $"{attempt.Provider} {attempt.SourceName} {attempt.FailureReason}", .. _host.Logs.Lines];
            Assert.All(texts, text =>
            {
                Assert.DoesNotContain("media.example.test", text, StringComparison.Ordinal);
                Assert.DoesNotContain(SecretMarker, text, StringComparison.Ordinal);
            });
        }
        finally
        {
            File.Delete(blocker); // the root is shared by this class's other tests
        }
    }
}
