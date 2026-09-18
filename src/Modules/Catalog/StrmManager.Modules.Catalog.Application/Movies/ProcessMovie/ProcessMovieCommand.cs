using StrmManager.Common.Application.Messaging;

namespace StrmManager.Modules.Catalog.Application.Movies.ProcessMovie;

public sealed record ProcessMovieCommand(Guid MovieId) : ICommand<ProcessMovieResult>;
