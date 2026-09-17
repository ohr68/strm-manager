namespace StrmManager.Modules.Catalog.Domain.Shared;

public enum MediaStatus
{
    Scheduled = 0,
    Pending = 1,
    Searching = 2,
    Validating = 3,
    Completed = 4,
    Unavailable = 5,
    Error = 6,
}
