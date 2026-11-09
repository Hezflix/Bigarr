namespace Bigarr.Api.Features.Series.Persistence;

using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Tv;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public sealed class SeasonConfiguration : IEntityTypeConfiguration<Season>
{
    public void Configure(EntityTypeBuilder<Season> builder)
    {
        builder.ToTable("Seasons");

        builder.Property(s => s.Title).HasMaxLength(500);
        builder.Property(s => s.Images).HasJsonConversion();
        builder.Ignore(s => s.IsSpecials);

        builder.HasIndex(s => new { s.SeriesId, s.SeasonNumber }).IsUnique();
    }
}

public sealed class SeriesVersionSeasonConfiguration : IEntityTypeConfiguration<SeriesVersionSeason>
{
    public void Configure(EntityTypeBuilder<SeriesVersionSeason> builder)
    {
        builder.ToTable("SeriesVersionSeasons");
        builder.HasKey(s => new { s.VersionId, s.SeasonId });

        builder.HasOne(s => s.Version)
            .WithMany(v => v.Seasons)
            .HasForeignKey(s => s.VersionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(s => s.Season)
            .WithMany()
            .HasForeignKey(s => s.SeasonId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
