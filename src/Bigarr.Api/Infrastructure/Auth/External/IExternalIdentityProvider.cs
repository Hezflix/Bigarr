namespace Bigarr.Api.Infrastructure.Auth.External;

/// <summary>
/// The person's account at a provider, as the provider vouches for it: an identity, never a role.
/// <c>ExternalId</c> is the only field matched on; the others are shown.
/// </summary>
public sealed record ExternalIdentity(string ExternalId, string Username, string? Email, string? ThumbUrl);

/// <summary>
/// A sign-in started at the provider. The browser opens <c>AuthUrl</c> (or shows <c>UserCode</c> for a provider whose
/// person types a code elsewhere); <c>State</c> is the provider's own handle, kept server-side and handed back to
/// <see cref="IExternalIdentityProvider.PollAsync"/>.
/// </summary>
public sealed record ExternalChallenge(string State, string? AuthUrl, string? UserCode, DateTimeOffset ExpiresAt);

public enum ExternalPollStatus
{
    /// <summary>The person has not answered yet.</summary>
    Pending,

    /// <summary>The provider no longer knows the sign-in: it ran out of time.</summary>
    Expired,

    /// <summary>The provider answered but would not vouch for anyone.</summary>
    Denied,

    /// <summary>The person signed in: <see cref="ExternalPoll.Identity"/> is set.</summary>
    Completed,
}

public sealed record ExternalPoll(ExternalPollStatus Status, ExternalIdentity? Identity = null)
{
    public static readonly ExternalPoll Pending = new(ExternalPollStatus.Pending);

    public static readonly ExternalPoll Expired = new(ExternalPollStatus.Expired);

    public static readonly ExternalPoll Denied = new(ExternalPollStatus.Denied);
}

/// <summary>
/// An outside service that can say who someone is, with a start-then-poll sign-in: the browser is sent to the provider,
/// Bigarr asks the provider until the person has answered. Plex's PIN flow is one; a code typed in another app
/// (Jellyfin's Quick Connect) fits the same shape. Throws <see cref="ExternalIdentityException"/> when the provider
/// cannot be reached; never logs or returns the provider's tokens.
/// </summary>
public interface IExternalIdentityProvider
{
    /// <summary>Stable key stored on links and used in routes, e.g. <c>plex</c>.</summary>
    string Key { get; }

    /// <summary>Shown on buttons, e.g. <c>Plex</c>.</summary>
    string DisplayName { get; }

    Task<ExternalChallenge> StartAsync(CancellationToken ct);

    Task<ExternalPoll> PollAsync(ExternalChallenge challenge, CancellationToken ct);
}

/// <summary>The provider could not be reached or answered something unreadable. The message is safe to show.</summary>
public sealed class ExternalIdentityException(string message, Exception? inner = null) : Exception(message, inner);
