using StrmManager.Common.Application.Messaging;
using StrmManager.Modules.Catalog.Application.Metadata;

namespace StrmManager.Modules.Catalog.Application.Series.RefreshSeriesMetadata;

public sealed record RefreshSeriesMetadataCommand(Guid SeriesId) : ICommand<CatalogSynchronizationResult>;
