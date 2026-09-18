using StrmManager.Common.Application.Messaging;

namespace StrmManager.Modules.Catalog.Application.Movies.AddMovie;

/// <summary>
/// Adds a movie by a known IMDb id. Deliberately carries no title/year - unlike a
/// series, a movie can only be created from provider metadata, so caller-supplied
/// values would either be ignored or override the canonical ones.
/// </summary>
public sealed record AddMovieCommand(string ImdbId) : ICommand<Guid>;
