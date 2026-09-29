using FluentValidation;

namespace StrmManager.Modules.Catalog.Application.Series.AddSeries;

internal sealed class AddSeriesCommandValidator : AbstractValidator<AddSeriesCommand>
{
    public AddSeriesCommandValidator()
    {
        RuleFor(c => c.ImdbId).NotEmpty();
    }
}
