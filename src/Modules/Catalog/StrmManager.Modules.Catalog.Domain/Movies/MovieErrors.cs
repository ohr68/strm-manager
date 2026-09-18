using StrmManager.Common.Domain.Abstractions;

namespace StrmManager.Modules.Catalog.Domain.Movies;

public static class MovieErrors
{
    public static Error NotFound(Guid movieId) =>
        Error.NotFound("Movies.NotFound", $"The movie with the identifier '{movieId}' was not found.");

    /// <summary>
    /// Another request already holds the movie's processing claim - either it is
    /// Searching/Validating right now, or this request lost the optimistic-concurrency
    /// race for the Pending -> Searching claim. Same Conflict (409) as
    /// EpisodeErrors.AlreadyBeingProcessed.
    /// </summary>
    public static Error AlreadyBeingProcessed(Guid movieId) =>
        Error.Conflict("Movies.AlreadyBeingProcessed", $"The movie with the identifier '{movieId}' is already being processed by another request.");

    public static Error AlreadyExists(string imdbId) =>
        Error.Conflict("Movies.AlreadyExists", $"A movie with IMDb id '{imdbId}' already exists.");

    /// <summary>
    /// A Movie is scheduled by its release timestamp (Movie.Schedule), and no caller may
    /// invent one - so metadata without a reliable release date cannot be added yet.
    /// </summary>
    public static Error ReleaseDateUnavailable(string imdbId) =>
        Error.Validation(
            "Movies.ReleaseDateUnavailable",
            $"The movie '{imdbId}' cannot be added: the metadata provider has no reliable release date for it, and one is required to schedule it.");
}
