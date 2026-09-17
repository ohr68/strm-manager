using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StrmManager.Modules.Catalog.Domain.StrmFiles;

namespace StrmManager.Modules.Catalog.Infrastructure.StrmFiles;

internal sealed class StrmFileConfiguration : IEntityTypeConfiguration<StrmFile>
{
    public void Configure(EntityTypeBuilder<StrmFile> builder)
    {
        builder.ToTable("strm_files");

        builder.HasKey(strmFile => strmFile.Id);

        builder.Property(strmFile => strmFile.Path).IsRequired().HasMaxLength(1024);

        builder.HasIndex(strmFile => strmFile.EpisodeId).IsUnique();
        builder.HasIndex(strmFile => strmFile.MovieId).IsUnique();
    }
}
