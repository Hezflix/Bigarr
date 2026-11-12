namespace Bigarr.Api.Features.Series.Persistence;

using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Tv;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public sealed class EpisodeFileConfiguration : IEntityTypeConfiguration<EpisodeFile>
{
    public void Configure(EntityTypeBuilder<EpisodeFile> builder)
    {
        builder.ToTable("EpisodeFiles");

        builder.Property(f => f.RelativePath).HasMaxLength(1024);
        builder.Property(f => f.ReleaseGroup).HasMaxLength(255);
        builder.Property(f => f.OriginalReleaseTitle).HasMaxLength(1024);
        builder.Property(f => f.MediaInfo).HasJsonConversion();
        builder.Property(f => f.LinkedFromPath).HasMaxLength(2048);
        builder.Ignore(f => f.IsFilmLink);

        builder.OwnsOne(f => f.Revision, revision =>
        {
            revision.Property(r => r.Version).HasColumnName("RevisionVersion");
            revision.Property(r => r.Real).HasColumnName("RevisionReal");
            revision.Property(r => r.IsRepack).HasColumnName("IsRepack");
        });
        builder.Navigation(f => f.Revision).IsRequired();

        // One live file per path in a version folder.
        builder.HasIndex(f => new { f.VersionId, f.RelativePath }).IsUnique();

        builder.HasOne(f => f.Version)
            .WithMany(v => v.Files)
            .HasForeignKey(f => f.VersionId)
            .OnDelete(DeleteBehavior.Cascade);

        // A film hardlink outlives its link row for one pass: the keeper then keeps it as a plain file or removes it.
        builder.HasOne(f => f.SeriesMovieLink)
            .WithMany()
            .HasForeignKey(f => f.SeriesMovieLinkId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(f => f.Quality)
            .WithMany()
            .HasForeignKey(f => f.QualityId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
