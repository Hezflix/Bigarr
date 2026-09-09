using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Movies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bigarr.Api.Features.Movies.Persistence;

public sealed class MediaFileConfiguration : IEntityTypeConfiguration<MediaFile>
{
    public void Configure(EntityTypeBuilder<MediaFile> builder)
    {
        builder.ToTable("MediaFiles");

        builder.Property(f => f.RelativePath).HasMaxLength(1024);
        builder.Property(f => f.Edition).HasMaxLength(255);
        builder.Property(f => f.ReleaseGroup).HasMaxLength(255);
        builder.Property(f => f.OriginalReleaseTitle).HasMaxLength(1024);
        builder.Property(f => f.MediaInfo).HasJsonConversion();

        builder.OwnsOne(f => f.Revision, revision =>
        {
            revision.Property(r => r.Version).HasColumnName("RevisionVersion");
            revision.Property(r => r.Real).HasColumnName("RevisionReal");
            revision.Property(r => r.IsRepack).HasColumnName("IsRepack");
        });
        builder.Navigation(f => f.Revision).IsRequired();

        builder.HasOne(f => f.Quality)
            .WithMany()
            .HasForeignKey(f => f.QualityId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
