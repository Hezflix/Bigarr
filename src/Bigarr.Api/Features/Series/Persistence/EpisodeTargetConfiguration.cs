namespace Bigarr.Api.Features.Series.Persistence;

using Bigarr.Domain.Tv;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

/// <summary>
/// TPT child of <c>AcquisitionTargets</c>. The database cascades only remove this table's row: deleting a version or an
/// episode must go through the base rows (<c>SeriesService</c> deletes them first), or the base table keeps orphans.
/// </summary>
public sealed class EpisodeTargetConfiguration : IEntityTypeConfiguration<EpisodeTarget>
{
    public void Configure(EntityTypeBuilder<EpisodeTarget> builder)
    {
        builder.ToTable("EpisodeTargets");

        builder.Ignore(t => t.HasFile);
        builder.Ignore(t => t.QualityProfileId);
        builder.Ignore(t => t.Constraints);
        builder.Ignore(t => t.UpgradesAllowed);

        builder.HasIndex(t => new { t.VersionId, t.EpisodeId }).IsUnique();
        builder.HasIndex(t => t.EpisodeId);

        builder.HasOne(t => t.Version)
            .WithMany(v => v.Episodes)
            .HasForeignKey(t => t.VersionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(t => t.Episode)
            .WithMany(e => e.Targets)
            .HasForeignKey(t => t.EpisodeId)
            .OnDelete(DeleteBehavior.Cascade);

        // A file going away makes its episodes missing again; it never deletes them.
        builder.HasOne(t => t.EpisodeFile)
            .WithMany(f => f.Episodes)
            .HasForeignKey(t => t.EpisodeFileId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
