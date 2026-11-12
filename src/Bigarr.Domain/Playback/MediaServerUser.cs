using Bigarr.Domain.MediaServers;

namespace Bigarr.Domain.Playback;

/// <summary>
/// Someone who plays from a media server: the owner or a friend the library is shared with. Learnt from the sessions
/// they open, never a Bigarr <c>User</c>: no password, no email, no token.
/// </summary>
public sealed class MediaServerUser
{
    public int Id { get; set; }

    public int MediaServerId { get; set; }

    public MediaServer MediaServer { get; set; } = null!;

    /// <summary>The server's account id (Plex: <c>User.id</c> of a session), unique per server.</summary>
    public required string ExternalId { get; set; }

    /// <summary>The account's name on the server, refreshed whenever it plays.</summary>
    public required string Name { get; set; }

    /// <summary>A name chosen in Bigarr, shown instead of <see cref="Name"/> when set.</summary>
    public string? DisplayName { get; set; }

    public string? ThumbUrl { get; set; }

    public bool IsOwner { get; set; }

    /// <summary>Off: the user's sessions show in Now playing but no play is written.</summary>
    public bool KeepHistory { get; set; } = true;

    /// <summary>
    /// The server is still shared with the account, as plex.tv last said (the owner always is). Null until known; a
    /// Tautulli import can give a first idea, which plex.tv then overrules.
    /// </summary>
    public bool? OnServer { get; set; }

    /// <summary>When plex.tv last answered about <see cref="OnServer"/>; null while it is only the import's idea.</summary>
    public DateTime? OnServerCheckedAt { get; set; }

    public DateTime FirstSeenAt { get; set; }

    /// <summary>
    /// When they were last seen playing: the start of a live stream, or the end of their last imported play. Null for
    /// someone never seen play: an account the Tautulli import brought along with no history.
    /// </summary>
    public DateTime? LastSeenAt { get; set; }

    public string ShownName => string.IsNullOrWhiteSpace(DisplayName) ? Name : DisplayName;
}
