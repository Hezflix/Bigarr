using Bigarr.Domain.Acquisition;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bigarr.Api.Infrastructure.Persistence.Configurations;

/// <summary>Base table of the TPT hierarchy. Owned by Infrastructure, never by a slice.</summary>
public sealed class AcquisitionTargetConfiguration : IEntityTypeConfiguration<AcquisitionTarget>
{
    public void Configure(EntityTypeBuilder<AcquisitionTarget> builder)
    {
        builder.ToTable("AcquisitionTargets");
        builder.UseTptMappingStrategy();

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Kind).IsRequired();
        builder.HasIndex(t => new { t.Kind, t.Monitored });
    }
}
