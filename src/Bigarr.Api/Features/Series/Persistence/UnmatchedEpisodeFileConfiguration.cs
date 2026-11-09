using Bigarr.Domain.Tv;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bigarr.Api.Features.Series.Persistence;

public sealed class UnmatchedEpisodeFileConfiguration : IEntityTypeConfiguration<UnmatchedEpisodeFile>
{
    public void Configure(EntityTypeBuilder<UnmatchedEpisodeFile> builder)
    {
        builder.ToTable("UnmatchedEpisodeFiles");
        builder.Property(f => f.RelativePath).HasMaxLength(1024);
        builder.Property(f => f.Detail).HasMaxLength(2000);
        builder.HasIndex(f => new { f.VersionId, f.RelativePath }).IsUnique();
        builder.HasIndex(f => f.SeriesId);

        builder.HasOne(f => f.Series)
            .WithMany()
            .HasForeignKey(f => f.SeriesId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(f => f.Version)
            .WithMany()
            .HasForeignKey(f => f.VersionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
