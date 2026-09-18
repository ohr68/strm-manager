using StrmManager.Common.Application.Messaging;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.Catalog.Application.Abstractions.Data;
using StrmManager.Modules.Catalog.Application.Metadata;
using StrmManager.Modules.Catalog.Domain.Movies;

namespace StrmManager.Modules.Catalog.Application.Movies.AddMovie;

/// <summary>
/// Creates a Movie from provider metadata. Unlike AddSeries there is no best-effort
/// fallback: without metadata there is nothing to create the movie from, so a provider
/// failure is returned to the caller. A movie without a reliable release date is
/// rejected rather than scheduled with an invented one (Movie.Schedule needs a real
/// release timestamp). A duplicate is a Conflict, not an idempotent success, so the
/// caller learns the movie already exists.
/// </summary>
internal sealed class AddMovieCommandHandler(
    IMovieRepository movieRepository,
    IMetadataProvider metadataProvider,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : ICommandHandler<AddMovieCommand, Guid>
{
    public async Task<Result<Guid>> Handle(AddMovieCommand command, CancellationToken cancellationToken)
    {
        // Cheap duplicate check first - avoids a provider call for a movie we already have.
        if (await movieRepository.GetByImdbIdAsync(command.ImdbId, cancellationToken) is not null)
        {
            return Result.Failure<Guid>(MovieErrors.AlreadyExists(command.ImdbId));
        }

        Result<MovieMetadata> metadataResult = await metadataProvider.GetMovieAsync(command.ImdbId, cancellationToken);

        if (metadataResult.IsFailure)
        {
            return Result.Failure<Guid>(metadataResult.Error);
        }

        MovieMetadata metadata = metadataResult.Value;

        // The provider's id is canonical; it can differ from what the caller asked for.
        string? canonicalImdbId = metadata.ExternalIds.ImdbId;

        if (string.IsNullOrWhiteSpace(canonicalImdbId))
        {
            return Result.Failure<Guid>(MetadataProviderErrors.InvalidResponse("The metadata provider", "missing canonical IMDb id"));
        }

        if (!string.Equals(canonicalImdbId, command.ImdbId, StringComparison.Ordinal) &&
            await movieRepository.GetByImdbIdAsync(canonicalImdbId, cancellationToken) is not null)
        {
            return Result.Failure<Guid>(MovieErrors.AlreadyExists(canonicalImdbId));
        }

        if (metadata.ReleaseAtUtc is not { } releaseAtUtc)
        {
            return Result.Failure<Guid>(MovieErrors.ReleaseDateUnavailable(canonicalImdbId));
        }

        DateTime utcNow = timeProvider.GetUtcNow().UtcDateTime;

        Movie movie = Movie.Schedule(
            metadata.ExternalIds,
            metadata.Title,
            metadata.Year,
            metadata.Runtime,
            releaseAtUtc,
            utcNow);

        movieRepository.Insert(movie);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return movie.Id;
    }
}
