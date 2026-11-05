namespace Bigarr.Domain.Notifications;

/// <summary>
/// What Radarr's Discord connection lets the user tune (<c>DiscordSettings</c>): the webhook's name and avatar, the
/// embed author, and which fields each kind of message carries. Stored as JSON on the endpoint.
/// </summary>
/// <param name="Username">Overrides the name the webhook posts under; Discord's own name for the webhook when empty.</param>
/// <param name="Avatar">Overrides the webhook's avatar: an image URL.</param>
/// <param name="Author">The embed author; <c>Bigarr</c> when empty, as Radarr puts its instance name.</param>
/// <param name="GrabFields">On a grab, and on a failed download (the grabbed release that failed).</param>
/// <param name="ImportFields">On an import and an upgrade; its poster and fanart choices also apply to added and deleted films.</param>
/// <param name="ManualInteractionFields">On an import failure, Radarr's "manual interaction required".</param>
public sealed record DiscordSettings(
    string? Username = null,
    string? Avatar = null,
    string? Author = null,
    List<DiscordGrabField>? GrabFields = null,
    List<DiscordImportField>? ImportFields = null,
    List<DiscordManualInteractionField>? ManualInteractionFields = null)
{
    /// <summary>Radarr's defaults: every grab field.</summary>
    public static IReadOnlyList<DiscordGrabField> DefaultGrabFields { get; } = Enum.GetValues<DiscordGrabField>();

    /// <summary>Radarr's defaults: every import field but the custom formats and their score.</summary>
    public static IReadOnlyList<DiscordImportField> DefaultImportFields { get; } =
        Enum.GetValues<DiscordImportField>().Where(f => f is not (DiscordImportField.CustomFormats or DiscordImportField.CustomFormatScore)).ToList();

    /// <summary>Radarr's defaults: every field.</summary>
    public static IReadOnlyList<DiscordManualInteractionField> DefaultManualInteractionFields { get; } = Enum.GetValues<DiscordManualInteractionField>();

    public static DiscordSettings Default { get; } = new DiscordSettings().Normalized();

    /// <summary>Blank texts become null, a missing field list takes Radarr's defaults, duplicates go.</summary>
    public DiscordSettings Normalized() => new(
        Blank(Username),
        Blank(Avatar),
        Blank(Author),
        [.. (GrabFields ?? DefaultGrabFields).Distinct().Order()],
        [.. (ImportFields ?? DefaultImportFields).Distinct().Order()],
        [.. (ManualInteractionFields ?? DefaultManualInteractionFields).Distinct().Order()]);

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Radarr's <c>DiscordGrabFieldType</c>, same values, plus the version the release is for.</summary>
public enum DiscordGrabField
{
    Overview = 0,
    Rating = 1,
    Genres = 2,
    Quality = 3,
    Group = 4,
    Size = 5,
    Links = 6,
    Release = 7,
    Poster = 8,
    Fanart = 9,
    Indexer = 10,
    CustomFormats = 11,
    CustomFormatScore = 12,
    Tags = 13,
    Version = 14,
}

/// <summary>Radarr's <c>DiscordImportFieldType</c>, same values, plus the version the file belongs to.</summary>
public enum DiscordImportField
{
    Overview = 0,
    Rating = 1,
    Genres = 2,
    Quality = 3,
    Codecs = 4,
    Group = 5,
    Size = 6,
    Languages = 7,
    Subtitles = 8,
    Links = 9,
    Release = 10,
    Poster = 11,
    Fanart = 12,
    Tags = 13,
    CustomFormats = 14,
    CustomFormatScore = 15,
    Version = 16,
}

/// <summary>Radarr's <c>DiscordManualInteractionFieldType</c>, same values, plus the version the download was for.</summary>
public enum DiscordManualInteractionField
{
    Overview = 0,
    Rating = 1,
    Genres = 2,
    Quality = 3,
    Group = 4,
    Size = 5,
    Links = 6,
    DownloadTitle = 7,
    Poster = 8,
    Fanart = 9,
    Tags = 10,
    Version = 11,
}
