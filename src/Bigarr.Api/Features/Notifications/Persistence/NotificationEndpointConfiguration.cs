using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bigarr.Api.Features.Notifications.Persistence;

public sealed class NotificationEndpointConfiguration : IEntityTypeConfiguration<NotificationEndpoint>
{
    public void Configure(EntityTypeBuilder<NotificationEndpoint> builder)
    {
        builder.ToTable("NotificationEndpoints");
        builder.Property(e => e.Name).HasMaxLength(128);
        builder.Property(e => e.Url).HasMaxLength(2048);
        builder.Property(e => e.Secret).HasMaxLength(512);
        builder.Property(e => e.Events).HasJsonConversion();
        builder.HasIndex(e => e.Name).IsUnique();
    }
}

public sealed class WebhookDeliveryConfiguration : IEntityTypeConfiguration<WebhookDelivery>
{
    public void Configure(EntityTypeBuilder<WebhookDelivery> builder)
    {
        builder.ToTable("WebhookDeliveries");
        builder.Property(d => d.EventType).HasMaxLength(64);
        builder.Property(d => d.LastError).HasMaxLength(2048);
        builder.Ignore(d => d.State);

        builder.HasOne(d => d.Event).WithMany().HasForeignKey(d => d.EventId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(d => d.Endpoint).WithMany().HasForeignKey(d => d.EndpointId).OnDelete(DeleteBehavior.Cascade);

        // What the sender polls: pending deliveries that are due.
        builder.HasIndex(d => new { d.DeliveredAt, d.NextAttemptAt });
        builder.HasIndex(d => new { d.EndpointId, d.CreatedAt });
    }
}
