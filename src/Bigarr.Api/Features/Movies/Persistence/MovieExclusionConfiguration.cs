using Bigarr.Domain.Movies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bigarr.Api.Features.Movies.Persistence;

public sealed class MovieExclusionConfiguration : IEntityTypeConfiguration<MovieExclusion>
{
    public void Configure(EntityTypeBuilder<MovieExclusion> builder)
    {
        builder.ToTable("MovieExclusions");
        builder.Property(e => e.Title).HasMaxLength(500);
        builder.HasIndex(e => e.TmdbId).IsUnique();
    }
}
