using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Playback;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bigarr.Api.Features.Playback.Persistence;

public sealed class MediaServerUserConfiguration : IEntityTypeConfiguration<MediaServerUser>
{
    public void Configure(EntityTypeBuilder<MediaServerUser> builder)
    {
        builder.ToTable("MediaServerUsers");

        builder.Property(u => u.ExternalId).HasMaxLength(64);
        builder.Property(u => u.Name).HasMaxLength(256);
        builder.Property(u => u.DisplayName).HasMaxLength(256);
        builder.Property(u => u.ThumbUrl).HasMaxLength(1024);
        builder.Ignore(u => u.ShownName);

        builder.HasOne(u => u.MediaServer).WithMany().HasForeignKey(u => u.MediaServerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(u => new { u.MediaServerId, u.ExternalId }).IsUnique();
    }
}

public sealed class MediaServerItemConfiguration : IEntityTypeConfiguration<MediaServerItem>
{
    public void Configure(EntityTypeBuilder<MediaServerItem> builder)
    {
        builder.ToTable("MediaServerItems");

        builder.Property(i => i.RatingKey).HasMaxLength(64);
        builder.Property(i => i.Guid).HasMaxLength(256);
        builder.Property(i => i.ImdbId).HasMaxLength(16);
        builder.Property(i => i.Title).HasMaxLength(512);
        builder.Property(i => i.OriginalTitle).HasMaxLength(512);
        builder.Property(i => i.LibrarySectionId).HasMaxLength(64);
        builder.Property(i => i.LibrarySectionTitle).HasMaxLength(256);
        builder.Property(i => i.ShowRatingKey).HasMaxLength(64);
        builder.Property(i => i.ShowGuid).HasMaxLength(256);
        builder.Property(i => i.ShowTitle).HasMaxLength(512);
        builder.Property(i => i.ShowImdbId).HasMaxLength(16);
        builder.Property(i => i.SeasonRatingKey).HasMaxLength(64);

        builder.HasOne(i => i.MediaServer).WithMany().HasForeignKey(i => i.MediaServerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(i => i.Movie).WithMany().HasForeignKey(i => i.MovieId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(i => i.LinkedVersion).WithMany().HasForeignKey(i => i.LinkedVersionId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(i => new { i.MediaServerId, i.RatingKey }).IsUnique();
        builder.HasIndex(i => i.TmdbId);
        builder.HasIndex(i => i.ShowTvdbId);
    }
}

public sealed class PlayConfiguration : IEntityTypeConfiguration<Play>
{
    public void Configure(EntityTypeBuilder<Play> builder)
    {
        builder.ToTable("Plays");

        builder.Property(p => p.VersionName).HasMaxLength(64);

        builder.HasOne(p => p.User).WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(p => p.Item).WithMany().HasForeignKey(p => p.ItemId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(p => p.Movie).WithMany().HasForeignKey(p => p.MovieId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(p => p.Version).WithMany().HasForeignKey(p => p.VersionId).OnDelete(DeleteBehavior.SetNull);
        builder.HasMany(p => p.Streams).WithOne(s => s.Play).HasForeignKey(s => s.PlayId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(p => p.StartedAt);
        builder.HasIndex(p => new { p.UserId, p.StartedAt });
        builder.HasIndex(p => new { p.ItemId, p.UserId });
        builder.HasIndex(p => new { p.MovieId, p.StartedAt });
        builder.HasIndex(p => new { p.VersionId, p.StartedAt });

        // The library list counts the plays of every film and version: read off this index alone, never the rows.
        builder.HasIndex(p => new { p.MovieId, p.VersionId, p.StoppedAt });
    }
}

public sealed class PlayStreamConfiguration : IEntityTypeConfiguration<PlayStream>
{
    public void Configure(EntityTypeBuilder<PlayStream> builder)
    {
        builder.ToTable("PlayStreams");

        builder.Property(s => s.FilePath).HasMaxLength(1024);
        builder.Property(s => s.ExternalKey).HasMaxLength(64);
        builder.ComplexProperty(s => s.Details, StreamDetailsConfiguration.Configure);

        builder.HasOne(s => s.Version).WithMany().HasForeignKey(s => s.VersionId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(s => s.ExternalKey).IsUnique().HasFilter("\"ExternalKey\" IS NOT NULL");
        builder.HasIndex(s => s.VersionId);

        // IX_PlayStreams_Decision_PlayId, for the Plays page's decision filter, is created by the PlaybackStats
        // migration: EF cannot index a complex property's column.
    }
}

public sealed class PlaybackSessionConfiguration : IEntityTypeConfiguration<PlaybackSession>
{
    public void Configure(EntityTypeBuilder<PlaybackSession> builder)
    {
        builder.ToTable("PlaybackSessions");

        builder.Property(s => s.SessionKey).HasMaxLength(64);
        builder.Property(s => s.ServerSessionId).HasMaxLength(128);
        builder.Property(s => s.RatingKey).HasMaxLength(64);
        builder.Property(s => s.FilePath).HasMaxLength(1024);
        builder.Property(s => s.VersionName).HasMaxLength(64);
        builder.Property(s => s.Details).HasJsonConversion();

        builder.HasOne(s => s.MediaServer).WithMany().HasForeignKey(s => s.MediaServerId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(s => s.User).WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(s => s.Item).WithMany().HasForeignKey(s => s.ItemId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(s => new { s.MediaServerId, s.SessionKey });
    }
}

public sealed class PlaybackSettingsConfiguration : IEntityTypeConfiguration<PlaybackSettings>
{
    public void Configure(EntityTypeBuilder<PlaybackSettings> builder)
    {
        builder.ToTable("PlaybackSettings");

        builder.Property(c => c.Id).ValueGeneratedNever();
    }
}

internal static class StreamDetailsConfiguration
{
    public static void Configure(ComplexPropertyBuilder<StreamDetails> details)
    {
        details.Property(d => d.PlayerTitle).HasColumnName("PlayerTitle").HasMaxLength(256);
        details.Property(d => d.PlayerProduct).HasColumnName("PlayerProduct").HasMaxLength(128);
        details.Property(d => d.PlayerPlatform).HasColumnName("PlayerPlatform").HasMaxLength(128);
        details.Property(d => d.PlayerPlatformVersion).HasColumnName("PlayerPlatformVersion").HasMaxLength(64);
        details.Property(d => d.PlayerDevice).HasColumnName("PlayerDevice").HasMaxLength(128);
        details.Property(d => d.PlayerId).HasColumnName("PlayerId").HasMaxLength(128);
        details.Property(d => d.IpAddress).HasColumnName("IpAddress").HasMaxLength(64);
        details.Property(d => d.Local).HasColumnName("Local");
        details.Property(d => d.Secure).HasColumnName("Secure");
        details.Property(d => d.Relayed).HasColumnName("Relayed");
        details.Property(d => d.Bandwidth).HasColumnName("Bandwidth");
        details.Property(d => d.Decision).HasColumnName("Decision");
        details.Property(d => d.VideoDecision).HasColumnName("VideoDecision").HasMaxLength(32);
        details.Property(d => d.AudioDecision).HasColumnName("AudioDecision").HasMaxLength(32);
        details.Property(d => d.SubtitleDecision).HasColumnName("SubtitleDecision").HasMaxLength(32);
        details.Property(d => d.HardwareTranscode).HasColumnName("HardwareTranscode");
        details.Property(d => d.SourceContainer).HasColumnName("SourceContainer").HasMaxLength(32);
        details.Property(d => d.SourceVideoResolution).HasColumnName("SourceVideoResolution").HasMaxLength(16);
        details.Property(d => d.SourceVideoCodec).HasColumnName("SourceVideoCodec").HasMaxLength(32);
        details.Property(d => d.SourceDynamicRange).HasColumnName("SourceDynamicRange").HasMaxLength(16);
        details.Property(d => d.SourceAudioCodec).HasColumnName("SourceAudioCodec").HasMaxLength(32);
        details.Property(d => d.SourceAudioChannels).HasColumnName("SourceAudioChannels");
        details.Property(d => d.SourceBitrate).HasColumnName("SourceBitrate");
        details.Property(d => d.StreamContainer).HasColumnName("StreamContainer").HasMaxLength(32);
        details.Property(d => d.StreamVideoResolution).HasColumnName("StreamVideoResolution").HasMaxLength(16);
        details.Property(d => d.StreamVideoCodec).HasColumnName("StreamVideoCodec").HasMaxLength(32);
        details.Property(d => d.StreamAudioCodec).HasColumnName("StreamAudioCodec").HasMaxLength(32);
        details.Property(d => d.StreamAudioChannels).HasColumnName("StreamAudioChannels");
        details.Property(d => d.StreamBitrate).HasColumnName("StreamBitrate");
        details.Property(d => d.ServerFilePath).HasColumnName("ServerFilePath").HasMaxLength(1024);
    }
}
