using Bigarr.Domain.Acquisition;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bigarr.Api.Features.Downloads.Persistence;

public sealed class GrabTargetConfiguration : IEntityTypeConfiguration<GrabTarget>
{
    public void Configure(EntityTypeBuilder<GrabTarget> builder)
    {
        builder.ToTable("GrabTargets");
        builder.HasKey(t => new { t.GrabId, t.TargetId });

        builder.HasOne(t => t.Target)
            .WithMany(a => a.Grabs)
            .HasForeignKey(t => t.TargetId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(t => t.TargetId, "IX_GrabTargets_TargetId");

        // Never two active downloads for the same target.
        builder.HasIndex(t => t.TargetId, "IX_GrabTargets_TargetId_Active")
            .IsUnique()
            .HasFilter("\"Active\" = 1");
    }
}
