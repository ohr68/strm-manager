using StrmManager.Common.Application.Messaging;

namespace StrmManager.Modules.Catalog.Application.Episodes.RetryEpisode;

public sealed record RetryEpisodeCommand(Guid EpisodeId) : ICommand<RetryEpisodeResult>;
