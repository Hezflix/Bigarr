using Bigarr.Domain.Tv;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bigarr.Api.Features.Series.Persistence;

public sealed class SeriesExclusionConfiguration : IEntityTypeConfiguration<SeriesExclusion>
{
    public void Configure(EntityTypeBuilder<SeriesExclusion> builder)
    {
        builder.ToTable("SeriesExclusions");

        builder.Property(e => e.Title).HasMaxLength(512);
        builder.HasIndex(e => e.TvdbId).IsUnique();
    }
}
