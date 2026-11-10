using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Shadow;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bigarr.Api.Features.Shadow.Persistence;

public sealed class ReleaseSightingConfiguration : IEntityTypeConfiguration<ReleaseSighting>
{
    public void Configure(EntityTypeBuilder<ReleaseSighting> builder)
    {
        builder.ToTable("ReleaseSightings");

        builder.Property(s => s.IndexerName).HasMaxLength(128);
        builder.Property(s => s.Guid).HasMaxLength(1024);
        builder.Property(s => s.InfoHash).HasMaxLength(64);
        builder.Property(s => s.Title).HasMaxLength(1024);
        builder.Property(s => s.UnmatchedReason).HasMaxLength(512);
        builder.Property(s => s.Episodes).HasMaxLength(128);
        builder.Property(s => s.AttributedVersion).HasMaxLength(64);
        builder.Property(s => s.Verdicts).HasJsonConversion();

        // No foreign key on the indexer: a sighting outlives it. Sightings are kept for good and only ever looked up by
        // release (guid, info hash or title), so each of those is indexed and nothing else.
        builder.HasIndex(s => new { s.IndexerName, s.Guid }).IsUnique();
        builder.HasIndex(s => s.Guid);
        builder.HasIndex(s => s.InfoHash);
        builder.HasIndex(s => s.Title);
    }
}
