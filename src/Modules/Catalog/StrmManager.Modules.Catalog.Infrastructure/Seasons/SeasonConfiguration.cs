using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StrmManager.Modules.Catalog.Domain.Seasons;
using SeriesEntity = StrmManager.Modules.Catalog.Domain.Series.Series;

namespace StrmManager.Modules.Catalog.Infrastructure.Seasons;

internal sealed class SeasonConfiguration : IEntityTypeConfiguration<Season>
{
    public void Configure(EntityTypeBuilder<Season> builder)
    {
        builder.ToTable("seasons");

        builder.HasKey(season => season.Id);

        builder.HasIndex(season => new { season.SeriesId, season.Number }).IsUnique();

        // No navigation property on either side by design (see docs/architecture.md) -
        // the FK-only relationship still gives us referential integrity at the database
        // level. Restrict: catalog deletion semantics haven't been designed yet, so a
        // Series with existing Seasons must not be deletable until that's decided
        // deliberately (see ADR-005).
        builder.HasOne<SeriesEntity>()
            .WithMany()
            .HasForeignKey(season => season.SeriesId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();
    }
}
