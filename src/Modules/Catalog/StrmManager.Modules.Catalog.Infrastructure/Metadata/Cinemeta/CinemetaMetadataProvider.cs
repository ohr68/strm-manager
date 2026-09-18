using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Polly.Timeout;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.Catalog.Application.Metadata;

namespace StrmManager.Modules.Catalog.Infrastructure.Metadata.Cinemeta;

internal sealed partial class CinemetaMetadataProvider(HttpClient httpClient, ILogger<CinemetaMetadataProvider> logger)
    : IMetadataProvider
{
    private const string ProviderName = "Cinemeta";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public Task<Result<SeriesMetadata>> GetSeriesAsync(string externalId, CancellationToken cancellationToken = default) =>
        GetMetaAsync(
            "series",
            externalId,
            CinemetaMetadataMapper.Map,
            CinemetaUnknownMedia.IsUnknownSeries,
            MetadataProviderErrors.SeriesNotFound(ProviderName, externalId),
            (series, elapsedMs) => LogSeriesRetrieved(logger, externalId, series.Episodes.Count, elapsedMs),
            cancellationToken);

    public Task<Result<MovieMetadata>> GetMovieAsync(string externalId, CancellationToken cancellationToken = default) =>
        GetMetaAsync(
            "movie",
            externalId,
            CinemetaMetadataMapper.MapMovie,
            CinemetaUnknownMedia.IsUnknownMovie,
            MetadataProviderErrors.MovieNotFound(ProviderName, externalId),
            (_, elapsedMs) => LogMovieRetrieved(logger, externalId, elapsedMs),
            cancellationToken);

    /// <summary>
    /// The single HTTP/error-handling path for GET meta/{type}/{id}.json - the two
    /// callers differ only in the media type, the mapper, how that type's HTTP-200
    /// "unknown id" response is recognized, and the not-found error.
    /// </summary>
    private async Task<Result<TMetadata>> GetMetaAsync<TMetadata>(
        string mediaType,
        string externalId,
        Func<CinemetaMetaDto, Result<TMetadata>> map,
        Func<CinemetaMetaResponseDto, bool> isUnknownMedia,
        Error notFoundError,
        Action<TMetadata, long> logRetrieved,
        CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        try
        {
            using HttpResponseMessage response = await httpClient.GetAsync(
                $"meta/{mediaType}/{Uri.EscapeDataString(externalId)}.json",
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                LogNotFound(logger, mediaType, externalId, stopwatch.ElapsedMilliseconds);
                return Result.Failure<TMetadata>(notFoundError);
            }

            if (!response.IsSuccessStatusCode)
            {
                LogUnexpectedStatus(logger, externalId, (int)response.StatusCode, stopwatch.ElapsedMilliseconds);
                return Result.Failure<TMetadata>(MetadataProviderErrors.ProviderUnavailable(ProviderName));
            }

            CinemetaMetaResponseDto? payload = await response.Content.ReadFromJsonAsync<CinemetaMetaResponseDto>(
                SerializerOptions,
                cancellationToken);

            // Cinemeta answers HTTP 200 (not 404) for ids it does not know - see
            // CinemetaUnknownMedia. Only the shape observed for THIS media type becomes
            // NotFound; every other unusable response stays InvalidResponse.
            if (payload is not null && isUnknownMedia(payload))
            {
                LogNotFound(logger, mediaType, externalId, stopwatch.ElapsedMilliseconds);
                return Result.Failure<TMetadata>(notFoundError);
            }

            if (payload?.Meta is null)
            {
                LogInvalidResponse(logger, externalId, "missing meta object");
                return Result.Failure<TMetadata>(MetadataProviderErrors.InvalidResponse(ProviderName, "missing meta object"));
            }

            Result<TMetadata> result = map(payload.Meta);

            if (result.IsSuccess)
            {
                logRetrieved(result.Value, stopwatch.ElapsedMilliseconds);
            }
            else
            {
                LogInvalidResponse(logger, externalId, result.Error.Description);
            }

            return result;
        }
        catch (TimeoutRejectedException)
        {
            LogTimeout(logger, externalId, stopwatch.ElapsedMilliseconds);
            return Result.Failure<TMetadata>(MetadataProviderErrors.Timeout(ProviderName));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogTimeout(logger, externalId, stopwatch.ElapsedMilliseconds);
            return Result.Failure<TMetadata>(MetadataProviderErrors.Timeout(ProviderName));
        }
        catch (HttpRequestException exception)
        {
            LogProviderUnreachable(logger, externalId, exception.Message);
            return Result.Failure<TMetadata>(MetadataProviderErrors.ProviderUnavailable(ProviderName));
        }
        catch (JsonException exception)
        {
            LogInvalidResponse(logger, externalId, exception.Message);
            return Result.Failure<TMetadata>(MetadataProviderErrors.InvalidResponse(ProviderName, "malformed JSON"));
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Retrieved {EpisodeCount} episodes for {ExternalId} from Cinemeta in {ElapsedMs}ms")]
    private static partial void LogSeriesRetrieved(ILogger logger, string externalId, int episodeCount, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Information, Message = "Retrieved movie metadata for {ExternalId} from Cinemeta in {ElapsedMs}ms")]
    private static partial void LogMovieRetrieved(ILogger logger, string externalId, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cinemeta has no {MediaType} metadata for {ExternalId} ({ElapsedMs}ms)")]
    private static partial void LogNotFound(ILogger logger, string mediaType, string externalId, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cinemeta returned status {StatusCode} for {ExternalId} ({ElapsedMs}ms)")]
    private static partial void LogUnexpectedStatus(ILogger logger, string externalId, int statusCode, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cinemeta request for {ExternalId} timed out after {ElapsedMs}ms")]
    private static partial void LogTimeout(ILogger logger, string externalId, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cinemeta request for {ExternalId} failed: {Reason}")]
    private static partial void LogProviderUnreachable(ILogger logger, string externalId, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cinemeta returned an invalid response for {ExternalId}: {Reason}")]
    private static partial void LogInvalidResponse(ILogger logger, string externalId, string reason);
}
