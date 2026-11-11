namespace Bigarr.Api.Features.Series.Persistence;

using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Tv;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public sealed class HeldReleaseConfiguration : IEntityTypeConfiguration<HeldRelease>
{
    public void Configure(EntityTypeBuilder<HeldRelease> builder)
    {
        builder.ToTable("HeldReleases");
        builder.Property(h => h.EpisodeIds).HasJsonConversion();
        builder.Property(h => h.Episodes).HasMaxLength(255);
        builder.Property(h => h.Title).HasMaxLength(1000);
        builder.Property(h => h.Guid).HasMaxLength(2000);
        builder.Property(h => h.IndexerName).HasMaxLength(255);
        builder.Property(h => h.DownloadUrl).HasMaxLength(4000);
        builder.Property(h => h.MagnetUrl).HasMaxLength(4000);
        builder.Property(h => h.InfoHash).HasMaxLength(64);
        builder.Property(h => h.Quality).HasMaxLength(64);
        builder.Property(h => h.Reason).HasMaxLength(1000);
        builder.Property(h => h.Source).HasMaxLength(64);
        builder.HasIndex(h => new { h.VersionId, h.Guid }).IsUnique();
        builder.HasIndex(h => h.SeriesId);
        builder.HasOne(h => h.Version).WithMany().HasForeignKey(h => h.VersionId).OnDelete(DeleteBehavior.Cascade);
    }
}
