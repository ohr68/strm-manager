namespace StrmManager.Modules.Catalog.Application.Status;

/// <summary>
/// The git commit deployed, supplied explicitly at Docker build time (Dockerfile's
/// GIT_COMMIT build arg -> Build__Commit env var) from the local repository's HEAD -
/// never inferred from the server's filesystem, which may not even have a .git
/// directory. Null outside Docker (e.g. `dotnet run` in dev) - that is a normal,
/// expected value, not an error, so no validation is applied.
/// </summary>
public sealed class BuildInfoOptions
{
    public const string SectionName = "Build";

    public string? Commit { get; set; }
}
