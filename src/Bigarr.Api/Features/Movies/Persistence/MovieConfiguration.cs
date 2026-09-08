using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Movies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bigarr.Api.Features.Movies.Persistence;

public sealed class MovieConfiguration : IEntityTypeConfiguration<Movie>
{
    public void Configure(EntityTypeBuilder<Movie> builder)
    {
        builder.ToTable("Movies");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Title).HasMaxLength(500);
        builder.Property(m => m.OriginalTitle).HasMaxLength(500);
        builder.Property(m => m.SortTitle).HasMaxLength(500);
        builder.Property(m => m.ImdbId).HasMaxLength(16);
        builder.Property(m => m.Certification).HasMaxLength(32);
        builder.Property(m => m.OriginalLanguage).HasMaxLength(8);
        builder.Property(m => m.Ratings).HasJsonConversion();
        builder.Property(m => m.AlternativeTitles).HasJsonConversion();
        builder.Property(m => m.Images).HasJsonConversion();
        builder.Ignore(m => m.Monitored);

        builder.HasIndex(m => m.TmdbId).IsUnique();
        builder.HasIndex(m => m.ImdbId);
        builder.HasIndex(m => m.SortTitle);

        builder.HasOne(m => m.DefaultRootFolder)
            .WithMany()
            .HasForeignKey(m => m.DefaultRootFolderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(m => m.Versions)
            .WithOne(v => v.Movie)
            .HasForeignKey(v => v.MovieId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(m => m.Tags)
            .WithMany()
            .UsingEntity<MovieTag>(
                right => right.HasOne(mt => mt.Tag).WithMany().HasForeignKey(mt => mt.TagId).OnDelete(DeleteBehavior.Cascade),
                left => left.HasOne(mt => mt.Movie).WithMany().HasForeignKey(mt => mt.MovieId).OnDelete(DeleteBehavior.Cascade),
                join =>
                {
                    join.ToTable("MovieTags");
                    join.HasKey(mt => new { mt.MovieId, mt.TagId });
                });
    }
}
