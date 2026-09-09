using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Indexers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bigarr.Api.Features.Blocklist.Persistence;

public sealed class BlocklistEntryConfiguration : IEntityTypeConfiguration<BlocklistEntry>
{
    public void Configure(EntityTypeBuilder<BlocklistEntry> builder)
    {
        builder.ToTable("Blocklist");

        builder.Property(e => e.SourceTitle).HasMaxLength(1024);
        builder.Property(e => e.ReleaseGuid).HasMaxLength(1024);
        builder.Property(e => e.InfoHash).HasMaxLength(64);
        builder.Property(e => e.IndexerName).HasMaxLength(128);
        builder.Property(e => e.Reason).HasMaxLength(1024);

        builder.HasOne(e => e.Target)
            .WithMany()
            .HasForeignKey(e => e.TargetId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Indexer>().WithMany().HasForeignKey(e => e.IndexerId).OnDelete(DeleteBehavior.SetNull);

        // Unique on the guid when there is one; the title fallback is enforced by the application.
        builder.HasIndex(e => new { e.TargetId, e.ReleaseGuid })
            .IsUnique()
            .HasFilter("\"ReleaseGuid\" IS NOT NULL");
        builder.HasIndex(e => new { e.TargetId, e.SourceTitle });
        builder.HasIndex(e => e.InfoHash);
    }
}
