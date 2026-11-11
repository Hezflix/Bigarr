namespace Bigarr.Api.Features.Series.Persistence;

using Bigarr.Domain.Tv;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public sealed class EpisodeConfiguration : IEntityTypeConfiguration<Episode>
{
    public void Configure(EntityTypeBuilder<Episode> builder)
    {
        builder.ToTable("Episodes");

        builder.Property(e => e.Title).HasMaxLength(1000);
        builder.Property(e => e.ImageUrl).HasMaxLength(1024);
        builder.Ignore(e => e.IsSpecial);
        builder.Ignore(e => e.InOrder);

        // TVDB episode ids are global and stable: the identity across refreshes (and, later, across orders).
        builder.HasIndex(e => e.TvdbId).IsUnique().HasFilter("\"TvdbId\" IS NOT NULL");
        builder.HasIndex(e => e.TmdbId);

        // Not unique: a provider can briefly list two episodes under one number; a refresh must not fail on it.
        builder.HasIndex(e => new { e.SeriesId, e.SeasonNumber, e.EpisodeNumber });
        builder.HasIndex(e => new { e.SeriesId, e.AbsoluteEpisodeNumber });
        builder.HasIndex(e => new { e.SeriesId, e.AirDate });
        builder.HasIndex(e => new { e.SeriesId, e.SceneSeasonNumber, e.SceneEpisodeNumber });
        builder.HasIndex(e => new { e.SeriesId, e.OrderSeasonNumber, e.OrderEpisodeNumber });

        builder.HasOne(e => e.Season)
            .WithMany()
            .HasForeignKey(e => e.SeasonId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
