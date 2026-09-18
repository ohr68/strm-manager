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

    public async Task<Result<SeriesMetadata>> GetSeriesAsync(string externalId, CancellationToken cancellationToken = default)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        try
        {
            using HttpResponseMessage response = await httpClient.GetAsync(
                $"meta/series/{Uri.EscapeDataString(externalId)}.json",
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                LogSeriesNotFound(logger, externalId, stopwatch.ElapsedMilliseconds);
                return Result.Failure<SeriesMetadata>(MetadataProviderErrors.SeriesNotFound(ProviderName, externalId));
            }

            if (!response.IsSuccessStatusCode)
            {
                LogUnexpectedStatus(logger, externalId, (int)response.StatusCode, stopwatch.ElapsedMilliseconds);
                return Result.Failure<SeriesMetadata>(MetadataProviderErrors.ProviderUnavailable(ProviderName));
            }

            CinemetaSeriesResponseDto? payload = await response.Content.ReadFromJsonAsync<CinemetaSeriesResponseDto>(
                SerializerOptions,
                cancellationToken);

            if (payload?.Meta is null)
            {
                LogInvalidResponse(logger, externalId, "missing meta object");
                return Result.Failure<SeriesMetadata>(MetadataProviderErrors.InvalidResponse(ProviderName, "missing meta object"));
            }

            Result<SeriesMetadata> result = CinemetaMetadataMapper.Map(payload.Meta);

            if (result.IsSuccess)
            {
                LogSeriesRetrieved(logger, externalId, result.Value.Episodes.Count, stopwatch.ElapsedMilliseconds);
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
            return Result.Failure<SeriesMetadata>(MetadataProviderErrors.Timeout(ProviderName));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogTimeout(logger, externalId, stopwatch.ElapsedMilliseconds);
            return Result.Failure<SeriesMetadata>(MetadataProviderErrors.Timeout(ProviderName));
        }
        catch (HttpRequestException exception)
        {
            LogProviderUnreachable(logger, externalId, exception.Message);
            return Result.Failure<SeriesMetadata>(MetadataProviderErrors.ProviderUnavailable(ProviderName));
        }
        catch (JsonException exception)
        {
            LogInvalidResponse(logger, externalId, exception.Message);
            return Result.Failure<SeriesMetadata>(MetadataProviderErrors.InvalidResponse(ProviderName, "malformed JSON"));
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Retrieved {EpisodeCount} episodes for {ExternalId} from Cinemeta in {ElapsedMs}ms")]
    private static partial void LogSeriesRetrieved(ILogger logger, string externalId, int episodeCount, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cinemeta has no metadata for {ExternalId} ({ElapsedMs}ms)")]
    private static partial void LogSeriesNotFound(ILogger logger, string externalId, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cinemeta returned status {StatusCode} for {ExternalId} ({ElapsedMs}ms)")]
    private static partial void LogUnexpectedStatus(ILogger logger, string externalId, int statusCode, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cinemeta request for {ExternalId} timed out after {ElapsedMs}ms")]
    private static partial void LogTimeout(ILogger logger, string externalId, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cinemeta request for {ExternalId} failed: {Reason}")]
    private static partial void LogProviderUnreachable(ILogger logger, string externalId, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cinemeta returned an invalid response for {ExternalId}: {Reason}")]
    private static partial void LogInvalidResponse(ILogger logger, string externalId, string reason);
}
