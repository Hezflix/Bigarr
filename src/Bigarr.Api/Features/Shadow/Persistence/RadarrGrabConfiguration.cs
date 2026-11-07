using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Shadow;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bigarr.Api.Features.Shadow.Persistence;

public sealed class RadarrGrabConfiguration : IEntityTypeConfiguration<RadarrGrab>
{
    public void Configure(EntityTypeBuilder<RadarrGrab> builder)
    {
        builder.ToTable("RadarrGrabs");

        builder.Property(g => g.MovieTitle).HasMaxLength(512);
        builder.Property(g => g.VersionName).HasMaxLength(64);
        builder.Property(g => g.ReleaseTitle).HasMaxLength(1024);
        builder.Property(g => g.ReleaseGuid).HasMaxLength(1024);
        builder.Property(g => g.InfoHash).HasMaxLength(64);
        builder.Property(g => g.DownloadId).HasMaxLength(128);
        builder.Property(g => g.IndexerName).HasMaxLength(128);
        builder.Property(g => g.ReleaseSource).HasMaxLength(32);
        builder.Property(g => g.DownloadClient).HasMaxLength(128);
        builder.Property(g => g.Quality).HasMaxLength(64);
        builder.Property(g => g.ReleaseGroup).HasMaxLength(255);
        builder.Property(g => g.Languages).HasJsonConversion();
        builder.Property(g => g.CustomFormats).HasJsonConversion();
        builder.Property(g => g.BigarrVerdict).HasJsonConversion();

        // Reading the same history lines again (the next pass starts a little before the checkpoint) adds nothing.
        builder.HasIndex(g => new { g.FollowedRadarrId, g.RadarrHistoryId }).IsUnique();
        builder.HasIndex(g => new { g.TmdbId, g.VersionName });
        builder.HasIndex(g => g.GrabbedAt);
        builder.HasIndex(g => g.DownloadId);

        // Forgetting a Radarr forgets its grabs; a deleted film or version keeps them for the comparison.
        builder.HasOne(g => g.FollowedRadarr).WithMany().HasForeignKey(g => g.FollowedRadarrId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Movie>().WithMany().HasForeignKey(g => g.MovieId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<MovieVersion>().WithMany().HasForeignKey(g => g.VersionId).OnDelete(DeleteBehavior.SetNull);
    }
}
