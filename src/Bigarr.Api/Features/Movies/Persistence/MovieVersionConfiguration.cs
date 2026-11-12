using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Common;
using Bigarr.Domain.Movies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bigarr.Api.Features.Movies.Persistence;

public sealed class MovieVersionConfiguration : IEntityTypeConfiguration<MovieVersion>
{
    public void Configure(EntityTypeBuilder<MovieVersion> builder)
    {
        builder.ToTable("MovieVersions");

        builder.Property(v => v.Name).HasMaxLength(Slug.MaxLength);
        builder.Property(v => v.FolderName).HasMaxLength(255);
        builder.Property(v => v.Constraints).HasJsonConversion();
        builder.Ignore(v => v.UpgradesAllowed);

        builder.HasIndex(v => new { v.MovieId, v.Name }).IsUnique();

        // Exactly one default per movie: partial unique index.
        builder.HasIndex(v => v.MovieId, "IX_MovieVersions_MovieId_IsDefault")
            .IsUnique()
            .HasFilter("\"IsDefault\" = 1");

        builder.HasOne(v => v.QualityProfile)
            .WithMany()
            .HasForeignKey(v => v.QualityProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(v => v.RootFolder)
            .WithMany()
            .HasForeignKey(v => v.RootFolderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(v => v.File)
            .WithOne(f => f.Version)
            .HasForeignKey<MediaFile>(f => f.VersionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(v => v.Tags)
            .WithMany()
            .UsingEntity<MovieVersionTag>(
                right => right.HasOne(t => t.Tag).WithMany().HasForeignKey(t => t.TagId).OnDelete(DeleteBehavior.Cascade),
                left => left.HasOne(t => t.Version).WithMany().HasForeignKey(t => t.VersionId).OnDelete(DeleteBehavior.Cascade),
                join =>
                {
                    join.ToTable("MovieVersionTags");
                    join.HasKey(t => new { t.VersionId, t.TagId });
                });
    }
}
