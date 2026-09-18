using StrmManager.Common.Application.Messaging;

namespace StrmManager.Modules.Catalog.Application.Status.GetOperationalStatus;

public sealed record GetOperationalStatusQuery : IQuery<OperationalStatusResponse>;
