using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Common;
using Bigarr.Domain.History;
using Bigarr.Domain.Indexers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bigarr.Api.Features.History.Persistence;

public sealed class HistoryEventConfiguration : IEntityTypeConfiguration<HistoryEvent>
{
    public void Configure(EntityTypeBuilder<HistoryEvent> builder)
    {
        builder.ToTable("History");

        builder.Property(h => h.MovieTitle).HasMaxLength(500);
        builder.Property(h => h.SeriesTitle).HasMaxLength(500);
        builder.Property(h => h.VersionName).HasMaxLength(Slug.MaxLength);
        builder.Property(h => h.SourceTitle).HasMaxLength(1024);
        builder.Property(h => h.IndexerName).HasMaxLength(128);
        builder.Property(h => h.Data).HasJsonConversion();

        // The journal survives every deletion: nullable FKs set to null, snapshots keep the meaning.
        builder.HasOne(h => h.Target).WithMany().HasForeignKey(h => h.TargetId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(h => h.Movie).WithMany().HasForeignKey(h => h.MovieId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Bigarr.Domain.Tv.Series>().WithMany().HasForeignKey(h => h.SeriesId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(h => h.MediaFile).WithMany().HasForeignKey(h => h.MediaFileId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Indexer>().WithMany().HasForeignKey(h => h.IndexerId).OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(h => h.CreatedAt);
        builder.HasIndex(h => h.EventType);
        builder.HasIndex(h => h.SeriesId);
    }
}
