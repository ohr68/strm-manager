using StrmManager.Common.Application.Messaging;

namespace StrmManager.Modules.Catalog.Application.Series.AddSeries;

public sealed record AddSeriesCommand(
    string ImdbId) : ICommand<Guid>;
