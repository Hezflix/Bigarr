using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Shadow;
using Bigarr.Domain.Tv;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bigarr.Api.Features.Shadow.Persistence;

public sealed class FollowedSonarrConfiguration : IEntityTypeConfiguration<FollowedSonarr>
{
    public void Configure(EntityTypeBuilder<FollowedSonarr> builder)
    {
        builder.ToTable("FollowedSonarrs");

        builder.Property(r => r.Name).HasMaxLength(64);
        builder.Property(r => r.Url).HasMaxLength(512);
        builder.Property(r => r.ApiKey).HasMaxLength(128);
        builder.Property(r => r.VersionName).HasMaxLength(64);
        builder.Property(r => r.ProfileMap).HasJsonConversion();
        builder.Property(r => r.RootFolderMap).HasJsonConversion();
        builder.Property(r => r.LastSummary).HasMaxLength(1024);
        builder.Property(r => r.LastError).HasMaxLength(2048);
        builder.Property(r => r.LastWebhookEvent).HasMaxLength(64);

        builder.HasIndex(r => r.Name).IsUnique();
    }
}

public sealed class SonarrGrabConfiguration : IEntityTypeConfiguration<SonarrGrab>
{
    public void Configure(EntityTypeBuilder<SonarrGrab> builder)
    {
        builder.ToTable("SonarrGrabs");

        builder.Property(g => g.SeriesTitle).HasMaxLength(512);
        builder.Property(g => g.VersionName).HasMaxLength(64);
        builder.Property(g => g.ReleaseTitle).HasMaxLength(1024);
        builder.Property(g => g.ReleaseGuid).HasMaxLength(1024);
        builder.Property(g => g.InfoHash).HasMaxLength(64);
        builder.Property(g => g.DownloadId).HasMaxLength(128);
        builder.Property(g => g.IndexerName).HasMaxLength(128);
        builder.Property(g => g.ReleaseSource).HasMaxLength(32);
        builder.Property(g => g.ReleaseType).HasMaxLength(32);
        builder.Property(g => g.DownloadClient).HasMaxLength(128);
        builder.Property(g => g.Quality).HasMaxLength(64);
        builder.Property(g => g.ReleaseGroup).HasMaxLength(255);
        builder.Property(g => g.Languages).HasJsonConversion();
        builder.Property(g => g.CustomFormats).HasJsonConversion();
        builder.Property(g => g.BigarrVerdict).HasJsonConversion();

        // Reading the same history lines again (the next pass starts a little before the checkpoint) adds nothing.
        builder.HasIndex(g => new { g.FollowedSonarrId, g.SonarrHistoryId }).IsUnique();
        builder.HasIndex(g => new { g.TvdbId, g.VersionName });
        builder.HasIndex(g => g.GrabbedAt);
        builder.HasIndex(g => g.DownloadId);

        // Forgetting a Sonarr forgets its grabs; a deleted series or version keeps them for the comparison.
        builder.HasOne(g => g.FollowedSonarr).WithMany().HasForeignKey(g => g.FollowedSonarrId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Bigarr.Domain.Tv.Series>().WithMany().HasForeignKey(g => g.SeriesId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<SeriesVersion>().WithMany().HasForeignKey(g => g.VersionId).OnDelete(DeleteBehavior.SetNull);
        builder.HasMany(g => g.Episodes).WithOne(e => e.Grab).HasForeignKey(e => e.GrabId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class SonarrGrabEpisodeConfiguration : IEntityTypeConfiguration<SonarrGrabEpisode>
{
    public void Configure(EntityTypeBuilder<SonarrGrabEpisode> builder)
    {
        builder.ToTable("SonarrGrabEpisodes");

        builder.HasIndex(e => e.EpisodeId);
        builder.HasOne<Episode>().WithMany().HasForeignKey(e => e.EpisodeId).OnDelete(DeleteBehavior.SetNull);
    }
}
