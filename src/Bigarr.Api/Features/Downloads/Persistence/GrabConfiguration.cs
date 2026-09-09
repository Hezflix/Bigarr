using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Indexers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bigarr.Api.Features.Downloads.Persistence;

public sealed class GrabConfiguration : IEntityTypeConfiguration<Grab>
{
    public void Configure(EntityTypeBuilder<Grab> builder)
    {
        builder.ToTable("Grabs");

        builder.Property(g => g.ReleaseTitle).HasMaxLength(1024);
        builder.Property(g => g.DownloadId).HasMaxLength(128);
        builder.Property(g => g.ReleaseGuid).HasMaxLength(1024);
        builder.Property(g => g.InfoHash).HasMaxLength(64);
        builder.Property(g => g.IndexerName).HasMaxLength(128);
        builder.Property(g => g.DownloadClientName).HasMaxLength(128);
        builder.Property(g => g.StatusMessages).HasJsonConversion();
        builder.Ignore(g => g.IsActive);

        builder.HasIndex(g => g.DownloadId);
        builder.HasIndex(g => g.State);

        builder.HasMany(g => g.Targets)
            .WithOne(t => t.Grab)
            .HasForeignKey(t => t.GrabId)
            .OnDelete(DeleteBehavior.Cascade);

        // The indexer may be deleted after the grab; the snapshotted name keeps the row readable.
        builder.HasOne<Indexer>().WithMany().HasForeignKey(g => g.IndexerId).OnDelete(DeleteBehavior.SetNull);
    }
}
