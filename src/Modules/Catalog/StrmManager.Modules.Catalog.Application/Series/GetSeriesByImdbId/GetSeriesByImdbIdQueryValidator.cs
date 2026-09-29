using FluentValidation;

namespace StrmManager.Modules.Catalog.Application.Series.GetSeriesByImdbId;

internal sealed class GetSeriesByImdbIdQueryValidator
    : AbstractValidator<GetSeriesByImdbIdQuery>
{
    public GetSeriesByImdbIdQueryValidator()
    {
        RuleFor(query => query.ImdbId).NotEmpty();
    }
}
