using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StrmManager.Modules.Catalog.Domain.SourceAttempts;

namespace StrmManager.Modules.Catalog.Infrastructure.SourceAttempts;

internal sealed class SourceAttemptConfiguration : IEntityTypeConfiguration<SourceAttempt>
{
    public void Configure(EntityTypeBuilder<SourceAttempt> builder)
    {
        builder.ToTable("source_attempts");

        builder.HasKey(attempt => attempt.Id);

        builder.Property(attempt => attempt.Provider).IsRequired().HasMaxLength(128);
        builder.Property(attempt => attempt.SourceName).IsRequired().HasMaxLength(256);
        builder.Property(attempt => attempt.Result).HasConversion<string>().HasMaxLength(32);
        builder.Property(attempt => attempt.VideoCodec).HasMaxLength(64);
        builder.Property(attempt => attempt.AudioCodec).HasMaxLength(64);
        builder.Property(attempt => attempt.FailureReason).HasMaxLength(2048);

        builder.HasIndex(attempt => attempt.EpisodeId);
        builder.HasIndex(attempt => attempt.MovieId);
    }
}
