using Bigarr.Domain.Tags;

namespace Bigarr.Domain.Notifications;

public sealed class NotificationEndpointTag
{
    public int EndpointId { get; set; }

    public NotificationEndpoint Endpoint { get; set; } = null!;

    public int TagId { get; set; }

    public Tag Tag { get; set; } = null!;
}
