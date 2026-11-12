namespace Bigarr.Api.Features.SeriesFilms.Persistence;

using Bigarr.Domain.Tv;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public sealed class SeriesMovieLinkConfiguration : IEntityTypeConfiguration<SeriesMovieLink>
{
    public void Configure(EntityTypeBuilder<SeriesMovieLink> builder)
    {
        builder.ToTable("SeriesMovieLinks");

        builder.Property(l => l.LinkError).HasMaxLength(1000);

        // One film per special; a film may be the special of several shows.
        builder.HasIndex(l => l.EpisodeId).IsUnique();
        builder.HasIndex(l => l.MovieId);
        builder.HasIndex(l => l.SeriesId);

        builder.HasOne(l => l.Episode)
            .WithOne(e => e.MovieLink)
            .HasForeignKey<SeriesMovieLink>(l => l.EpisodeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(l => l.Series)
            .WithMany()
            .HasForeignKey(l => l.SeriesId)
            .OnDelete(DeleteBehavior.Cascade);

        // Deleting the film ends the link; its hardlinks are then settled by the link keeper (orphan rule).
        builder.HasOne(l => l.Movie)
            .WithMany()
            .HasForeignKey(l => l.MovieId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
