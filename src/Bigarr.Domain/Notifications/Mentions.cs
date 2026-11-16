using Bigarr.Domain.Auth;
using Bigarr.Domain.Events;

namespace Bigarr.Domain.Notifications;

/// <summary>Who an event concerns: the person it is about, and whether the people who handle requests and issues should hear of it.</summary>
/// <param name="UserId">The requester (or reporter) the event is about; null when it is not for them.</param>
/// <param name="Managers">Admins and request managers, who approve requests and handle issues.</param>
/// <param name="Actor">The username of who caused the event, never mentioned for their own act.</param>
public sealed record MentionTarget(int? UserId, bool Managers, string? Actor);

/// <summary>
/// Who a request or issue message on Discord mentions (<c>&lt;@id&gt;</c>), like Seerr: the user the event is about,
/// or the people who handle it, each only with a Discord user id set and the event in their opt-ins, never for what they
/// did themselves. Pure: the sender passes the users.
/// </summary>
public static class Mentions
{
    /// <summary>The events a user may choose to be mentioned on, in the order the settings list them.</summary>
    public static IReadOnlyList<string> Choices { get; } = new[]
    {
        typeof(RequestCreated), typeof(RequestApproved), typeof(RequestDeclined), typeof(RequestAvailable), typeof(RequestFailed),
    }.Select(DomainEventCatalog.NameOf).ToList();

    /// <summary>Choices only an admin or a request manager is ever mentioned on: someone else's request waiting for approval.</summary>
    public static IReadOnlyList<string> ManagerChoices { get; } = new[] { typeof(RequestCreated) }.Select(DomainEventCatalog.NameOf).ToList();

    /// <summary>A Discord user id is a snowflake: 17 to 20 digits.</summary>
    public static bool IsDiscordUserId(string? value) =>
        value is { Length: >= 17 and <= 20 } && value.All(char.IsAsciiDigit);

    /// <summary>Blank becomes null, spaces and a pasted <c>&lt;@…&gt;</c> are trimmed; null when what is left is not a snowflake.</summary>
    public static string? NormalizeDiscordUserId(string? value, out bool valid)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            valid = true;
            return null;
        }

        if (trimmed.StartsWith("<@", StringComparison.Ordinal) && trimmed.EndsWith('>'))
        {
            trimmed = trimmed[2..^1].TrimStart('!');
        }

        valid = IsDiscordUserId(trimmed);
        return valid ? trimmed : null;
    }

    /// <summary>A user's opt-ins as stored: null (never chosen) means every choice; unknown names go.</summary>
    public static IReadOnlyList<string> Effective(IEnumerable<string>? stored) =>
        stored is null ? Choices : Choices.Where(c => stored.Contains(c, StringComparer.Ordinal)).ToList();

    /// <summary>Who the event concerns, or null when its message mentions no one.</summary>
    public static MentionTarget? Target(DomainEvent domainEvent) => domainEvent switch
    {
        // Someone else's request, waiting for approval; one approved at once needs nobody.
        RequestCreated { AutoApproved: false } e => new MentionTarget(null, Managers: true, e.Request.RequestedBy),
        RequestCreated => null,

        // Approved by the user's own flags or by themselves: they know.
        RequestApproved e => new MentionTarget(e.Request.RequestedByUserId, Managers: false, e.AutoApproved ? e.Request.RequestedBy : e.ApprovedBy),
        RequestDeclined e => new MentionTarget(e.Request.RequestedByUserId, Managers: false, e.DeclinedBy),
        RequestAvailable e => new MentionTarget(e.Request.RequestedByUserId, Managers: false, null),

        // The requester waits on it, and someone has to act on it.
        RequestFailed e => new MentionTarget(e.Request.RequestedByUserId, Managers: true, null),
        _ => null,
    };

    /// <summary>The Discord ids to mention for the event, in user id order, each once.</summary>
    public static IReadOnlyList<string> For(DomainEvent domainEvent, IEnumerable<User> users)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        ArgumentNullException.ThrowIfNull(users);
        if (Target(domainEvent) is not { } target)
        {
            return [];
        }

        return users
            .Where(u => !u.IsDisabled && IsDiscordUserId(u.DiscordUserId))
            .Where(u => target.Actor is null || !string.Equals(u.Username, target.Actor, StringComparison.Ordinal))
            .Where(u => u.Id == target.UserId || (target.Managers && IsManager(u)))
            .Where(u => Effective(u.DiscordMentions).Contains(domainEvent.Type, StringComparer.Ordinal))
            .OrderBy(u => u.Id)
            .Select(u => u.DiscordUserId!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    public static bool IsManager(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return user.Role is UserRole.Admin or UserRole.RequestManager;
    }
}
