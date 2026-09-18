using StrmManager.Common.Application.Messaging;

namespace StrmManager.Modules.Catalog.Application.Maintenance;

public sealed record RunCatalogMaintenanceCommand : ICommand<CatalogMaintenanceResult>;
