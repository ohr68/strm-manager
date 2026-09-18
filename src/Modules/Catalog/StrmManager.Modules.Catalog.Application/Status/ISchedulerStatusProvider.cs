namespace StrmManager.Modules.Catalog.Application.Status;

/// <summary>
/// Lets GetOperationalStatusQueryHandler report whether the autonomous scheduler is
/// enabled without Catalog.Application depending on the Scheduling module - the
/// abstraction lives here (the consumer), the implementation lives in
/// Scheduling.Infrastructure (the producer) and is registered as this interface, the
/// same Dependency Inversion shape already used for IMetadataProvider/IStreamProvider.
/// See ADR-012.
/// </summary>
public interface ISchedulerStatusProvider
{
    bool Enabled { get; }
}
