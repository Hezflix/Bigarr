namespace Bigarr.Api.Features.Series.Persistence;

using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Common;
using Bigarr.Domain.Tv;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public sealed class SeriesVersionConfiguration : IEntityTypeConfiguration<SeriesVersion>
{
    public void Configure(EntityTypeBuilder<SeriesVersion> builder)
    {
        builder.ToTable("SeriesVersions");

        builder.Property(v => v.Name).HasMaxLength(Slug.MaxLength);
        builder.Property(v => v.FolderName).HasMaxLength(255);
        builder.Property(v => v.Constraints).HasJsonConversion();
        builder.Ignore(v => v.UpgradesAllowed);

        builder.HasIndex(v => new { v.SeriesId, v.Name }).IsUnique();

        // Exactly one default per series: partial unique index, like the films'.
        builder.HasIndex(v => v.SeriesId, "IX_SeriesVersions_SeriesId_IsDefault")
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

        builder.HasMany(v => v.Tags)
            .WithMany()
            .UsingEntity<SeriesVersionTag>(
                right => right.HasOne(t => t.Tag).WithMany().HasForeignKey(t => t.TagId).OnDelete(DeleteBehavior.Cascade),
                left => left.HasOne(t => t.Version).WithMany().HasForeignKey(t => t.VersionId).OnDelete(DeleteBehavior.Cascade),
                join =>
                {
                    join.ToTable("SeriesVersionTags");
                    join.HasKey(t => new { t.VersionId, t.TagId });
                });
    }
}
