using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.StrmManager.StrmManagerClient;

/// <summary>
/// STRM Manager HTTP client. Registered as a typed client (see PluginServiceRegistrator) so its <see
/// cref="HttpClient"/> already carries a validated BaseAddress (or none, if configuration is invalid/unset) - this
/// class never reads plugin configuration or Jellyfin statics itself, which is what keeps it unit-testable with a
/// fake HttpMessageHandler and no running plugin/host.
///
/// Logging here is structured (LoggerMessage source-generated delegates, matching the rest of this project) and
/// stays within the same discipline: IMDb id and the returned movie id/status are fine to log; a response body, a
/// query string, or anything that could be a provider or media URL never is. STRM Manager's own MovieResponse
/// contract has no such field, but this class does not rely on that - it only ever logs the specific values named
/// above, never response content wholesale.
/// </summary>
public sealed partial class StrmManagerClient(HttpClient httpClient, ILogger<StrmManagerClient> logger) : IStrmManagerClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<MovieLookupResult> GetByImdbIdAsync(string imdbId, CancellationToken cancellationToken)
    {
        if (httpClient.BaseAddress is null)
        {
            LogNoBaseUrl(imdbId);
            return new MovieLookupResult.Error("STRM Manager BaseUrl is not configured.");
        }

        var requestUri = new Uri($"api/movies/by-imdb/{Uri.EscapeDataString(imdbId)}", UriKind.Relative);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient represents its OWN timeout as an OperationCanceledException too, indistinguishable from a
            // caller cancellation except by checking the caller's token - which is exactly what this guard does. If
            // the caller's token WAS the cause, the exception is deliberately left to propagate unchanged below.
            LogTimedOut(imdbId);
            return new MovieLookupResult.Unreachable("Request timed out.");
        }
        catch (HttpRequestException exception)
        {
            LogUnreachable(exception, imdbId, exception.GetType().Name);
            return new MovieLookupResult.Unreachable(exception.GetType().Name);
        }

        using (response)
        {
            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    return await ReadFoundAsync(response, imdbId, cancellationToken).ConfigureAwait(false);
                case HttpStatusCode.NotFound:
                    LogNotFound(imdbId);
                    return new MovieLookupResult.NotFound();
                case HttpStatusCode.BadRequest:
                    LogInvalid(imdbId);
                    return new MovieLookupResult.Invalid();
                default:
                    LogUnexpectedStatus((int)response.StatusCode, imdbId);
                    return new MovieLookupResult.Error($"Unexpected HTTP status {(int)response.StatusCode}.");
            }
        }
    }

    private async Task<MovieLookupResult> ReadFoundAsync(HttpResponseMessage response, string imdbId, CancellationToken cancellationToken)
    {
        MovieResponseDto? dto;
        try
        {
            dto = await response.Content.ReadFromJsonAsync<MovieResponseDto>(JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            LogMalformedBody(exception, imdbId);
            return new MovieLookupResult.Error("Malformed response body.");
        }

        // Parsed without throwing, but structurally not what MovieResponse looks like (e.g. "{}") - still not usable.
        if (dto is null || dto.Id == Guid.Empty || string.IsNullOrWhiteSpace(dto.ImdbId) || string.IsNullOrWhiteSpace(dto.Status))
        {
            LogUnexpectedShape(imdbId);
            return new MovieLookupResult.Error("Unexpected response shape.");
        }

        LogFound(dto.Id, imdbId, dto.Status);
        return new MovieLookupResult.Found(dto.Id, dto.ImdbId, dto.Title, dto.Year, dto.Status);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "STRM Manager lookup for {ImdbId} skipped: no valid BaseUrl is configured")]
    private partial void LogNoBaseUrl(string imdbId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "STRM Manager lookup for {ImdbId} timed out")]
    private partial void LogTimedOut(string imdbId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "STRM Manager lookup for {ImdbId} failed to reach the server ({ExceptionType})")]
    private partial void LogUnreachable(Exception exception, string imdbId, string exceptionType);

    [LoggerMessage(Level = LogLevel.Information, Message = "STRM Manager has no movie for {ImdbId}")]
    private partial void LogNotFound(string imdbId);

    [LoggerMessage(Level = LogLevel.Information, Message = "STRM Manager rejected {ImdbId} as an invalid IMDb id")]
    private partial void LogInvalid(string imdbId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "STRM Manager returned unexpected status {StatusCode} for {ImdbId}")]
    private partial void LogUnexpectedStatus(int statusCode, string imdbId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "STRM Manager returned a malformed 200 body for {ImdbId}")]
    private partial void LogMalformedBody(Exception exception, string imdbId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "STRM Manager returned an unexpected 200 body shape for {ImdbId}")]
    private partial void LogUnexpectedShape(string imdbId);

    [LoggerMessage(Level = LogLevel.Information, Message = "STRM Manager movie {MovieId} for {ImdbId} is {Status}")]
    private partial void LogFound(Guid movieId, string imdbId, string status);

    public async Task<AddMovieResult> AddMovieAsync(string imdbId, CancellationToken cancellationToken)
    {
        if (httpClient.BaseAddress is null)
        {
            LogAddNoBaseUrl(imdbId);
            return new AddMovieResult.Error("STRM Manager BaseUrl is not configured.");
        }

        // The entire request body STRM Manager's POST /api/movies accepts or needs (see AddMovieCommand's own
        // remark: title/year always come from its metadata provider, never from a caller).
        using JsonContent content = JsonContent.Create(new { imdbId }, options: JsonOptions);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.PostAsync(new Uri("api/movies", UriKind.Relative), content, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Same reasoning as GetByImdbIdAsync: HttpClient's own timeout also throws OperationCanceledException, so
            // only a cause OTHER than the caller's token is handled here; a caller cancellation propagates unchanged.
            LogAddTimedOut(imdbId);
            return new AddMovieResult.Unreachable("Request timed out.");
        }
        catch (HttpRequestException exception)
        {
            LogAddUnreachable(exception, imdbId, exception.GetType().Name);
            return new AddMovieResult.Unreachable(exception.GetType().Name);
        }

        using (response)
        {
            switch (response.StatusCode)
            {
                case HttpStatusCode.Created:
                    return await ReadCreatedAsync(response, imdbId, cancellationToken).ConfigureAwait(false);
                case HttpStatusCode.Conflict:
                    LogAlreadyExists(imdbId);
                    return new AddMovieResult.AlreadyExists();
                case HttpStatusCode.BadRequest:
                    LogAddInvalid(imdbId);
                    return new AddMovieResult.Invalid();
                case HttpStatusCode.NotFound:
                    LogAddNotFound(imdbId);
                    return new AddMovieResult.NotFound();
                default:
                    LogAddUnexpectedStatus((int)response.StatusCode, imdbId);
                    return new AddMovieResult.Error($"Unexpected HTTP status {(int)response.StatusCode}.");
            }
        }
    }

    private async Task<AddMovieResult> ReadCreatedAsync(HttpResponseMessage response, string imdbId, CancellationToken cancellationToken)
    {
        CreatedResponseDto? dto;
        try
        {
            dto = await response.Content.ReadFromJsonAsync<CreatedResponseDto>(JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            LogAddMalformedBody(exception, imdbId);
            return new AddMovieResult.Error("Malformed response body.");
        }

        if (dto is null || dto.Id == Guid.Empty)
        {
            LogAddUnexpectedShape(imdbId);
            return new AddMovieResult.Error("Unexpected response shape.");
        }

        LogCreated(dto.Id, imdbId);
        return new AddMovieResult.Created(dto.Id);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "STRM Manager add for {ImdbId} skipped: no valid BaseUrl is configured")]
    private partial void LogAddNoBaseUrl(string imdbId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "STRM Manager add for {ImdbId} timed out")]
    private partial void LogAddTimedOut(string imdbId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "STRM Manager add for {ImdbId} failed to reach the server ({ExceptionType})")]
    private partial void LogAddUnreachable(Exception exception, string imdbId, string exceptionType);

    [LoggerMessage(Level = LogLevel.Information, Message = "STRM Manager movie {MovieId} created for {ImdbId}")]
    private partial void LogCreated(Guid movieId, string imdbId);

    [LoggerMessage(Level = LogLevel.Information, Message = "STRM Manager already has a movie for {ImdbId}")]
    private partial void LogAlreadyExists(string imdbId);

    [LoggerMessage(Level = LogLevel.Information, Message = "STRM Manager rejected adding {ImdbId} as invalid")]
    private partial void LogAddInvalid(string imdbId);

    [LoggerMessage(Level = LogLevel.Information, Message = "STRM Manager's metadata provider has no data for {ImdbId}")]
    private partial void LogAddNotFound(string imdbId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "STRM Manager returned unexpected status {StatusCode} adding {ImdbId}")]
    private partial void LogAddUnexpectedStatus(int statusCode, string imdbId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "STRM Manager returned a malformed 201 body for {ImdbId}")]
    private partial void LogAddMalformedBody(Exception exception, string imdbId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "STRM Manager returned an unexpected 201 body shape for {ImdbId}")]
    private partial void LogAddUnexpectedShape(string imdbId);

    /// <summary>Only the fields MovieLookupResult.Found needs; every other MovieResponse field is ignored.</summary>
    private sealed class MovieResponseDto
    {
        public Guid Id { get; init; }

        public string? ImdbId { get; init; }

        public string Title { get; init; } = string.Empty;

        public int Year { get; init; }

        public string Status { get; init; } = string.Empty;
    }

    /// <summary>POST /api/movies's success body is just {"id": "..."} - nothing else to read.</summary>
    private sealed class CreatedResponseDto
    {
        public Guid Id { get; init; }
    }
}
