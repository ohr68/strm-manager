using StrmManager.Common.Application.Messaging;

namespace StrmManager.Modules.Catalog.Application.Series.AddSeries;

public sealed record AddSeriesCommand(
    string ImdbId,
    string? TmdbId,
    string? TvdbId,
    string Title,
    string? OriginalTitle,
    int Year) : ICommand<Guid>;
