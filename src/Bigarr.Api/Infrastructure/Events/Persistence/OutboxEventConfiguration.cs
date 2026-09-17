using Bigarr.Domain.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bigarr.Api.Infrastructure.Events.Persistence;

public sealed class OutboxEventConfiguration : IEntityTypeConfiguration<OutboxEvent>
{
    public void Configure(EntityTypeBuilder<OutboxEvent> builder)
    {
        builder.ToTable("OutboxEvents");
        builder.Property(e => e.Type).HasMaxLength(64);
        builder.Property(e => e.Payload).HasColumnType("TEXT");
        builder.HasIndex(e => e.DispatchedAt);
    }
}
