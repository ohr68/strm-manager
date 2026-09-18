namespace StrmManager.Modules.Catalog.Application.Abstractions.Data;

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Same as SaveChangesAsync, but translates a concurrency conflict (another caller
    /// already committed changes to a row this call is trying to update, detected via an
    /// optimistic-concurrency token - see EpisodeConfiguration/ADR-013) into a false
    /// return instead of throwing, so Application code can handle "someone else already
    /// claimed this" as a normal outcome without depending on an EF Core exception type.
    /// </summary>
    Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken = default);
}
