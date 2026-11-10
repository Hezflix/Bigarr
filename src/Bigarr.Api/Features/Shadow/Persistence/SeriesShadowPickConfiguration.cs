using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Indexers;
using Bigarr.Domain.Shadow;
using Bigarr.Domain.Tv;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bigarr.Api.Features.Shadow.Persistence;

public sealed class SeriesShadowPickConfiguration : IEntityTypeConfiguration<SeriesShadowPick>
{
    public void Configure(EntityTypeBuilder<SeriesShadowPick> builder)
    {
        builder.ToTable("SeriesShadowPicks");

        builder.Property(p => p.SeriesTitle).HasMaxLength(512);
        builder.Property(p => p.VersionName).HasMaxLength(64);
        builder.Property(p => p.Trigger).HasMaxLength(64);
        builder.Property(p => p.ReleaseTitle).HasMaxLength(1024);
        builder.Property(p => p.ReleaseGuid).HasMaxLength(1024);
        builder.Property(p => p.InfoHash).HasMaxLength(64);
        builder.Property(p => p.IndexerName).HasMaxLength(128);
        builder.Property(p => p.Quality).HasMaxLength(64);
        builder.Property(p => p.ReleaseGroup).HasMaxLength(255);
        builder.Property(p => p.Languages).HasJsonConversion();
        builder.Property(p => p.CustomFormats).HasJsonConversion();
        builder.Property(p => p.SonarrView).HasJsonConversion();

        // One row per version and release: a release picked again only moves LastPickedAt.
        builder.HasIndex(p => new { p.VersionId, p.ReleaseGuid }).IsUnique();
        builder.HasIndex(p => new { p.TvdbId, p.VersionName });
        builder.HasIndex(p => p.PickedAt);

        // A deleted series or version keeps its picks: the snapshots still pair them with Sonarr's grabs.
        builder.HasOne(p => p.Version).WithMany().HasForeignKey(p => p.VersionId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Bigarr.Domain.Tv.Series>().WithMany().HasForeignKey(p => p.SeriesId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Indexer>().WithMany().HasForeignKey(p => p.IndexerId).OnDelete(DeleteBehavior.SetNull);
        builder.HasMany(p => p.Episodes).WithOne(e => e.Pick).HasForeignKey(e => e.PickId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class SeriesShadowPickEpisodeConfiguration : IEntityTypeConfiguration<SeriesShadowPickEpisode>
{
    public void Configure(EntityTypeBuilder<SeriesShadowPickEpisode> builder)
    {
        builder.ToTable("SeriesShadowPickEpisodes");

        builder.HasIndex(e => e.TargetId);
        builder.HasOne<AcquisitionTarget>().WithMany().HasForeignKey(e => e.TargetId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Episode>().WithMany().HasForeignKey(e => e.EpisodeId).OnDelete(DeleteBehavior.SetNull);
    }
}
