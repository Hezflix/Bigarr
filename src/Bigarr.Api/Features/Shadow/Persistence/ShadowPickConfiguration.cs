using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Indexers;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Shadow;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bigarr.Api.Features.Shadow.Persistence;

public sealed class ShadowPickConfiguration : IEntityTypeConfiguration<ShadowPick>
{
    public void Configure(EntityTypeBuilder<ShadowPick> builder)
    {
        builder.ToTable("ShadowPicks");

        builder.Property(p => p.MovieTitle).HasMaxLength(512);
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
        builder.Property(p => p.RadarrView).HasJsonConversion();

        // One row per version and release: a release picked again by a later pass only moves LastPickedAt.
        builder.HasIndex(p => new { p.VersionId, p.ReleaseGuid }).IsUnique();
        builder.HasIndex(p => new { p.TmdbId, p.VersionName });
        builder.HasIndex(p => p.PickedAt);

        // A deleted film or version keeps its picks: the snapshots still pair them with Radarr's grabs.
        builder.HasOne(p => p.Version).WithMany().HasForeignKey(p => p.VersionId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Movie>().WithMany().HasForeignKey(p => p.MovieId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Indexer>().WithMany().HasForeignKey(p => p.IndexerId).OnDelete(DeleteBehavior.SetNull);
    }
}
