using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Polly.Timeout;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.MediaProcessing.Application.Streams;

namespace StrmManager.Modules.MediaProcessing.Infrastructure.Streams.FrostStream;

/// <summary>
/// Uses only the normal, public Stremio-compatible HTTP interface already validated by
/// the legacy prototype (GET /stream/series/{episodeId}.json, and the same convention's
/// GET /stream/movie/{imdbId}.json for movies) - no cookie extraction, no
/// captured sessions, no anti-bot bypass. See ADR-008.
/// </summary>
internal sealed partial class FrostStreamProvider(HttpClient httpClient, ILogger<FrostStreamProvider> logger)
    : IStreamProvider
{
    private const string ProviderName = "FrostStream";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public Task<Result<IReadOnlyList<StreamCandidate>>> GetEpisodeStreamsAsync(
        EpisodeStreamReference reference,
        CancellationToken cancellationToken = default) =>
        GetStreamsAsync("series", reference.ExternalId, cancellationToken);

    public Task<Result<IReadOnlyList<StreamCandidate>>> GetMovieStreamsAsync(
        MovieStreamReference reference,
        CancellationToken cancellationToken = default) =>
        GetStreamsAsync("movie", reference.ImdbId, cancellationToken);

    // One request/error-handling path for both media types - the Stremio endpoint differs
    // only in its type segment (/stream/series/... vs /stream/movie/...), so the same
    // client, resilience pipeline, DTOs, mapper and error taxonomy serve both.
    private async Task<Result<IReadOnlyList<StreamCandidate>>> GetStreamsAsync(
        string mediaType,
        string externalId,
        CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        try
        {
            // Uri.EscapeDataString handles the colon-delimited episode id (e.g.
            // "tt27497393:1:8" -> "tt27497393%3A1%3A8") - never hand-built/concatenated.
            string escapedId = Uri.EscapeDataString(externalId);

            using HttpResponseMessage response = await httpClient.GetAsync(
                $"stream/{mediaType}/{escapedId}.json",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                LogUnexpectedStatus(logger, externalId, (int)response.StatusCode, stopwatch.ElapsedMilliseconds);
                return Result.Failure<IReadOnlyList<StreamCandidate>>(StreamProviderErrors.ProviderUnavailable(ProviderName));
            }

            FrostStreamResponseDto? payload = await response.Content.ReadFromJsonAsync<FrostStreamResponseDto>(
                SerializerOptions,
                cancellationToken);

            if (payload is null)
            {
                LogInvalidResponse(logger, externalId, "empty response body");
                return Result.Failure<IReadOnlyList<StreamCandidate>>(StreamProviderErrors.InvalidResponse(ProviderName, "empty response body"));
            }

            IReadOnlyList<StreamCandidate> candidates = FrostStreamMapper.Map(payload);

            LogStreamsRetrieved(logger, externalId, candidates.Count, stopwatch.ElapsedMilliseconds);

            return Result.Success(candidates);
        }
        catch (TimeoutRejectedException)
        {
            LogTimeout(logger, externalId, stopwatch.ElapsedMilliseconds);
            return Result.Failure<IReadOnlyList<StreamCandidate>>(StreamProviderErrors.Timeout(ProviderName));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogTimeout(logger, externalId, stopwatch.ElapsedMilliseconds);
            return Result.Failure<IReadOnlyList<StreamCandidate>>(StreamProviderErrors.Timeout(ProviderName));
        }
        catch (HttpRequestException exception)
        {
            LogProviderUnreachable(logger, externalId, exception.Message);
            return Result.Failure<IReadOnlyList<StreamCandidate>>(StreamProviderErrors.ProviderUnavailable(ProviderName));
        }
        catch (JsonException exception)
        {
            LogInvalidResponse(logger, externalId, exception.Message);
            return Result.Failure<IReadOnlyList<StreamCandidate>>(StreamProviderErrors.InvalidResponse(ProviderName, "malformed JSON"));
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Retrieved {CandidateCount} stream candidates for {ExternalId} from FrostStream in {ElapsedMs}ms")]
    private static partial void LogStreamsRetrieved(ILogger logger, string externalId, int candidateCount, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "FrostStream returned status {StatusCode} for {ExternalId} ({ElapsedMs}ms)")]
    private static partial void LogUnexpectedStatus(ILogger logger, string externalId, int statusCode, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "FrostStream request for {ExternalId} timed out after {ElapsedMs}ms")]
    private static partial void LogTimeout(ILogger logger, string externalId, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "FrostStream request for {ExternalId} failed: {Reason}")]
    private static partial void LogProviderUnreachable(ILogger logger, string externalId, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "FrostStream returned an invalid response for {ExternalId}: {Reason}")]
    private static partial void LogInvalidResponse(ILogger logger, string externalId, string reason);
}
