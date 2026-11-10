using System.Globalization;
using System.Text.Json;
using Bigarr.Domain.Playback;

namespace Bigarr.Api.Infrastructure.MediaServers;

/// <summary>A stream as <c>/status/sessions</c> shows it: who, what, where it is, and the stream's details.</summary>
public sealed record PlexSession(
    string SessionKey,
    string? SessionId,
    string RatingKey,
    MediaItemKind Kind,
    string UserId,
    string UserName,
    string? UserThumb,
    PlaybackState State,
    long ViewOffsetMs,
    long? DurationMs,
    string? PartId,
    StreamDetails Details,
    PlexTranscode? Transcode = null);

/// <summary>
/// How a transcode is keeping up, live only: speed (1.0 = real time), throttled (ahead enough that Plex slows down,
/// which is good), how far ahead of the player it is, and the hardware doing the work.
/// </summary>
public sealed record PlexTranscode(double? Speed, bool? Throttled, long? BufferedMs, string? HardwareDecoding, string? HardwareEncoding, string? Protocol, bool? Complete);

/// <summary>A <c>playing</c> notification: Plex sends one every ~10 s per stream, and on every state change.</summary>
public sealed record PlexPlayingEvent(string SessionKey, string RatingKey, string State, long? ViewOffsetMs)
{
    /// <summary>Plex's keep-alive, passed on so the listener knows the stream is up.</summary>
    public static readonly PlexPlayingEvent Ping = new(string.Empty, string.Empty, "ping", null);

    public bool IsPing => ReferenceEquals(this, Ping);

    public bool IsStopped => string.Equals(State, "stopped", StringComparison.OrdinalIgnoreCase);
}

/// <summary>One file of an item (Plex merges several files of a title as "versions" of one item).</summary>
public sealed record PlexPart(
    string Id,
    string? File,
    string? Container,
    string? VideoResolution,
    string? VideoCodec,
    string? DynamicRange,
    string? AudioCodec,
    int? AudioChannels,
    int? Bitrate);

/// <summary>An item's metadata from <c>/library/metadata/{ratingKey}</c>: identity, ids, duration, credits, files.</summary>
public sealed record PlexItem(
    string RatingKey,
    MediaItemKind Kind,
    string? Guid,
    PlexIds Ids,
    string Title,
    int? Year,
    long? DurationMs,
    long? CreditsStartMs,
    string? LibrarySectionId,
    string? LibrarySectionTitle,
    string? ShowRatingKey,
    string? ShowTitle,
    string? SeasonRatingKey,
    int? SeasonNumber,
    int? EpisodeNumber,
    IReadOnlyList<PlexPart> Parts);

public sealed record PlexIds(int? TmdbId, string? ImdbId, int? TvdbId);

/// <summary>A viewed mark of Plex's watch history (<c>/status/sessions/history/all</c>): who finished what, and when.</summary>
public sealed record PlexHistoryEntry(string RatingKey, MediaItemKind Kind, string AccountId, DateTime ViewedAt, string Title, string? LibrarySectionId);

/// <summary>One page of the watch history and how many marks the whole query has.</summary>
public sealed record PlexHistoryPage(List<PlexHistoryEntry> Entries, int Total);

/// <summary>An account of the server (<c>/accounts</c>): the owner is 1, the friends their plex.tv id, like in the sessions.</summary>
public sealed record PlexAccount(string Id, string Name, string? Thumb);

public sealed record PlexSection(string Key, string Title, string? Type, IReadOnlyList<string> Locations)
{
    public bool IsMovies => string.Equals(Type, "movie", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Reads Plex's playback JSON. Plex documents little of it: the shapes follow what Tautulli reads and what a real
/// server answers. Ids come as strings or numbers depending on the endpoint, booleans as <c>true</c> or <c>1</c>.
/// </summary>
public static class PlexPlayback
{
    public static List<PlexSession> ParseSessions(JsonElement root)
    {
        var sessions = new List<PlexSession>();
        foreach (var video in Metadata(root))
        {
            var kind = KindOf(Str(video, "type"));
            var sessionKey = Str(video, "sessionKey");
            var ratingKey = Str(video, "ratingKey");
            if (kind is null || sessionKey is null || ratingKey is null || !video.TryGetProperty("User", out var user) || Str(user, "id") is not { } userId)
            {
                continue;
            }

            video.TryGetProperty("Player", out var player);
            video.TryGetProperty("Session", out var session);
            var transcode = video.TryGetProperty("TranscodeSession", out var ts) && ts.ValueKind == JsonValueKind.Object ? ts : (JsonElement?)null;
            var media = Selected(video, "Media");
            var part = media is { } m ? Selected(m, "Part") : null;

            var details = new StreamDetails
            {
                PlayerTitle = Str(player, "title"),
                PlayerProduct = Str(player, "product"),
                PlayerPlatform = Str(player, "platform"),
                PlayerPlatformVersion = Str(player, "platformVersion"),
                PlayerDevice = Str(player, "device"),
                PlayerId = Str(player, "machineIdentifier"),
                IpAddress = Str(player, "address"),
                Local = Bool(player, "local"),
                Secure = Bool(player, "secure"),
                Relayed = Bool(player, "relayed"),
                Bandwidth = Int(session, "bandwidth"),
                StreamContainer = transcode is { } t1 ? Str(t1, "container") ?? Str(media, "container") : Str(media, "container"),
                StreamVideoResolution = Str(media, "videoResolution"),
                StreamVideoCodec = transcode is { } t2 ? Str(t2, "videoCodec") ?? Str(media, "videoCodec") : Str(media, "videoCodec"),
                StreamAudioCodec = transcode is { } t3 ? Str(t3, "audioCodec") ?? Str(media, "audioCodec") : Str(media, "audioCodec"),
                StreamAudioChannels = transcode is { } t4 ? Int(t4, "audioChannels") ?? Int(media, "audioChannels") : Int(media, "audioChannels"),
                StreamBitrate = Int(media, "bitrate"),
            };

            var videoDecision = transcode is { } tv ? Str(tv, "videoDecision") : null;
            var audioDecision = transcode is { } ta ? Str(ta, "audioDecision") : null;
            details.VideoDecision = videoDecision ?? "directplay";
            details.AudioDecision = audioDecision ?? "directplay";
            details.Decision = Is(videoDecision, "transcode") || Is(audioDecision, "transcode") ? StreamDecision.Transcode
                : transcode is not null ? StreamDecision.DirectStream
                : StreamDecision.DirectPlay;
            details.SubtitleDecision = part is { } p ? Streams(p).Where(s => Int(s, "streamType") == 3 && Bool(s, "selected") == true).Select(s => Str(s, "decision")).FirstOrDefault() : null;
            details.HardwareTranscode = transcode is { } th && details.Decision == StreamDecision.Transcode
                ? Bool(th, "transcodeHwFullPipeline") == true || Str(th, "transcodeHwEncoding") is not null || Str(th, "transcodeHwDecoding") is not null
                : null;

            sessions.Add(new PlexSession(
                sessionKey,
                Str(session, "id"),
                ratingKey,
                kind.Value,
                userId,
                Str(user, "title") ?? userId,
                Str(user, "thumb"),
                StateOf(Str(player, "state")),
                Long(video, "viewOffset") ?? 0,
                Long(video, "duration"),
                part is { } pp ? Str(pp, "id") : null,
                details,
                transcode is { } tp ? TranscodeOf(tp, Long(video, "viewOffset") ?? 0) : null));
        }

        return sessions;
    }

    public static PlexItem? ParseItem(JsonElement root)
    {
        var item = Metadata(root).FirstOrDefault();
        if (item.ValueKind != JsonValueKind.Object || KindOf(Str(item, "type")) is not { } kind || Str(item, "ratingKey") is not { } ratingKey)
        {
            return null;
        }

        var parts = new List<PlexPart>();
        foreach (var media in Array(item, "Media"))
        {
            foreach (var part in Array(media, "Part"))
            {
                if (Str(part, "id") is not { } partId)
                {
                    continue;
                }

                var video = Streams(part).FirstOrDefault(s => Int(s, "streamType") == 1);
                parts.Add(new PlexPart(
                    partId,
                    Str(part, "file"),
                    Str(media, "container") ?? Str(part, "container"),
                    Str(media, "videoResolution"),
                    Str(media, "videoCodec"),
                    video.ValueKind == JsonValueKind.Object ? DynamicRangeOf(video) : null,
                    Str(media, "audioCodec"),
                    Int(media, "audioChannels"),
                    Int(media, "bitrate")));
            }
        }

        var credits = Array(item, "Marker").Where(m => Is(Str(m, "type"), "credits")).Select(m => Long(m, "startTimeOffset")).Min();

        var guid = Str(item, "guid");
        var ids = IdsOf(item, guid);
        return new PlexItem(
            ratingKey,
            kind,
            guid,
            ids,
            Str(item, "title") ?? ratingKey,
            Int(item, "year"),
            Long(item, "duration"),
            credits,
            Str(item, "librarySectionID"),
            Str(item, "librarySectionTitle"),
            Str(item, "grandparentRatingKey"),
            Str(item, "grandparentTitle"),
            Str(item, "parentRatingKey"),
            Int(item, "parentIndex"),
            kind == MediaItemKind.Episode ? Int(item, "index") : null,
            parts);
    }

    /// <summary>The guid, external ids and title of any item (a show, a season), or null when Plex has no such item.</summary>
    public static (string? Guid, PlexIds Ids, string? Title)? ParseIds(JsonElement root)
    {
        var item = Metadata(root).FirstOrDefault();
        if (item.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var guid = Str(item, "guid");
        return (guid, IdsOf(item, guid), Str(item, "title"));
    }

    /// <summary>The marks of a history page; those of anything but a film or an episode (music, photos) are skipped.</summary>
    public static PlexHistoryPage ParseHistory(JsonElement root)
    {
        var entries = new List<PlexHistoryEntry>();
        var count = 0;
        foreach (var item in Metadata(root))
        {
            count++;
            if (KindOf(Str(item, "type")) is not { } kind || Str(item, "ratingKey") is not { } ratingKey || Str(item, "accountID") is not { } account || Long(item, "viewedAt") is not { } viewedAt)
            {
                continue;
            }

            var title = Str(item, "title") ?? ratingKey;
            entries.Add(new PlexHistoryEntry(ratingKey, kind, account, DateTimeOffset.FromUnixTimeSeconds(viewedAt).UtcDateTime, title, Str(item, "librarySectionID")));
        }

        var total = root.TryGetProperty("MediaContainer", out var container) ? Int(container, "totalSize") ?? Int(container, "size") ?? count : count;
        return new PlexHistoryPage(entries, total);
    }

    public static List<PlexAccount> ParseAccounts(JsonElement root)
    {
        var accounts = new List<PlexAccount>();
        if (!root.TryGetProperty("MediaContainer", out var container))
        {
            return accounts;
        }

        foreach (var account in Array(container, "Account"))
        {
            if (Str(account, "id") is { } id && Str(account, "name") is { } name)
            {
                accounts.Add(new PlexAccount(id, name, Str(account, "thumb")));
            }
        }

        return accounts;
    }

    public static List<PlexSection> ParseSections(JsonElement root)
    {
        var sections = new List<PlexSection>();
        if (!root.TryGetProperty("MediaContainer", out var container))
        {
            return sections;
        }

        foreach (var directory in Array(container, "Directory"))
        {
            if (Str(directory, "key") is not { Length: > 0 } key)
            {
                continue;
            }

            var locations = Array(directory, "Location").Select(l => Str(l, "path")).OfType<string>().Where(p => p.Length > 0).ToList();
            sections.Add(new PlexSection(key, Str(directory, "title") ?? key, Str(directory, "type"), locations));
        }

        return sections;
    }

    /// <summary>The <c>data:</c> of a server-sent <c>playing</c> event, or null for anything else.</summary>
    public static PlexPlayingEvent? ParsePlayingEvent(string data)
    {
        try
        {
            using var doc = JsonDocument.Parse(data);
            var root = doc.RootElement;
            if (root.TryGetProperty("NotificationContainer", out var container))
            {
                root = container;
            }

            if (!root.TryGetProperty("PlaySessionStateNotification", out var notification))
            {
                return null;
            }

            if (notification.ValueKind == JsonValueKind.Array)
            {
                notification = notification.EnumerateArray().FirstOrDefault();
            }

            return Str(notification, "sessionKey") is { } sessionKey && Str(notification, "ratingKey") is { } ratingKey
                ? new PlexPlayingEvent(sessionKey, ratingKey, Str(notification, "state") ?? "playing", Long(notification, "viewOffset"))
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// TMDB, IMDb and TVDB ids from the item's <c>Guid</c> list (new agents), or from a legacy agent guid
    /// (<c>com.plexapp.agents.imdb://tt0133093?lang=en</c>, <c>com.plexapp.agents.thetvdb://81189/1/2</c>).
    /// </summary>
    public static PlexIds IdsOf(JsonElement item, string? guid) =>
        IdsOf([.. Array(item, "Guid").Select(entry => Str(entry, "id")), guid]);

    /// <summary>
    /// TMDB, IMDb and TVDB ids from guids, the first of each kind winning: <c>tmdb://</c>, <c>imdb://</c>, <c>tvdb://</c>
    /// and the legacy agents' <c>com.plexapp.agents.themoviedb|imdb|thetvdb://</c> (a TVDB episode guid gives the show).
    /// </summary>
    public static PlexIds IdsOf(IEnumerable<string?> guids)
    {
        ArgumentNullException.ThrowIfNull(guids);

        int? tmdb = null;
        string? imdb = null;
        int? tvdb = null;
        foreach (var id in guids)
        {
            var sep = id?.IndexOf("://", StringComparison.Ordinal) ?? -1;
            if (id is null || sep < 0)
            {
                continue;
            }

            var scheme = id[..sep];
            var value = id[(sep + 3)..];
            value = value.Split('?', '/')[0];
            switch (scheme)
            {
                case "tmdb" or "com.plexapp.agents.themoviedb" when tmdb is null && int.TryParse(value, CultureInfo.InvariantCulture, out var t):
                    tmdb = t;
                    break;
                case "imdb" or "com.plexapp.agents.imdb" when imdb is null && value.StartsWith("tt", StringComparison.Ordinal):
                    imdb = value;
                    break;
                case "tvdb" or "com.plexapp.agents.thetvdb" when tvdb is null && int.TryParse(value, CultureInfo.InvariantCulture, out var v):
                    tvdb = v;
                    break;
            }
        }

        return new PlexIds(tmdb, imdb, tvdb);
    }

    private static PlexTranscode TranscodeOf(JsonElement transcode, long viewOffsetMs)
    {
        // maxOffsetAvailable is in seconds of media already transcoded.
        long? buffered = Str(transcode, "maxOffsetAvailable") is { } max && double.TryParse(max, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            ? Math.Max(0, (long)(seconds * 1000) - viewOffsetMs)
            : null;
        double? speed = Str(transcode, "speed") is { } s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
        return new PlexTranscode(
            speed,
            Bool(transcode, "throttled"),
            buffered,
            Str(transcode, "transcodeHwDecodingTitle"),
            Str(transcode, "transcodeHwEncodingTitle"),
            Str(transcode, "protocol"),
            Bool(transcode, "complete"));
    }

    private static string DynamicRangeOf(JsonElement video)
    {
        if (Bool(video, "DOVIPresent") == true)
        {
            return "DV";
        }

        return Str(video, "colorTrc") switch
        {
            "smpte2084" => "HDR10",
            "arib-std-b67" => "HLG",
            _ => "SDR",
        };
    }

    private static MediaItemKind? KindOf(string? type) => type switch
    {
        "movie" => MediaItemKind.Movie,
        "episode" => MediaItemKind.Episode,
        _ => null,
    };

    public static PlaybackState StateOf(string? state) => state?.ToLowerInvariant() switch
    {
        "paused" => PlaybackState.Paused,
        "buffering" => PlaybackState.Buffering,
        _ => PlaybackState.Playing,
    };

    private static IEnumerable<JsonElement> Metadata(JsonElement root) =>
        root.TryGetProperty("MediaContainer", out var container) ? Array(container, "Metadata") : [];

    private static IEnumerable<JsonElement> Streams(JsonElement part) => Array(part, "Stream");

    private static IEnumerable<JsonElement> Array(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array ? array.EnumerateArray() : [];

    /// <summary>The element marked <c>selected</c>, else the first.</summary>
    private static JsonElement? Selected(JsonElement element, string name)
    {
        JsonElement? first = null;
        foreach (var candidate in Array(element, name))
        {
            if (Bool(candidate, "selected") == true)
            {
                return candidate;
            }

            first ??= candidate;
        }

        return first;
    }

    private static bool Is(string? value, string expected) => string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);

    private static string? Str(JsonElement? element, string name)
    {
        if (element is not { ValueKind: JsonValueKind.Object } e || !e.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() is { Length: > 0 } s ? s : null,
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };
    }

    private static long? Long(JsonElement? element, string name) =>
        Str(element, name) is { } s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? (long)d : null;

    private static int? Int(JsonElement? element, string name) => Long(element, name) is { } l ? (int)Math.Clamp(l, int.MinValue, int.MaxValue) : null;

    private static bool? Bool(JsonElement? element, string name)
    {
        if (element is not { ValueKind: JsonValueKind.Object } e || !e.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => value.GetRawText() != "0",
            JsonValueKind.String => value.GetString() is "1" or "true",
            _ => null,
        };
    }
}
