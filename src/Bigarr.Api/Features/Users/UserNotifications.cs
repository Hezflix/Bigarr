using Bigarr.Domain.Auth;
using Bigarr.Domain.Events;
using Bigarr.Domain.Notifications;

namespace Bigarr.Api.Features.Users;

/// <summary>A user's Discord id and mention opt-ins, as their profile and Settings → Users read and write them.</summary>
public static class UserNotifications
{
    public const string InvalidDiscordId = "A Discord user id is 17 to 20 digits (Discord: User Settings → Advanced → Developer Mode, then Copy User ID).";

    private static readonly Dictionary<string, string> Descriptions = DomainEventCatalog.All.ToDictionary(e => e.Type, e => e.Description, StringComparer.Ordinal);

    /// <summary>The choices offered; a user who handles no one else's requests does not see those that only concern managers.</summary>
    public static List<MentionChoiceResponse> Choices(bool managers) =>
        [.. Mentions.Choices
            .Select(c => new MentionChoiceResponse(c, Descriptions.GetValueOrDefault(c, c), Mentions.ManagerChoices.Contains(c, StringComparer.Ordinal)))
            .Where(c => managers || !c.ManagersOnly)];

    public static MyNotificationsResponse Mine(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var managers = Mentions.IsManager(user);
        var choices = Choices(managers);
        var mentions = Mentions.Effective(user.DiscordMentions).Where(m => choices.Any(c => c.Type == m)).ToList();
        return new MyNotificationsResponse(user.DiscordUserId, mentions, choices);
    }

    /// <summary>
    /// Writes the id and the opt-ins; false (and nothing written) when the id is no Discord id. Choices the caller was
    /// not shown keep their current value: a user does not turn off what they cannot see.
    /// </summary>
    public static bool Apply(User user, UserNotificationsDto request, bool everyChoice)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(request);
        var id = Mentions.NormalizeDiscordUserId(request.DiscordUserId, out var valid);
        if (!valid)
        {
            return false;
        }

        var shown = Choices(everyChoice).Select(c => c.Type).ToHashSet(StringComparer.Ordinal);
        var current = Mentions.Effective(user.DiscordMentions);
        user.DiscordUserId = id;
        user.DiscordMentions = [.. Mentions.Choices.Where(c => shown.Contains(c) ? (request.Mentions ?? []).Contains(c, StringComparer.Ordinal) : current.Contains(c, StringComparer.Ordinal))];
        return true;
    }
}
