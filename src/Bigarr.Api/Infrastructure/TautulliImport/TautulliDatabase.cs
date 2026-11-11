using System.Globalization;
using Bigarr.Domain.Playback;
using Microsoft.Data.Sqlite;

namespace Bigarr.Api.Infrastructure.TautulliImport;

/// <summary>A Tautulli account (<c>users</c>). Emails and tokens are never read.</summary>
public sealed record TautulliUser(long UserId, string Username, string? FriendlyName, string? Thumb, bool IsAdmin, bool KeepHistory, bool Deleted);

/// <summary>
/// What Tautulli knows of a rating key, from the newest history row that has metadata. <see cref="Guids"/> holds every
/// guid the key had, newest first: an agent switch or a re-match changes the guid but keeps the key.
/// </summary>
public sealed record TautulliItem(
    string RatingKey,
    string MediaType,
    string Title,
    int? Year,
    long? DurationMs,
    long? CreditsStartMs,
    IReadOnlyList<string> Guids,
    string? SectionId,
    string? ParentRatingKey,
    string? GrandparentRatingKey,
    string? GrandparentTitle,
    int? SeasonNumber,
    int? EpisodeNumber,
    bool Live,
    string? OriginalTitle = null)
{
    public string? Guid => Guids.Count > 0 ? Guids[0] : null;
}

/// <summary>Ids Tautulli looked up for a rating key (<c>themoviedb_lookup</c>, <c>tvmaze_lookup</c>): a film's own, a show's for an episode.</summary>
public sealed record TautulliLookup(int? TmdbId, string? ImdbId, int? TvdbId);

/// <summary>One <c>session_history</c> row with its <c>session_history_media_info</c>: one stream.</summary>
/// <param name="GroupId">Tautulli's <c>reference_id</c>: the rows of one grouped play share it.</param>
public sealed record TautulliRow(
    long Id,
    long GroupId,
    DateTime StartedAt,
    DateTime StoppedAt,
    string RatingKey,
    long UserId,
    string? UserName,
    int PausedSeconds,
    long ViewOffsetMs,
    string? MediaType,
    long? MediaDurationMs,
    StreamDetails Details);

/// <summary>
/// A copy of Tautulli's <c>tautulli.db</c>, read-only. The file is opened immutable: nothing is written, no journal is
/// created next to it, and a database Tautulli is still writing may be read half-written, hence a copy. Columns are
/// looked up first, so a database of an older Tautulli reads too, with what it lacks left null.
/// </summary>
public sealed class TautulliDatabase : IDisposable
{
    private static readonly string[] RequiredTables = ["users", "session_history", "session_history_metadata"];

    private readonly SqliteConnection _connection;
    private readonly Dictionary<string, HashSet<string>> _columns = new(StringComparer.Ordinal);

    private TautulliDatabase(SqliteConnection connection) => _connection = connection;

    /// <summary>Opens the file, or throws <see cref="TautulliDatabaseException"/> when it is missing or not a Tautulli database.</summary>
    public static TautulliDatabase Open(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new TautulliDatabaseException("The path of the Tautulli database is required.");
        }

        var full = Path.GetFullPath(path.Trim());
        if (!File.Exists(full))
        {
            throw new TautulliDatabaseException($"No file at {full}.");
        }

        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = new Uri(full).AbsoluteUri + "?immutable=1",
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());

        var database = new TautulliDatabase(connection);
        try
        {
            connection.Open();
            foreach (var table in RequiredTables)
            {
                if (database.Columns(table).Count == 0)
                {
                    throw new TautulliDatabaseException($"{Path.GetFileName(full)} is not a Tautulli database: it has no {table} table.");
                }
            }
        }
        catch (SqliteException ex)
        {
            database.Dispose();
            throw new TautulliDatabaseException($"{Path.GetFileName(full)} cannot be read as a SQLite database: {ex.Message}", ex);
        }
        catch
        {
            database.Dispose();
            throw;
        }

        return database;
    }

    /// <summary>The Tautulli version that last wrote the file, when it says.</summary>
    public string? Version()
    {
        if (Columns("version_info").Count == 0)
        {
            return null;
        }

        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT value FROM version_info WHERE key = 'version'";
        return command.ExecuteScalar() as string;
    }

    public List<TautulliUser> Users()
    {
        var users = new List<TautulliUser>();
        using var command = _connection.CreateCommand();
        command.CommandText = $"""
            SELECT user_id, username, {Col("users", "friendly_name")}, {Col("users", "thumb")}, {Col("users", "is_admin")},
                   {Col("users", "keep_history")}, {Col("users", "deleted_user")}
            FROM users WHERE user_id IS NOT NULL
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            users.Add(new TautulliUser(
                reader.GetInt64(0),
                Text(reader, 1) ?? reader.GetInt64(0).ToString(CultureInfo.InvariantCulture),
                Text(reader, 2),
                Text(reader, 3),
                Long(reader, 4) == 1,
                Long(reader, 5) != 0,
                Long(reader, 6) == 1));
        }

        return users;
    }

    /// <summary>Every rating key the history played, described by its newest row with metadata.</summary>
    public Dictionary<string, TautulliItem> Items()
    {
        var guids = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        using (var command = _connection.CreateCommand())
        {
            command.CommandText = """
                SELECT h.rating_key, m.guid, MAX(h.id) AS last
                FROM session_history h JOIN session_history_metadata m ON m.id = h.id
                WHERE m.guid IS NOT NULL AND m.guid <> ''
                GROUP BY h.rating_key, m.guid
                ORDER BY h.rating_key, last DESC
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var key = Key(reader, 0);
                if (!guids.TryGetValue(key, out var list))
                {
                    guids[key] = list = [];
                }

                list.Add(reader.GetString(1));
            }
        }

        var items = new Dictionary<string, TautulliItem>(StringComparer.Ordinal);
        using (var command = _connection.CreateCommand())
        {
            const string m = "session_history_metadata";
            command.CommandText = $"""
                SELECT h.rating_key, COALESCE(m.media_type, h.media_type), m.title, {Col(m, "year", "m")}, {Col(m, "duration", "m")},
                       {Col(m, "marker_credits_first", "m")}, h.section_id, {Col(m, "parent_rating_key", "m")}, {Col(m, "grandparent_rating_key", "m")},
                       {Col(m, "grandparent_title", "m")}, {Col(m, "parent_media_index", "m")}, {Col(m, "media_index", "m")}, {Col(m, "live", "m")},
                       {Col(m, "original_title", "m")}
                FROM session_history h
                JOIN (SELECT h2.rating_key AS rk, MAX(h2.id) AS last
                      FROM session_history h2 JOIN session_history_metadata m2 ON m2.id = h2.id GROUP BY h2.rating_key) l ON l.last = h.id
                JOIN session_history_metadata m ON m.id = h.id
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var key = Key(reader, 0);
                items[key] = new TautulliItem(
                    key,
                    Text(reader, 1) ?? string.Empty,
                    Text(reader, 2) ?? key,
                    Int(reader, 3),
                    Long(reader, 4) is > 0 and var duration ? duration : null,
                    Long(reader, 5) is > 0 and var credits ? credits : null,
                    guids.TryGetValue(key, out var known) ? known : [],
                    KeyOrNull(reader, 6),
                    KeyOrNull(reader, 7),
                    KeyOrNull(reader, 8),
                    Text(reader, 9),
                    Int(reader, 10),
                    Int(reader, 11),
                    Long(reader, 12) == 1,
                    Text(reader, 13) is { } original && !string.Equals(original, Text(reader, 2), StringComparison.Ordinal) ? original : null);
            }
        }

        return items;
    }

    /// <summary>Rating keys the history played with no metadata row at all: known by their history rows only.</summary>
    public List<TautulliItem> ItemsWithoutMetadata()
    {
        var items = new List<TautulliItem>();
        using var command = _connection.CreateCommand();
        command.CommandText = $"""
            SELECT h.rating_key, {Col("session_history", "media_type", "h")}, h.section_id, {Col("session_history", "parent_rating_key", "h")},
                   {Col("session_history", "grandparent_rating_key", "h")}
            FROM session_history h
            JOIN (SELECT rating_key AS rk, MAX(id) AS last FROM session_history
                  WHERE rating_key NOT IN (SELECT h2.rating_key FROM session_history h2 JOIN session_history_metadata m2 ON m2.id = h2.id)
                  GROUP BY rating_key) l ON l.last = h.id
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var key = Key(reader, 0);
            items.Add(new TautulliItem(key, Text(reader, 1) ?? string.Empty, key, null, null, null, [], KeyOrNull(reader, 2), KeyOrNull(reader, 3), KeyOrNull(reader, 4), null, null, null, false));
        }

        return items;
    }

    /// <summary>Every account id the history names, with the name it played under: some may be missing from <c>users</c>.</summary>
    public Dictionary<long, string?> HistoryUsers()
    {
        var users = new Dictionary<long, string?>();
        using var command = _connection.CreateCommand();
        command.CommandText = $"SELECT user_id, MAX({Col("session_history", "user")}) FROM session_history WHERE user_id IS NOT NULL GROUP BY user_id";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            users[reader.GetInt64(0)] = Text(reader, 1);
        }

        return users;
    }

    /// <summary>Tautulli's TMDB and TVmaze lookups by rating key; TMDB's wins where both have one.</summary>
    public Dictionary<string, TautulliLookup> Lookups()
    {
        var lookups = new Dictionary<string, TautulliLookup>(StringComparer.Ordinal);
        if (Columns("tvmaze_lookup").Count > 0)
        {
            Read($"SELECT rating_key, NULL, {Col("tvmaze_lookup", "imdb_id")}, {Col("tvmaze_lookup", "thetvdb_id")} FROM tvmaze_lookup");
        }

        if (Columns("themoviedb_lookup").Count > 0)
        {
            Read($"SELECT rating_key, {Col("themoviedb_lookup", "themoviedb_id")}, {Col("themoviedb_lookup", "imdb_id")}, {Col("themoviedb_lookup", "thetvdb_id")} FROM themoviedb_lookup");
        }

        return lookups;

        void Read(string sql)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = sql;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (reader.IsDBNull(0))
                {
                    continue;
                }

                var key = Key(reader, 0);
                var found = new TautulliLookup(Int(reader, 1) is > 0 and var t ? t : null, Text(reader, 2) is { } i && i.StartsWith("tt", StringComparison.Ordinal) ? i : null, Int(reader, 3) is > 0 and var v ? v : null);
                lookups[key] = lookups.TryGetValue(key, out var known)
                    ? new TautulliLookup(found.TmdbId ?? known.TmdbId, found.ImdbId ?? known.ImdbId, found.TvdbId ?? known.TvdbId)
                    : found;
            }
        }
    }

    /// <summary>How many grouped plays the history holds (rows sharing a <c>reference_id</c> count once).</summary>
    public int GroupCount()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT COUNT(DISTINCT COALESCE(reference_id, id)) FROM session_history";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    /// <summary>Every history row with its media info, the rows of a group together and in order.</summary>
    public IEnumerable<TautulliRow> Rows(CancellationToken ct = default)
    {
        const string h = "session_history";
        const string mi = "session_history_media_info";
        var hasMedia = Columns(mi).Count > 0;
        string M(string column) => hasMedia ? Col(mi, column, "mi") : "NULL";

        using var command = _connection.CreateCommand();
        command.CommandText = $"""
            SELECT h.id, COALESCE(h.reference_id, h.id), h.started, h.stopped, h.rating_key, h.user_id, {Col(h, "user", "h")},
                   {Col(h, "paused_counter", "h")}, {Col(h, "view_offset", "h")}, {Col(h, "media_type", "h")},
                   {Col(h, "player", "h")}, {Col(h, "product", "h")}, {Col(h, "platform", "h")}, {Col(h, "platform_version", "h")},
                   {Col(h, "machine_id", "h")}, {Col(h, "ip_address", "h")}, {Col(h, "location", "h")}, {Col(h, "secure", "h")},
                   {Col(h, "relayed", "h")}, {Col(h, "bandwidth", "h")},
                   {M("transcode_decision")}, {M("video_decision")}, {M("audio_decision")}, {M("stream_video_decision")},
                   {M("stream_audio_decision")}, {M("stream_subtitle_decision")}, {M("transcode_hw_decoding")}, {M("transcode_hw_encoding")},
                   {M("container")}, {M("video_resolution")}, {M("video_codec")}, {M("video_dynamic_range")}, {M("audio_codec")},
                   {M("audio_channels")}, {M("bitrate")}, {M("stream_container")}, {M("stream_video_resolution")}, {M("stream_video_codec")},
                   {M("stream_audio_codec")}, {M("stream_audio_channels")}, {M("stream_bitrate")}, {M("duration")}
            FROM session_history h {(hasMedia ? "LEFT JOIN session_history_media_info mi ON mi.id = h.id" : string.Empty)}
            WHERE h.rating_key IS NOT NULL AND h.user_id IS NOT NULL AND h.started IS NOT NULL
            ORDER BY COALESCE(h.reference_id, h.id), h.started, h.id
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            var started = Long(reader, 2) ?? 0;
            var stopped = Math.Max(started, Long(reader, 3) ?? started);
            var transcodeDecision = Text(reader, 20);
            var hwDecoding = Long(reader, 26);
            var hwEncoding = Long(reader, 27);
            var location = Text(reader, 16);
            var details = new StreamDetails
            {
                PlayerTitle = Text(reader, 10),
                PlayerProduct = Text(reader, 11),
                PlayerPlatform = Text(reader, 12),
                PlayerPlatformVersion = Text(reader, 13),
                PlayerId = Text(reader, 14),
                IpAddress = Text(reader, 15),
                Local = location is null ? null : string.Equals(location, "lan", StringComparison.OrdinalIgnoreCase),
                Secure = Bool(reader, 17),
                Relayed = Bool(reader, 18),
                Bandwidth = Int(reader, 19),
                Decision = DecisionOf(transcodeDecision),
                VideoDecision = DecisionText(Text(reader, 23) ?? Text(reader, 21)),
                AudioDecision = DecisionText(Text(reader, 24) ?? Text(reader, 22)),
                SubtitleDecision = DecisionText(Text(reader, 25)),
                HardwareTranscode = DecisionOf(transcodeDecision) == StreamDecision.Transcode && (hwDecoding is not null || hwEncoding is not null)
                    ? hwDecoding == 1 || hwEncoding == 1
                    : null,
                SourceContainer = Text(reader, 28),
                SourceVideoResolution = Text(reader, 29),
                SourceVideoCodec = Text(reader, 30),
                SourceDynamicRange = DynamicRangeOf(Text(reader, 31)),
                SourceAudioCodec = Text(reader, 32),
                SourceAudioChannels = Int(reader, 33),
                SourceBitrate = Int(reader, 34),
                StreamContainer = Text(reader, 35),
                StreamVideoResolution = Text(reader, 36),
                StreamVideoCodec = Text(reader, 37),
                StreamAudioCodec = Text(reader, 38),
                StreamAudioChannels = Int(reader, 39),
                StreamBitrate = Int(reader, 40),
            };

            yield return new TautulliRow(
                reader.GetInt64(0),
                reader.GetInt64(1),
                DateTime.UnixEpoch.AddSeconds(started),
                DateTime.UnixEpoch.AddSeconds(stopped),
                Key(reader, 4),
                reader.GetInt64(5),
                Text(reader, 6),
                Math.Max(0, Int(reader, 7) ?? 0),
                Math.Max(0, Long(reader, 8) ?? 0),
                Text(reader, 9),
                Long(reader, 41) is > 0 and var duration ? duration : null,
                details);
        }
    }

    public void Dispose() => _connection.Dispose();

    /// <summary>Tautulli's <c>transcode_decision</c>: <c>direct play</c>, <c>copy</c> or <c>transcode</c>.</summary>
    public static StreamDecision? DecisionOf(string? decision) => decision?.Trim().ToLowerInvariant() switch
    {
        "direct play" or "directplay" => StreamDecision.DirectPlay,
        "copy" => StreamDecision.DirectStream,
        "transcode" => StreamDecision.Transcode,
        _ => null,
    };

    /// <summary>A stream decision as Plex words it in its sessions (<c>directplay</c>), as the live streams store it.</summary>
    private static string? DecisionText(string? decision) =>
        decision is null ? null : string.Equals(decision.Trim(), "direct play", StringComparison.OrdinalIgnoreCase) ? "directplay" : decision.Trim().ToLowerInvariant();

    /// <summary>Tautulli's dynamic range label in the live streams' words: SDR, HDR10, HLG or DV.</summary>
    private static string? DynamicRangeOf(string? range)
    {
        if (range is null)
        {
            return null;
        }

        if (range.Contains("Dolby Vision", StringComparison.OrdinalIgnoreCase) || range.Contains("DoVi", StringComparison.OrdinalIgnoreCase))
        {
            return "DV";
        }

        if (range.Contains("HLG", StringComparison.OrdinalIgnoreCase))
        {
            return "HLG";
        }

        return range.Contains("HDR", StringComparison.OrdinalIgnoreCase) ? "HDR10" : range.Contains("SDR", StringComparison.OrdinalIgnoreCase) ? "SDR" : range;
    }

    private HashSet<string> Columns(string table)
    {
        if (_columns.TryGetValue(table, out var known))
        {
            return known;
        }

        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT name FROM pragma_table_info($table)";
        command.Parameters.AddWithValue("$table", table);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            columns.Add(reader.GetString(0));
        }

        _columns[table] = columns;
        return columns;
    }

    /// <summary>The column when this database has it, NULL otherwise.</summary>
    private string Col(string table, string column, string? alias = null) =>
        Columns(table).Contains(column) ? (alias is null ? column : $"{alias}.{column}") : "NULL";

    private static string Key(SqliteDataReader reader, int ordinal) => KeyOrNull(reader, ordinal) ?? string.Empty;

    /// <summary>A rating key or section id, stored as an integer by Tautulli and as a string by Plex and Bigarr.</summary>
    private static string? KeyOrNull(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture) is { Length: > 0 } key ? key : null;

    private static string? Text(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture)?.Trim() is { Length: > 0 } text ? text : null;

    private static long? Long(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        var value = reader.GetValue(ordinal);
        return value switch
        {
            long l => l,
            double d => (long)d,
            string s when long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => (long)parsed,
            _ => null,
        };
    }

    private static int? Int(SqliteDataReader reader, int ordinal) => Long(reader, ordinal) is { } value ? (int)Math.Clamp(value, int.MinValue, int.MaxValue) : null;

    private static bool? Bool(SqliteDataReader reader, int ordinal) => Long(reader, ordinal) is { } value ? value != 0 : null;
}

public sealed class TautulliDatabaseException(string message, Exception? inner = null) : Exception(message, inner);
