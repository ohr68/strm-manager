namespace StrmManager.Modules.Catalog.Application.Series.GetSeasons;

public sealed record SeasonResponse(Guid Id, int Number, DateTime CreatedAtUtc, DateTime UpdatedAtUtc);
