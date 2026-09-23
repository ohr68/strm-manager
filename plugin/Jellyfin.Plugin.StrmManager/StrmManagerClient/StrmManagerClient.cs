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
public sealed partial class StrmManagerClient(HttpClient httpClient, IHttpClientFactory httpClientFactory, ILogger<StrmManagerClient> logger) : IStrmManagerClient
{
    /// <summary>
    /// Name of the dedicated named HttpClient (see PluginServiceRegistrator) that ProcessMovieAsync alone uses -
    /// same BaseUrl rules as the ordinary typed <see cref="HttpClient"/> above, but with no client-side timeout,
    /// since that POST can legitimately run far longer than the fast metadata calls this class also makes.
    /// </summary>
    public const string ProcessMovieHttpClientName = "StrmManagerProcessMovie";

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

    public async Task<ProcessMovieResult> ProcessMovieAsync(Guid movieId, CancellationToken cancellationToken)
    {
        // A fresh instance every call, by design (see IHttpClientFactory) - not held as a field, so the same
        // BaseUrl-refresh behavior the ordinary typed client already has applies here too, on every invocation.
        HttpClient processMovieClient = httpClientFactory.CreateClient(ProcessMovieHttpClientName);

        if (processMovieClient.BaseAddress is null)
        {
            LogProcessNoBaseUrl(movieId);
            return new ProcessMovieResult.Error("STRM Manager BaseUrl is not configured.");
        }

        var requestUri = new Uri($"api/movies/{movieId:D}/process", UriKind.Relative);

        HttpResponseMessage response;
        try
        {
            response = await processMovieClient.PostAsync(requestUri, content: null, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Same reasoning as GetByImdbIdAsync/AddMovieAsync: HttpClient's own timeout also throws
            // OperationCanceledException, so only a cause OTHER than the caller's token is handled here.
            LogProcessTimedOut(movieId);
            return new ProcessMovieResult.Unreachable("Request timed out.");
        }
        catch (HttpRequestException exception)
        {
            LogProcessUnreachable(exception, movieId, exception.GetType().Name);
            return new ProcessMovieResult.Unreachable(exception.GetType().Name);
        }

        using (response)
        {
            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    return await ReadProcessedAsync(response, movieId, cancellationToken).ConfigureAwait(false);
                case HttpStatusCode.Conflict:
                    return await ReadConflictAsync(response, movieId, cancellationToken).ConfigureAwait(false);
                case HttpStatusCode.NotFound:
                    LogProcessNotFound(movieId);
                    return new ProcessMovieResult.NotFound();
                default:
                    LogProcessUnexpectedStatus((int)response.StatusCode, movieId);
                    return new ProcessMovieResult.Error($"Unexpected HTTP status {(int)response.StatusCode}.");
            }
        }
    }

    private async Task<ProcessMovieResult> ReadProcessedAsync(HttpResponseMessage response, Guid movieId, CancellationToken cancellationToken)
    {
        ProcessMovieResponseDto? dto;
        try
        {
            dto = await response.Content.ReadFromJsonAsync<ProcessMovieResponseDto>(JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            LogProcessMalformedBody(exception, movieId);
            return new ProcessMovieResult.Error("Malformed response body.");
        }

        if (dto is null || dto.MovieId == Guid.Empty || string.IsNullOrWhiteSpace(dto.Status))
        {
            LogProcessUnexpectedShape(movieId);
            return new ProcessMovieResult.Error("Unexpected response shape.");
        }

        switch (dto.Status)
        {
            case "Completed":
                LogProcessCompleted(dto.MovieId, dto.Attempts);
                return new ProcessMovieResult.Completed(dto.MovieId, dto.Attempts, dto.SelectedSource?.Provider, dto.SelectedSource?.Name, dto.StrmPath);
            case "Unavailable":
                LogProcessUnavailable(dto.MovieId, dto.Attempts);
                return new ProcessMovieResult.Unavailable(dto.Attempts, dto.Reason);
            case "Error":
                LogProcessingFailed(dto.MovieId, dto.Attempts);
                return new ProcessMovieResult.ProcessingFailed(dto.Attempts, dto.Reason);
            default:
                LogProcessUnexpectedShape(movieId);
                return new ProcessMovieResult.Error($"Unexpected processing status '{dto.Status}'.");
        }
    }

    /// <summary>
    /// A 409 is either Movies.AlreadyBeingProcessed or Movie.InvalidTransition - the standard ProblemDetails body's
    /// "title" carries the backend's own error code (see ApiResults.Problem), which is the only way to tell them
    /// apart at this client's boundary.
    /// </summary>
    private async Task<ProcessMovieResult> ReadConflictAsync(HttpResponseMessage response, Guid movieId, CancellationToken cancellationToken)
    {
        ProblemDetailsDto? problem;
        try
        {
            problem = await response.Content.ReadFromJsonAsync<ProblemDetailsDto>(JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            LogProcessMalformedBody(exception, movieId);
            return new ProcessMovieResult.Error("Malformed response body.");
        }

        switch (problem?.Title)
        {
            case "Movies.AlreadyBeingProcessed":
                LogProcessAlreadyBeingProcessed(movieId);
                return new ProcessMovieResult.AlreadyBeingProcessed();
            case "Movie.InvalidTransition":
                LogProcessInvalidTransition(movieId);
                return new ProcessMovieResult.InvalidTransition();
            default:
                LogProcessUnexpectedShape(movieId);
                return new ProcessMovieResult.Error("Unexpected 409 response shape.");
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "STRM Manager process for movie {MovieId} skipped: no valid BaseUrl is configured")]
    private partial void LogProcessNoBaseUrl(Guid movieId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "STRM Manager process for movie {MovieId} timed out")]
    private partial void LogProcessTimedOut(Guid movieId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "STRM Manager process for movie {MovieId} failed to reach the server ({ExceptionType})")]
    private partial void LogProcessUnreachable(Exception exception, Guid movieId, string exceptionType);

    [LoggerMessage(Level = LogLevel.Information, Message = "STRM Manager process for movie {MovieId} completed after {Attempts} attempt(s)")]
    private partial void LogProcessCompleted(Guid movieId, int attempts);

    [LoggerMessage(Level = LogLevel.Information, Message = "STRM Manager process for movie {MovieId} reported Unavailable after {Attempts} attempt(s)")]
    private partial void LogProcessUnavailable(Guid movieId, int attempts);

    [LoggerMessage(Level = LogLevel.Warning, Message = "STRM Manager process for movie {MovieId} reported a processing failure after {Attempts} attempt(s)")]
    private partial void LogProcessingFailed(Guid movieId, int attempts);

    [LoggerMessage(Level = LogLevel.Information, Message = "STRM Manager movie {MovieId} is already being processed by another request")]
    private partial void LogProcessAlreadyBeingProcessed(Guid movieId);

    [LoggerMessage(Level = LogLevel.Information, Message = "STRM Manager rejected processing movie {MovieId}: invalid state transition")]
    private partial void LogProcessInvalidTransition(Guid movieId);

    [LoggerMessage(Level = LogLevel.Information, Message = "STRM Manager has no movie {MovieId} to process")]
    private partial void LogProcessNotFound(Guid movieId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "STRM Manager returned unexpected status {StatusCode} processing movie {MovieId}")]
    private partial void LogProcessUnexpectedStatus(int statusCode, Guid movieId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "STRM Manager returned a malformed response body processing movie {MovieId}")]
    private partial void LogProcessMalformedBody(Exception exception, Guid movieId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "STRM Manager returned an unexpected response shape processing movie {MovieId}")]
    private partial void LogProcessUnexpectedShape(Guid movieId);

    /// <summary>Only the fields ProcessMovieResult needs; every other backend ProcessMovieResult field is ignored.</summary>
    private sealed class ProcessMovieResponseDto
    {
        public Guid MovieId { get; init; }

        public string Status { get; init; } = string.Empty;

        public int Attempts { get; init; }

        public SelectedSourceDto? SelectedSource { get; init; }

        public string? StrmPath { get; init; }

        public string? Reason { get; init; }
    }

    private sealed class SelectedSourceDto
    {
        public string? Provider { get; init; }

        public string? Name { get; init; }
    }

    /// <summary>Only the "title" field of the standard ProblemDetails body - that is ApiResults.Problem's error code.</summary>
    private sealed class ProblemDetailsDto
    {
        public string? Title { get; init; }
    }

    public async Task<CatalogMoviesResult> GetPopularMoviesAsync(CancellationToken cancellationToken)
    {
        if (httpClient.BaseAddress is null)
        {
            LogCatalogNoBaseUrl();
            return new CatalogMoviesResult.Error("STRM Manager BaseUrl is not configured.");
        }

        HttpResponseMessage response;
        try
        {
            response = await httpClient.GetAsync(new Uri("api/catalog/movies/popular", UriKind.Relative), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogCatalogTimedOut();
            return new CatalogMoviesResult.Unreachable("Request timed out.");
        }
        catch (HttpRequestException exception)
        {
            LogCatalogUnreachable(exception, exception.GetType().Name);
            return new CatalogMoviesResult.Unreachable(exception.GetType().Name);
        }

        using (response)
        {
            if (response.StatusCode != HttpStatusCode.OK)
            {
                LogCatalogUnexpectedStatus((int)response.StatusCode);
                return new CatalogMoviesResult.Error($"Unexpected HTTP status {(int)response.StatusCode}.");
            }

            return await ReadCatalogAsync(response, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<CatalogMoviesResult> ReadCatalogAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        PopularMoviesResponseDto? dto;
        try
        {
            dto = await response.Content.ReadFromJsonAsync<PopularMoviesResponseDto>(JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            LogCatalogMalformedBody(exception);
            return new CatalogMoviesResult.Error("Malformed response body.");
        }

        if (dto?.Movies is null)
        {
            LogCatalogUnexpectedShape();
            return new CatalogMoviesResult.Error("Unexpected response shape.");
        }

        // Skip entries missing what a catalog card needs rather than failing the whole row - mirrors
        // CinemetaCatalogMapper's own skip-not-fail rule on the backend.
        List<CatalogMovie> movies = dto.Movies
            .Where(item => !string.IsNullOrWhiteSpace(item.ExternalId) && !string.IsNullOrWhiteSpace(item.Title))
            .Select(item => new CatalogMovie(item.ExternalId!, item.Title!, item.Year, item.PosterUrl))
            .ToList();

        LogCatalogRetrieved(movies.Count);
        return new CatalogMoviesResult.Found(movies);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "STRM Manager popular movies catalog request skipped: no valid BaseUrl is configured")]
    private partial void LogCatalogNoBaseUrl();

    [LoggerMessage(Level = LogLevel.Warning, Message = "STRM Manager popular movies catalog request timed out")]
    private partial void LogCatalogTimedOut();

    [LoggerMessage(Level = LogLevel.Warning, Message = "STRM Manager popular movies catalog request failed to reach the server ({ExceptionType})")]
    private partial void LogCatalogUnreachable(Exception exception, string exceptionType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "STRM Manager returned unexpected status {StatusCode} for the popular movies catalog")]
    private partial void LogCatalogUnexpectedStatus(int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "STRM Manager returned a malformed popular movies catalog body")]
    private partial void LogCatalogMalformedBody(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "STRM Manager returned an unexpected popular movies catalog body shape")]
    private partial void LogCatalogUnexpectedShape();

    [LoggerMessage(Level = LogLevel.Information, Message = "STRM Manager returned {MovieCount} popular movies")]
    private partial void LogCatalogRetrieved(int movieCount);

    /// <summary>Only the fields CatalogMovie needs; matches the backend's own CatalogMovieResponse.</summary>
    private sealed class PopularMoviesResponseDto
    {
        public List<CatalogMovieDto>? Movies { get; set; }
    }

    private sealed class CatalogMovieDto
    {
        public string? ExternalId { get; set; }

        public string? Title { get; set; }

        public int? Year { get; set; }

        public string? PosterUrl { get; set; }
    }

    public async Task<MovieRowsResult> GetMovieRowsAsync(CancellationToken cancellationToken)
    {
        if (httpClient.BaseAddress is null)
        {
            LogRowsNoBaseUrl();
            return new MovieRowsResult.Error("STRM Manager BaseUrl is not configured.");
        }

        HttpResponseMessage response;
        try
        {
            response = await httpClient.GetAsync(new Uri("api/catalog/movies/rows", UriKind.Relative), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogRowsTimedOut();
            return new MovieRowsResult.Unreachable("Request timed out.");
        }
        catch (HttpRequestException exception)
        {
            LogRowsUnreachable(exception, exception.GetType().Name);
            return new MovieRowsResult.Unreachable(exception.GetType().Name);
        }

        using (response)
        {
            if (response.StatusCode != HttpStatusCode.OK)
            {
                LogRowsUnexpectedStatus((int)response.StatusCode);
                return new MovieRowsResult.Error($"Unexpected HTTP status {(int)response.StatusCode}.");
            }

            return await ReadRowsAsync(response, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<MovieRowsResult> ReadRowsAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        MovieRowsResponseDto? dto;
        try
        {
            dto = await response.Content.ReadFromJsonAsync<MovieRowsResponseDto>(JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            LogRowsMalformedBody(exception);
            return new MovieRowsResult.Error("Malformed response body.");
        }

        if (dto?.Rows is null)
        {
            LogRowsUnexpectedShape();
            return new MovieRowsResult.Error("Unexpected response shape.");
        }

        // Skip a row missing what it needs (id/name) rather than failing the whole response - same skip-not-fail
        // rule GetPopularMoviesAsync/CinemetaCatalogMapper already follow. A row's own movies follow the same rule.
        List<MovieRow> rows = dto.Rows
            .Where(row => !string.IsNullOrWhiteSpace(row.Id) && !string.IsNullOrWhiteSpace(row.Name))
            .Select(row => new MovieRow(
                row.Id!,
                row.Name!,
                (row.Movies ?? [])
                    .Where(item => !string.IsNullOrWhiteSpace(item.ExternalId) && !string.IsNullOrWhiteSpace(item.Title))
                    .Select(item => new CatalogMovie(item.ExternalId!, item.Title!, item.Year, item.PosterUrl))
                    .ToList()))
            .ToList();

        LogRowsRetrieved(rows.Count);
        return new MovieRowsResult.Found(rows);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "STRM Manager movie rows request skipped: no valid BaseUrl is configured")]
    private partial void LogRowsNoBaseUrl();

    [LoggerMessage(Level = LogLevel.Warning, Message = "STRM Manager movie rows request timed out")]
    private partial void LogRowsTimedOut();

    [LoggerMessage(Level = LogLevel.Warning, Message = "STRM Manager movie rows request failed to reach the server ({ExceptionType})")]
    private partial void LogRowsUnreachable(Exception exception, string exceptionType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "STRM Manager returned unexpected status {StatusCode} for movie rows")]
    private partial void LogRowsUnexpectedStatus(int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "STRM Manager returned a malformed movie rows body")]
    private partial void LogRowsMalformedBody(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "STRM Manager returned an unexpected movie rows body shape")]
    private partial void LogRowsUnexpectedShape();

    [LoggerMessage(Level = LogLevel.Information, Message = "STRM Manager returned {RowCount} movie rows")]
    private partial void LogRowsRetrieved(int rowCount);

    /// <summary>Only the fields MovieRow needs; matches the backend's own MovieRowsResponse.</summary>
    private sealed class MovieRowsResponseDto
    {
        public List<MovieRowDto>? Rows { get; set; }
    }

    private sealed class MovieRowDto
    {
        public string? Id { get; set; }

        public string? Name { get; set; }

        public List<CatalogMovieDto>? Movies { get; set; }
    }

    public async Task<SearchMoviesResult> SearchMoviesAsync(string query, CancellationToken cancellationToken)
    {
        if (httpClient.BaseAddress is null)
        {
            LogSearchNoBaseUrl();
            return new SearchMoviesResult.Error("STRM Manager BaseUrl is not configured.");
        }

        var requestUri = new Uri("api/catalog/movies/search?query=" + Uri.EscapeDataString(query), UriKind.Relative);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogSearchTimedOut();
            return new SearchMoviesResult.Unreachable("Request timed out.");
        }
        catch (HttpRequestException exception)
        {
            LogSearchUnreachable(exception, exception.GetType().Name);
            return new SearchMoviesResult.Unreachable(exception.GetType().Name);
        }

        using (response)
        {
            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    return await ReadSearchAsync(response, cancellationToken).ConfigureAwait(false);
                case HttpStatusCode.BadRequest:
                    LogSearchInvalid();
                    return new SearchMoviesResult.Invalid();
                default:
                    LogSearchUnexpectedStatus((int)response.StatusCode);
                    return new SearchMoviesResult.Error($"Unexpected HTTP status {(int)response.StatusCode}.");
            }
        }
    }

    // Reuses PopularMoviesResponseDto/CatalogMovieDto - the backend's search response is the same
    // {"movies": [...]} shape as GetPopularMoviesAsync/GetMovieRowsAsync's cards.
    private async Task<SearchMoviesResult> ReadSearchAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        PopularMoviesResponseDto? dto;
        try
        {
            dto = await response.Content.ReadFromJsonAsync<PopularMoviesResponseDto>(JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            LogSearchMalformedBody(exception);
            return new SearchMoviesResult.Error("Malformed response body.");
        }

        if (dto?.Movies is null)
        {
            LogSearchUnexpectedShape();
            return new SearchMoviesResult.Error("Unexpected response shape.");
        }

        List<CatalogMovie> movies = dto.Movies
            .Where(item => !string.IsNullOrWhiteSpace(item.ExternalId) && !string.IsNullOrWhiteSpace(item.Title))
            .Select(item => new CatalogMovie(item.ExternalId!, item.Title!, item.Year, item.PosterUrl))
            .ToList();

        LogSearchRetrieved(movies.Count);
        return new SearchMoviesResult.Found(movies);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "STRM Manager movie search request skipped: no valid BaseUrl is configured")]
    private partial void LogSearchNoBaseUrl();

    [LoggerMessage(Level = LogLevel.Warning, Message = "STRM Manager movie search request timed out")]
    private partial void LogSearchTimedOut();

    [LoggerMessage(Level = LogLevel.Warning, Message = "STRM Manager movie search request failed to reach the server ({ExceptionType})")]
    private partial void LogSearchUnreachable(Exception exception, string exceptionType);

    [LoggerMessage(Level = LogLevel.Information, Message = "STRM Manager rejected the movie search query as invalid")]
    private partial void LogSearchInvalid();

    [LoggerMessage(Level = LogLevel.Warning, Message = "STRM Manager returned unexpected status {StatusCode} for movie search")]
    private partial void LogSearchUnexpectedStatus(int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "STRM Manager returned a malformed movie search body")]
    private partial void LogSearchMalformedBody(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "STRM Manager returned an unexpected movie search body shape")]
    private partial void LogSearchUnexpectedShape();

    [LoggerMessage(Level = LogLevel.Information, Message = "STRM Manager returned {MovieCount} movie search results")]
    private partial void LogSearchRetrieved(int movieCount);
}
