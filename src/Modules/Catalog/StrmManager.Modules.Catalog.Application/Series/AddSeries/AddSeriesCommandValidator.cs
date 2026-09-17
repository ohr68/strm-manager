using FluentValidation;

namespace StrmManager.Modules.Catalog.Application.Series.AddSeries;

internal sealed class AddSeriesCommandValidator : AbstractValidator<AddSeriesCommand>
{
    public AddSeriesCommandValidator()
    {
        RuleFor(c => c.ImdbId).NotEmpty();
        RuleFor(c => c.Title).NotEmpty();
        RuleFor(c => c.Year).InclusiveBetween(1870, 2200);
    }
}
