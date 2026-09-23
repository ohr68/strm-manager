using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Polly.Timeout;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.Catalog.Application.Metadata;

namespace StrmManager.Modules.Catalog.Infrastructure.Metadata.Cinemeta;

/// <summary>
/// ICatalogProvider backed by Cinemeta's "Popular" movie catalog (GET catalog/movie/top.json) - the same addon
/// CinemetaMetadataProvider already uses, a different resource. Verified live before implementation (see the UI-2
/// report): the manifest advertises movie/top ("Popular"), the response is {"metas": [...]}, up to 50 items,
/// each already carrying a ready-to-use poster URL and a releaseInfo year in the same format meta/movie/{id}.json
/// uses. Same HTTP/error-handling shape as CinemetaMetadataProvider (own typed client, resilience handled one
/// layer up - see CatalogModule).
/// </summary>
internal sealed partial class CinemetaCatalogProvider(HttpClient httpClient, ILogger<CinemetaCatalogProvider> logger)
    : ICatalogProvider
{
    private const string ProviderName = "Cinemeta";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<Result<IReadOnlyList<CatalogMovie>>> GetPopularMoviesAsync(int limit, CancellationToken cancellationToken = default)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        try
        {
            using HttpResponseMessage response = await httpClient.GetAsync("catalog/movie/top.json", cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                LogUnexpectedStatus(logger, (int)response.StatusCode, stopwatch.ElapsedMilliseconds);
                return Result.Failure<IReadOnlyList<CatalogMovie>>(MetadataProviderErrors.ProviderUnavailable(ProviderName));
            }

            CinemetaCatalogResponseDto? payload = await response.Content.ReadFromJsonAsync<CinemetaCatalogResponseDto>(
                SerializerOptions,
                cancellationToken);

            if (payload?.Metas is null)
            {
                LogInvalidResponse(logger, "missing metas array");
                return Result.Failure<IReadOnlyList<CatalogMovie>>(MetadataProviderErrors.InvalidResponse(ProviderName, "missing metas array"));
            }

            List<CatalogMovie> movies = payload.Metas
                .Select(CinemetaCatalogMapper.Map)
                .Where(movie => movie is not null)
                .Select(movie => movie!)
                .Take(limit)
                .ToList();

            LogCatalogRetrieved(logger, movies.Count, stopwatch.ElapsedMilliseconds);
            return movies;
        }
        catch (TimeoutRejectedException)
        {
            LogTimeout(logger, stopwatch.ElapsedMilliseconds);
            return Result.Failure<IReadOnlyList<CatalogMovie>>(MetadataProviderErrors.Timeout(ProviderName));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogTimeout(logger, stopwatch.ElapsedMilliseconds);
            return Result.Failure<IReadOnlyList<CatalogMovie>>(MetadataProviderErrors.Timeout(ProviderName));
        }
        catch (HttpRequestException exception)
        {
            LogProviderUnreachable(logger, exception.Message);
            return Result.Failure<IReadOnlyList<CatalogMovie>>(MetadataProviderErrors.ProviderUnavailable(ProviderName));
        }
        catch (JsonException exception)
        {
            LogInvalidResponse(logger, exception.Message);
            return Result.Failure<IReadOnlyList<CatalogMovie>>(MetadataProviderErrors.InvalidResponse(ProviderName, "malformed JSON"));
        }
    }

    /// <summary>
    /// UI-5b movie search - Cinemeta's movie/top catalog with a "search" extra (verified live in UI-5a: GET
    /// catalog/movie/top/search={query}.json, HTTP 200 + metas:[] for no matches). Exactly one request, same
    /// response DTO/mapper as GetPopularMoviesAsync, Cinemeta's own result order preserved, limit applied
    /// client-side. The query text itself is never logged (only counts/timing), matching this provider's existing
    /// logging shape.
    /// </summary>
    public async Task<Result<IReadOnlyList<CatalogMovie>>> SearchMoviesAsync(string query, int limit, CancellationToken cancellationToken = default)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        string requestUri = $"catalog/movie/top/search={Uri.EscapeDataString(query)}.json";

        try
        {
            using HttpResponseMessage response = await httpClient.GetAsync(requestUri, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                LogSearchUnexpectedStatus(logger, (int)response.StatusCode, stopwatch.ElapsedMilliseconds);
                return Result.Failure<IReadOnlyList<CatalogMovie>>(MetadataProviderErrors.ProviderUnavailable(ProviderName));
            }

            CinemetaCatalogResponseDto? payload = await response.Content.ReadFromJsonAsync<CinemetaCatalogResponseDto>(
                SerializerOptions,
                cancellationToken);

            if (payload?.Metas is null)
            {
                LogSearchInvalidResponse(logger, "missing metas array");
                return Result.Failure<IReadOnlyList<CatalogMovie>>(MetadataProviderErrors.InvalidResponse(ProviderName, "missing metas array"));
            }

            List<CatalogMovie> movies = payload.Metas
                .Select(CinemetaCatalogMapper.Map)
                .Where(movie => movie is not null)
                .Select(movie => movie!)
                .Take(limit)
                .ToList();

            LogSearchRetrieved(logger, movies.Count, stopwatch.ElapsedMilliseconds);
            return movies;
        }
        catch (TimeoutRejectedException)
        {
            LogSearchTimeout(logger, stopwatch.ElapsedMilliseconds);
            return Result.Failure<IReadOnlyList<CatalogMovie>>(MetadataProviderErrors.Timeout(ProviderName));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogSearchTimeout(logger, stopwatch.ElapsedMilliseconds);
            return Result.Failure<IReadOnlyList<CatalogMovie>>(MetadataProviderErrors.Timeout(ProviderName));
        }
        catch (HttpRequestException exception)
        {
            LogSearchProviderUnreachable(logger, exception.Message);
            return Result.Failure<IReadOnlyList<CatalogMovie>>(MetadataProviderErrors.ProviderUnavailable(ProviderName));
        }
        catch (JsonException exception)
        {
            LogSearchInvalidResponse(logger, exception.Message);
            return Result.Failure<IReadOnlyList<CatalogMovie>>(MetadataProviderErrors.InvalidResponse(ProviderName, "malformed JSON"));
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Retrieved {MovieCount} popular movies from Cinemeta in {ElapsedMs}ms")]
    private static partial void LogCatalogRetrieved(ILogger logger, int movieCount, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cinemeta popular movies catalog returned status {StatusCode} ({ElapsedMs}ms)")]
    private static partial void LogUnexpectedStatus(ILogger logger, int statusCode, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cinemeta popular movies catalog request timed out after {ElapsedMs}ms")]
    private static partial void LogTimeout(ILogger logger, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cinemeta popular movies catalog request failed: {Reason}")]
    private static partial void LogProviderUnreachable(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cinemeta popular movies catalog returned an invalid response: {Reason}")]
    private static partial void LogInvalidResponse(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Retrieved {MovieCount} movie search results from Cinemeta in {ElapsedMs}ms")]
    private static partial void LogSearchRetrieved(ILogger logger, int movieCount, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cinemeta movie search returned status {StatusCode} ({ElapsedMs}ms)")]
    private static partial void LogSearchUnexpectedStatus(ILogger logger, int statusCode, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cinemeta movie search request timed out after {ElapsedMs}ms")]
    private static partial void LogSearchTimeout(ILogger logger, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cinemeta movie search request failed: {Reason}")]
    private static partial void LogSearchProviderUnreachable(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cinemeta movie search returned an invalid response: {Reason}")]
    private static partial void LogSearchInvalidResponse(ILogger logger, string reason);
}
