namespace StrmManager.Modules.Catalog.Domain.SourceAttempts;

public enum SourceAttemptResult
{
    Approved = 0,
    Rejected = 1,
    Timeout = 2,
    ProbeError = 3,
    InvalidMedia = 4,
}
