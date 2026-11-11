namespace Bigarr.Api.Features.Series.Persistence;

// Inside the namespace: "Series" would otherwise resolve to this slice's namespace, not the entity.
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Tv;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public sealed class SeriesConfiguration : IEntityTypeConfiguration<Series>
{
    public void Configure(EntityTypeBuilder<Series> builder)
    {
        builder.ToTable("Series");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Title).HasMaxLength(500);
        builder.Property(s => s.OriginalTitle).HasMaxLength(500);
        builder.Property(s => s.SortTitle).HasMaxLength(500);
        builder.Property(s => s.ImdbId).HasMaxLength(16);
        builder.Property(s => s.Network).HasMaxLength(255);
        builder.Property(s => s.AirTime).HasMaxLength(16);
        builder.Property(s => s.OriginalCountry).HasMaxLength(8);
        builder.Property(s => s.NetworkCountry).HasMaxLength(8);
        builder.Property(s => s.OriginalLanguage).HasMaxLength(8);
        builder.Property(s => s.AlternativeTitles).HasJsonConversion();
        builder.Property(s => s.Images).HasJsonConversion();
        builder.Property(s => s.OrderOnlyEpisodes).HasJsonConversion();
        builder.Property(s => s.EpisodeGroupId).HasMaxLength(64);
        builder.Property(s => s.EpisodeGroupName).HasMaxLength(255);
        builder.Ignore(s => s.Monitored);

        // One row per show: the TVDB id is the key of the ecosystem, the TMDB id the fallback for shows TVDB lacks.
        builder.HasIndex(s => s.TvdbId).IsUnique().HasFilter("\"TvdbId\" IS NOT NULL");
        builder.HasIndex(s => s.TmdbId).IsUnique().HasFilter("\"TmdbId\" IS NOT NULL");
        builder.HasIndex(s => s.SortTitle);

        builder.HasOne(s => s.DefaultRootFolder)
            .WithMany()
            .HasForeignKey(s => s.DefaultRootFolderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(s => s.Versions)
            .WithOne(v => v.Series)
            .HasForeignKey(v => v.SeriesId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(s => s.Seasons)
            .WithOne(s => s.Series)
            .HasForeignKey(s => s.SeriesId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(s => s.Episodes)
            .WithOne(e => e.Series)
            .HasForeignKey(e => e.SeriesId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(s => s.Tags)
            .WithMany()
            .UsingEntity<SeriesTag>(
                right => right.HasOne(t => t.Tag).WithMany().HasForeignKey(t => t.TagId).OnDelete(DeleteBehavior.Cascade),
                left => left.HasOne(t => t.Series).WithMany().HasForeignKey(t => t.SeriesId).OnDelete(DeleteBehavior.Cascade),
                join =>
                {
                    join.ToTable("SeriesTags");
                    join.HasKey(t => new { t.SeriesId, t.TagId });
                });
    }
}
