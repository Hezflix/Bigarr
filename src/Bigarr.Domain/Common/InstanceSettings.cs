namespace Bigarr.Domain.Common;

/// <summary>Single row (id 1): values that belong to this install and are generated once, never typed by anyone.</summary>
public sealed class InstanceSettings
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    /// <summary>
    /// The <c>X-Plex-Client-Identifier</c> this install sends to plex.tv and to Plex servers: a GUID generated on first
    /// use and kept, as Radarr and Sonarr do. plex.tv lists each install as its own device under it.
    /// </summary>
    public string? PlexClientIdentifier { get; set; }
}
