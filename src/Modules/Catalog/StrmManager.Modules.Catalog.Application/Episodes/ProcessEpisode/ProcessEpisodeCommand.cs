using StrmManager.Common.Application.Messaging;

namespace StrmManager.Modules.Catalog.Application.Episodes.ProcessEpisode;

public sealed record ProcessEpisodeCommand(Guid EpisodeId) : ICommand<ProcessEpisodeResult>;
