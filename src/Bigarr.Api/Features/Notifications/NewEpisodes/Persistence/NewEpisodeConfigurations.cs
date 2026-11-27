using Bigarr.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bigarr.Api.Features.Notifications.NewEpisodes.Persistence;

public sealed class NewEpisodeAnnouncementConfiguration : IEntityTypeConfiguration<NewEpisodeAnnouncement>
{
    public void Configure(EntityTypeBuilder<NewEpisodeAnnouncement> builder)
    {
        builder.ToTable("NewEpisodeAnnouncements");
        builder.Property(a => a.Body).HasMaxLength(16 * 1024);

        builder.HasMany(a => a.Episodes).WithOne(e => e.Announcement).HasForeignKey(e => e.AnnouncementId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(a => a.Messages).WithOne(m => m.Announcement).HasForeignKey(m => m.AnnouncementId).OnDelete(DeleteBehavior.Cascade);

        // What the announcer polls: the announcements still collecting or waiting for Plex, by show.
        builder.HasIndex(a => new { a.ReadyAt, a.SeriesId });
    }
}

public sealed class NewEpisodeAnnouncementEpisodeConfiguration : IEntityTypeConfiguration<NewEpisodeAnnouncementEpisode>
{
    public void Configure(EntityTypeBuilder<NewEpisodeAnnouncementEpisode> builder)
    {
        builder.ToTable("NewEpisodeAnnouncementEpisodes");
        builder.Property(e => e.Title).HasMaxLength(512);
        builder.Property(e => e.Overview).HasMaxLength(4096);
        builder.Property(e => e.FilePath).HasMaxLength(4096);

        // An episode is announced once, ever.
        builder.HasIndex(e => e.EpisodeId).IsUnique();
    }
}

public sealed class NewEpisodeMessageConfiguration : IEntityTypeConfiguration<NewEpisodeMessage>
{
    public void Configure(EntityTypeBuilder<NewEpisodeMessage> builder)
    {
        builder.ToTable("NewEpisodeMessages");
        builder.Property(m => m.DiscordUserId).HasMaxLength(32);
        builder.Property(m => m.LastError).HasMaxLength(2048);
        builder.Ignore(m => m.State);

        builder.HasIndex(m => new { m.DeliveredAt, m.NextAttemptAt });
        builder.HasIndex(m => new { m.AnnouncementId, m.UserId }).IsUnique();
    }
}

public sealed class SeriesNotificationOptOutConfiguration : IEntityTypeConfiguration<SeriesNotificationOptOut>
{
    public void Configure(EntityTypeBuilder<SeriesNotificationOptOut> builder)
    {
        builder.ToTable("SeriesNotificationOptOuts");
        builder.HasKey(o => new { o.UserId, o.SeriesId });
        builder.HasIndex(o => o.SeriesId);
        builder.HasOne<Bigarr.Domain.Auth.User>().WithMany().HasForeignKey(o => o.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Bigarr.Domain.Tv.Series>().WithMany().HasForeignKey(o => o.SeriesId).OnDelete(DeleteBehavior.Cascade);
    }
}
