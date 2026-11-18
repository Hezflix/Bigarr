using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bigarr.Api.Infrastructure.Persistence;

/// <summary>
/// A one-time data repair a migration queued and a startup task carries out, for a repair too involved for SQL. The
/// task removes its row in the same save as the repair, so it runs once, and again only if that save failed.
/// </summary>
public sealed class PendingRepair
{
    public required string Name { get; set; }
}

public sealed class PendingRepairConfiguration : IEntityTypeConfiguration<PendingRepair>
{
    public void Configure(EntityTypeBuilder<PendingRepair> builder)
    {
        builder.ToTable("PendingRepairs");
        builder.HasKey(r => r.Name);
        builder.Property(r => r.Name).HasMaxLength(64);
    }
}
