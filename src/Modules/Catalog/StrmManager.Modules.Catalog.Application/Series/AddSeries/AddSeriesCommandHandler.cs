using StrmManager.Common.Application.Messaging;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.Catalog.Application.Abstractions.Data;
using StrmManager.Modules.Catalog.Domain.Shared;
using SeriesEntity = StrmManager.Modules.Catalog.Domain.Series.Series;
using ISeriesRepository = StrmManager.Modules.Catalog.Domain.Series.ISeriesRepository;

namespace StrmManager.Modules.Catalog.Application.Series.AddSeries;

internal sealed class AddSeriesCommandHandler(
    ISeriesRepository seriesRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : ICommandHandler<AddSeriesCommand, Guid>
{
    public async Task<Result<Guid>> Handle(AddSeriesCommand command, CancellationToken cancellationToken)
    {
        SeriesEntity? existing = await seriesRepository.GetByImdbIdAsync(command.ImdbId, cancellationToken);

        if (existing is not null)
        {
            return existing.Id;
        }

        DateTime utcNow = timeProvider.GetUtcNow().UtcDateTime;

        var externalIds = new ExternalIds(command.ImdbId, command.TmdbId, command.TvdbId);

        SeriesEntity series = SeriesEntity.Create(externalIds, command.Title, command.OriginalTitle, command.Year, utcNow);

        seriesRepository.Insert(series);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return series.Id;
    }
}
