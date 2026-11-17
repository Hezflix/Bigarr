using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Bigarr.Api.Infrastructure.SeerrImport;

/// <summary>
/// A Seerr account (<c>user</c> with its <c>user_settings</c>). Passwords, Plex and Jellyfin tokens, reset links and
/// avatars are never read.
/// </summary>
/// <param name="UserType">Seerr's <c>UserType</c>: 1 Plex, 2 local, 3 Jellyfin, 4 Emby.</param>
/// <param name="DiscordIds">The Discord user ids of the user's notification settings: Seerr keeps a list, Overseerr one id.</param>
/// <param name="DiscordTypes">The Discord bits of the user's <c>notificationTypes</c>; null when the settings hold none.</param>
public sealed record SeerrUser(
    int Id,
    string? Email,
    string? Username,
    string? PlexUsername,
    string? JellyfinUsername,
    long? PlexId,
    long Permissions,
    int UserType,
    DateTime? CreatedAt,
    int? MovieQuotaLimit,
    int? MovieQuotaDays,
    int? TvQuotaLimit,
    int? TvQuotaDays,
    string? Locale,
    IReadOnlyList<string> DiscordIds,
    long? DiscordTypes)
{
    /// <summary>The name Seerr shows: its own username, else the Plex or Jellyfin one, else the start of the email.</summary>
    public string DisplayName =>
        Username is { Length: > 0 } ? Username
        : PlexUsername is { Length: > 0 } ? PlexUsername
        : JellyfinUsername is { Length: > 0 } ? JellyfinUsername
        : Email is { Length: > 0 } && Email.Split('@')[0] is { Length: > 0 } local ? local
        : $"seerr-user-{Id.ToString(CultureInfo.InvariantCulture)}";
}

/// <summary>A title Seerr tracks (<c>media</c>): by TMDB id, a series by its TVDB id too.</summary>
/// <param name="Status">Seerr's <c>MediaStatus</c> of the standard version: 5 available, 4 partially available.</param>
public sealed record SeerrMedia(int Id, string MediaType, int TmdbId, int? TvdbId, int Status, int Status4K);

/// <summary>A request (<c>media_request</c>) with the seasons it asked for (<c>season_request</c>).</summary>
/// <param name="Status">Seerr's <c>MediaRequestStatus</c>: 1 pending, 2 approved, 3 declined, 4 failed, 5 completed.</param>
public sealed record SeerrRequest(
    int Id,
    int Status,
    string Type,
    int? MediaId,
    int? RequestedById,
    int? ModifiedById,
    bool Is4K,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<int> Seasons);

/// <summary>An issue (<c>issue</c>). Its description is its first comment, as Seerr writes it.</summary>
/// <param name="IssueType">1 video, 2 audio, 3 subtitles, 4 other.</param>
/// <param name="Status">1 open, 2 resolved.</param>
/// <param name="ProblemSeason">0 for a whole film or series.</param>
/// <param name="ProblemEpisode">0 for a whole season.</param>
public sealed record SeerrIssue(
    int Id,
    int IssueType,
    int Status,
    int ProblemSeason,
    int ProblemEpisode,
    int? MediaId,
    int? CreatedById,
    int? ModifiedById,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record SeerrComment(int Id, int IssueId, int? UserId, string Message, DateTime CreatedAt);

/// <summary>A row of Seerr's Discover page (<c>discover_slider</c>).</summary>
/// <param name="Type">Seerr's <c>DiscoverSliderType</c>.</param>
/// <param name="Data">The row's value: a TMDB id, comma-separated keyword ids, or the words searched for.</param>
public sealed record SeerrSlider(int Id, int Type, int Order, bool IsBuiltIn, bool Enabled, string? Title, string? Data);

/// <summary>Everything the import reads from a Seerr database, at once: a few thousand rows at most.</summary>
public sealed record SeerrSnapshot(
    IReadOnlyList<SeerrUser> Users,
    IReadOnlyDictionary<int, SeerrMedia> Media,
    IReadOnlyList<SeerrRequest> Requests,
    IReadOnlyList<SeerrIssue> Issues,
    IReadOnlyList<SeerrComment> Comments,
    IReadOnlyList<SeerrSlider> Sliders,
    /// <summary>"Seerr", "Jellyseerr" or "Overseerr", told by the columns only one of them has.</summary>
    string Flavor);

/// <summary>
/// A copy of Seerr's <c>db.sqlite3</c> (Overseerr, Jellyseerr or Seerr), read-only. Opened immutable: nothing is written,
/// no journal is created next to it. Columns are looked up first, so an older Overseerr and the newer Seerr both read,
/// with what one lacks left null. Column names are those of Seerr's TypeORM entities and SQLite migrations.
/// </summary>
public sealed class SeerrDatabase : IDisposable
{
    private static readonly string[] RequiredTables = ["user", "media", "media_request"];

    private readonly SqliteConnection _connection;
    private readonly Dictionary<string, HashSet<string>> _columns = new(StringComparer.Ordinal);

    private SeerrDatabase(SqliteConnection connection) => _connection = connection;

    /// <summary>Opens the file, or throws <see cref="SeerrDatabaseException"/> when it is missing or not a Seerr database.</summary>
    public static SeerrDatabase Open(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new SeerrDatabaseException("The path of the Seerr database is required.");
        }

        var full = Path.GetFullPath(path.Trim());
        if (!File.Exists(full))
        {
            throw new SeerrDatabaseException($"No file at {full}.");
        }

        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = new Uri(full).AbsoluteUri + "?immutable=1",
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());

        var database = new SeerrDatabase(connection);
        try
        {
            connection.Open();
            foreach (var table in RequiredTables)
            {
                if (database.Columns(table).Count == 0)
                {
                    throw new SeerrDatabaseException($"{Path.GetFileName(full)} is not a Seerr database: it has no {table} table.");
                }
            }
        }
        catch (SqliteException ex)
        {
            database.Dispose();
            throw new SeerrDatabaseException($"{Path.GetFileName(full)} cannot be read as a SQLite database: {ex.Message}", ex);
        }
        catch
        {
            database.Dispose();
            throw;
        }

        return database;
    }

    public SeerrSnapshot Read() => new(Users(), Media(), Requests(), Issues(), Comments(), Sliders(), Flavor());

    /// <summary>Seerr renamed <c>discordId</c> to a list; Jellyseerr added Jellyfin accounts; Overseerr has neither.</summary>
    public string Flavor() =>
        Columns("user_settings").Contains("discordIds") ? "Seerr"
        : Columns("user").Contains("jellyfinUserId") ? "Jellyseerr"
        : "Overseerr";

    public List<SeerrUser> Users()
    {
        var settings = Settings();
        var users = new List<SeerrUser>();
        using var command = _connection.CreateCommand();
        command.CommandText = $"""
            SELECT id, {Col("user", "email")}, {Col("user", "username")}, {Col("user", "plexUsername")}, {Col("user", "jellyfinUsername")},
                   {Col("user", "plexId")}, {Col("user", "permissions")}, {Col("user", "userType")}, {Col("user", "createdAt")},
                   {Col("user", "movieQuotaLimit")}, {Col("user", "movieQuotaDays")}, {Col("user", "tvQuotaLimit")}, {Col("user", "tvQuotaDays")}
            FROM "user" ORDER BY id
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var id = reader.GetInt32(0);
            var setting = settings.GetValueOrDefault(id);
            users.Add(new SeerrUser(
                id,
                Text(reader, 1),
                Text(reader, 2),
                Text(reader, 3),
                Text(reader, 4),
                Long(reader, 5) is > 0 and var plexId ? plexId : null,
                Long(reader, 6) ?? 0,
                (int)(Long(reader, 7) ?? 1),
                Date(reader, 8),
                Int(reader, 9),
                Int(reader, 10),
                Int(reader, 11),
                Int(reader, 12),
                setting?.Locale,
                setting?.DiscordIds ?? [],
                setting?.DiscordTypes));
        }

        return users;
    }

    public Dictionary<int, SeerrMedia> Media()
    {
        var media = new Dictionary<int, SeerrMedia>();
        using var command = _connection.CreateCommand();
        command.CommandText = $"""
            SELECT id, mediaType, tmdbId, {Col("media", "tvdbId")}, {Col("media", "status")}, {Col("media", "status4k")}
            FROM media
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var id = reader.GetInt32(0);
            media[id] = new SeerrMedia(id, Text(reader, 1) ?? string.Empty, reader.GetInt32(2), Int(reader, 3) is > 0 and var tvdb ? tvdb : null, Int(reader, 4) ?? 1, Int(reader, 5) ?? 1);
        }

        return media;
    }

    public List<SeerrRequest> Requests()
    {
        var seasons = new Dictionary<int, List<int>>();
        if (Columns("season_request").Contains("requestId"))
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """SELECT requestId, seasonNumber FROM season_request WHERE requestId IS NOT NULL ORDER BY requestId, seasonNumber""";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var requestId = reader.GetInt32(0);
                if (!seasons.TryGetValue(requestId, out var list))
                {
                    seasons[requestId] = list = [];
                }

                if (!list.Contains(reader.GetInt32(1)))
                {
                    list.Add(reader.GetInt32(1));
                }
            }
        }

        var requests = new List<SeerrRequest>();
        using (var command = _connection.CreateCommand())
        {
            const string t = "media_request";
            command.CommandText = $"""
                SELECT id, status, type, {Col(t, "mediaId")}, {Col(t, "requestedById")}, {Col(t, "modifiedById")}, {Col(t, "is4k")},
                       {Col(t, "createdAt")}, {Col(t, "updatedAt")}
                FROM media_request ORDER BY id
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var id = reader.GetInt32(0);
                var created = Date(reader, 7) ?? DateTime.UnixEpoch;
                requests.Add(new SeerrRequest(
                    id,
                    reader.GetInt32(1),
                    Text(reader, 2) ?? string.Empty,
                    Int(reader, 3),
                    Int(reader, 4),
                    Int(reader, 5),
                    Long(reader, 6) == 1,
                    created,
                    Date(reader, 8) ?? created,
                    seasons.TryGetValue(id, out var list) ? list : []));
            }
        }

        return requests;
    }

    public List<SeerrIssue> Issues()
    {
        var issues = new List<SeerrIssue>();
        if (Columns("issue").Count == 0)
        {
            return issues;
        }

        using var command = _connection.CreateCommand();
        const string t = "issue";
        command.CommandText = $"""
            SELECT id, issueType, {Col(t, "status")}, {Col(t, "problemSeason")}, {Col(t, "problemEpisode")}, {Col(t, "mediaId")},
                   {Col(t, "createdById")}, {Col(t, "modifiedById")}, {Col(t, "createdAt")}, {Col(t, "updatedAt")}
            FROM issue ORDER BY id
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var created = Date(reader, 8) ?? DateTime.UnixEpoch;
            issues.Add(new SeerrIssue(
                reader.GetInt32(0),
                reader.GetInt32(1),
                Int(reader, 2) ?? 1,
                Int(reader, 3) ?? 0,
                Int(reader, 4) ?? 0,
                Int(reader, 5),
                Int(reader, 6),
                Int(reader, 7),
                created,
                Date(reader, 9) ?? created));
        }

        return issues;
    }

    public List<SeerrComment> Comments()
    {
        var comments = new List<SeerrComment>();
        if (Columns("issue_comment").Count == 0)
        {
            return comments;
        }

        using var command = _connection.CreateCommand();
        command.CommandText = $"""
            SELECT id, issueId, {Col("issue_comment", "userId")}, message, {Col("issue_comment", "createdAt")}
            FROM issue_comment WHERE issueId IS NOT NULL ORDER BY issueId, id
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            comments.Add(new SeerrComment(reader.GetInt32(0), reader.GetInt32(1), Int(reader, 2), Text(reader, 3) ?? string.Empty, Date(reader, 4) ?? DateTime.UnixEpoch));
        }

        return comments;
    }

    public List<SeerrSlider> Sliders()
    {
        var sliders = new List<SeerrSlider>();
        if (Columns("discover_slider").Count == 0)
        {
            return sliders;
        }

        using var command = _connection.CreateCommand();
        const string t = "discover_slider";
        command.CommandText = $"""
            SELECT id, type, "order", {Col(t, "isBuiltIn")}, {Col(t, "enabled")}, {Col(t, "title")}, {Col(t, "data")}
            FROM discover_slider ORDER BY "order", id
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            sliders.Add(new SeerrSlider(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), Long(reader, 3) == 1, Long(reader, 4) != 0, Text(reader, 5), Text(reader, 6)));
        }

        return sliders;
    }

    public void Dispose() => _connection.Dispose();

    private sealed record UserSetting(string? Locale, IReadOnlyList<string> DiscordIds, long? DiscordTypes);

    /// <summary>Each user's locale, Discord ids and Discord notification bits.</summary>
    private Dictionary<int, UserSetting> Settings()
    {
        var settings = new Dictionary<int, UserSetting>();
        var columns = Columns("user_settings");
        if (!columns.Contains("userId"))
        {
            return settings;
        }

        // Seerr keeps a JSON list of ids (discordIds); Overseerr and Jellyseerr one id (discordId).
        var discord = columns.Contains("discordIds") ? "discordIds" : columns.Contains("discordId") ? "discordId" : null;
        using var command = _connection.CreateCommand();
        command.CommandText = $"""
            SELECT userId, {Col("user_settings", "locale")}, {(discord is null ? "NULL" : $"\"{discord}\"")}, {Col("user_settings", "notificationTypes")}
            FROM user_settings WHERE userId IS NOT NULL
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var raw = Text(reader, 2);
            IReadOnlyList<string> ids = discord == "discordIds" ? JsonList(raw) : raw is { Length: > 0 } ? [raw.Trim()] : [];
            settings[reader.GetInt32(0)] = new UserSetting(Text(reader, 1), ids, DiscordBits(Text(reader, 3)));
        }

        return settings;
    }

    private static List<string> JsonList(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            return document.RootElement.ValueKind == JsonValueKind.Array
                ? [.. document.RootElement.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.ToString()).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim())]
                : [];
        }
        catch (JsonException)
        {
            // A bare id, as Overseerr wrote it.
            return [raw.Trim()];
        }
    }

    /// <summary>The <c>discord</c> key of <c>notificationTypes</c> (<c>{"discord": 4, "email": …}</c>); null when absent.</summary>
    private static long? DiscordBits(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("discord", out var discord)
                && discord.ValueKind == JsonValueKind.Number
                && discord.TryGetInt64(out var bits)
                ? bits
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private HashSet<string> Columns(string table)
    {
        if (_columns.TryGetValue(table, out var cached))
        {
            return cached;
        }

        var columns = new HashSet<string>(StringComparer.Ordinal);
        using (var command = _connection.CreateCommand())
        {
            command.CommandText = $"PRAGMA table_info(\"{table}\")";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                columns.Add(reader.GetString(1));
            }
        }

        _columns[table] = columns;
        return columns;
    }

    /// <summary>The column, quoted, or NULL when this database lacks it.</summary>
    private string Col(string table, string column) => Columns(table).Contains(column) ? $"\"{column}\"" : "NULL";

    private static string? Text(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture) is { Length: > 0 } text ? text : null;

    private static long? Long(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        return reader.GetValue(ordinal) switch
        {
            long l => l,
            double d => (long)d,
            string s when long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            string s when bool.TryParse(s, out var flag) => flag ? 1 : 0,
            _ => null,
        };
    }

    private static int? Int(SqliteDataReader reader, int ordinal) => Long(reader, ordinal) is { } value ? (int)value : null;

    /// <summary>TypeORM writes UTC as <c>yyyy-MM-dd HH:mm:ss[.fff]</c>; an ISO string or Unix milliseconds read too.</summary>
    private static DateTime? Date(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        var value = reader.GetValue(ordinal);
        if (value is long ms)
        {
            return DateTime.UnixEpoch.AddMilliseconds(ms);
        }

        return DateTime.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
            ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
            : null;
    }
}

public sealed class SeerrDatabaseException : Exception
{
    public SeerrDatabaseException()
    {
    }

    public SeerrDatabaseException(string message)
        : base(message)
    {
    }

    public SeerrDatabaseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
