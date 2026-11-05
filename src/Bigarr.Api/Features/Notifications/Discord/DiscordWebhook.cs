using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bigarr.Api.Infrastructure.Events;
using Bigarr.Domain.Events;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Notifications;

namespace Bigarr.Api.Features.Notifications.Discord;

/// <summary>
/// Bigarr events as Radarr's Discord connection posts them (<c>NzbDrone.Core/Notifications/Discord</c>): same embeds,
/// colors and fields, chosen by the endpoint's <see cref="DiscordSettings"/>, plus the version the event is about, the
/// failures Radarr keeps to itself (colored as Lidarr colors them) and the versions added, removed or made the
/// default, which Radarr cannot have. Built from the event's snapshot only, like the other formats. Every text is cut
/// to Discord's limits: a long overview never gets a message refused.
/// </summary>
public static class DiscordWebhook
{
    public const string InstanceName = "Bigarr";

    // Discord's limits ("Embed Limits" in its API documentation).
    public const int ContentLimit = 2000;
    public const int UsernameLimit = 80;
    public const int EmbedLimit = 10;
    public const int TitleLimit = 256;
    public const int DescriptionLimit = 4096;
    public const int AuthorLimit = 256;
    public const int FieldCountLimit = 25;
    public const int FieldNameLimit = 256;
    public const int FieldValueLimit = 1024;

    /// <summary>Titles, descriptions, author names and fields of all the embeds of a message, together.</summary>
    public const int TotalLimit = 6000;

    /// <summary>Radarr cuts the overview of a grab or an import at 300 characters.</summary>
    public const int OverviewLimit = 300;

    private const string Ellipsis = "...";
    private const string Fence = "```";

    /// <summary>A text shortened to fit the total keeps at least this much.</summary>
    private const int ShortestText = 16;

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    /// <summary>The body of one delivery, or null when the Discord format posts nothing for it.</summary>
    public static string? Body(string eventType, string payloadJson, DiscordSettings? settings)
    {
        var domainEvent = EventJson.Deserialize(eventType, payloadJson);
        var payload = domainEvent is null ? null : Payload(domainEvent, settings);
        return payload is null ? null : Serialize(payload);
    }

    public static string Serialize(DiscordPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return JsonSerializer.Serialize(payload, Json);
    }

    /// <summary>The message for one event, cut to Discord's limits, or null when the Discord format posts nothing for it.</summary>
    public static DiscordPayload? Payload(DomainEvent domainEvent, DiscordSettings? settings)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        var s = (settings ?? DiscordSettings.Default).Normalized();
        var at = domainEvent.OccurredAt.ToString("O", CultureInfo.InvariantCulture);
        var embed = domainEvent switch
        {
            ReleaseGrabbed e => Grab(s, e.Movie, e.Version, e.Release, "Movie Grabbed", DiscordColor.Standard, at),
            FileImported e => Import(s, e.Movie, e.Version, e.File, e.Release, upgrade: false, at),
            FileUpgraded e => Import(s, e.Movie, e.Version, e.File, e.Release, upgrade: true, at),
            MovieAdded e => Film(s, e.Movie, "Movie Added", DiscordColor.Success, at),

            // Radarr's MovieDeleteMessage wording.
            MovieDeleted e => Film(s, e.Movie, e.DeletedFiles ? "Movie removed and all files were deleted" : "Movie removed, files were not deleted", DiscordColor.Danger, at),
            FileDeleted e => Embed(s, e.Movie, null, "Movie File Deleted", DiscordColor.Danger, at, null, null, Fields(
                ("Version", e.Version.Name, true),
                ("Reason", e.Reason == "recycled" ? "Moved to the recycle bin" : "Deleted", true),
                ("File name", Code(e.File.Path), false))),

            // Radarr's rename embed: no author, no color, the paths relative to the film's folder.
            FileRenamed e => new DiscordEmbed(Title(e.Movie), $"{Relative(e.Version, e.PreviousPath)} renamed to {e.File.RelativePath ?? Relative(e.Version, e.File.Path)}", null, null, null, null, null, null, null),

            // Bigarr's own: Radarr has one version per film.
            VersionAdded e => VersionChange(s, e.Movie, e.Version, "Version Added", DiscordColor.Success, at),
            VersionDeleted e => VersionChange(s, e.Movie, e.Version, e.DeletedFiles ? "Version removed and its files were deleted" : "Version removed, files were not deleted", DiscordColor.Danger, at),
            DefaultVersionChanged e => Embed(s, e.Movie, null, "Default Version Changed", DiscordColor.Standard, at, Poster(s, e.Movie), null, Fields(
                ("Default Version", e.NewDefault.Name, true),
                ("Previous Default", e.PreviousDefault?.Name, true))),
            HealthIssueRaised e => Embed(s, null, e.Source, e.Message, e.IssueType == HealthIssueType.Error ? DiscordColor.Danger : DiscordColor.Warning, at, null, null, null),
            HealthIssueResolved e => Embed(s, null, $"Health Issue Resolved: {e.Source}", $"The following issue is now resolved: {e.Message}", DiscordColor.Success, at, null, null, null),
            ImportFailed e => ImportFailure(s, e, at),
            DownloadFailed e => Grab(s, e.Movie, e.Version, e.Release, "Download Failed", DiscordColor.Danger, at, Reason(e)),
            TestEvent e => Embed(s, null, "Test message from Bigarr", e.Message, DiscordColor.Standard, at, null, null, null),
            _ => null,
        };

        return embed is null ? null : Fit(new DiscordPayload(domainEvent is FileRenamed ? "Renamed" : null, s.Username, s.Avatar, [embed]));
    }

    /// <summary>What the test button posts: the test embed, under the endpoint's name and avatar.</summary>
    public static DiscordPayload Test(string message, DateTime occurredAt, DiscordSettings? settings) =>
        Payload(new TestEvent(message) { OccurredAt = occurredAt }, settings)!;

    /// <summary>Radarr's <c>GetTitle</c>: the title and the year, backticks escaped, cut to the title limit.</summary>
    public static string Title(MovieRef movie)
    {
        ArgumentNullException.ThrowIfNull(movie);
        var title = movie.Year > 0 ? $"{movie.Title} ({movie.Year})" : movie.Title;
        return Cut(title.Replace("`", "\\`", StringComparison.Ordinal), TitleLimit)!;
    }

    /// <summary>Radarr's <c>BytesToString</c>: binary units, one decimal.</summary>
    public static string Bytes(long byteCount)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB", "PB", "EB"];
        if (byteCount == 0)
        {
            return "0 B";
        }

        var bytes = Math.Abs(byteCount);
        var place = Convert.ToInt32(Math.Floor(Math.Log(bytes, 1024)));
        var number = Math.Round(bytes / Math.Pow(1024, place), 1);
        return $"{(Math.Sign(byteCount) * number).ToString(CultureInfo.InvariantCulture)} {suffixes[place]}";
    }

    /// <summary>
    /// Discord's limits, applied without refusing anything: each text cut to its own limit, at most 25 fields, then
    /// the longest descriptions and field values shortened until the embeds fit the 6000 characters of a message.
    /// </summary>
    public static DiscordPayload Fit(DiscordPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var embeds = payload.Embeds?.Take(EmbedLimit).Select(e => e with
        {
            Title = Cut(e.Title, TitleLimit),
            Description = Cut(e.Description, DescriptionLimit),
            Author = e.Author is null ? null : e.Author with { Name = Cut(e.Author.Name, AuthorLimit)! },
            Fields = e.Fields?.Take(FieldCountLimit).Select(f => f with { Name = Cut(f.Name, FieldNameLimit)!, Value = CutValue(f.Value, FieldValueLimit) }).ToList(),
        }).ToList();

        if (embeds is not null)
        {
            Shorten(embeds);
        }

        return payload with { Content = Cut(payload.Content, ContentLimit), Username = Cut(payload.Username, UsernameLimit), Embeds = embeds };
    }

    /// <summary>Characters an embed counts toward <see cref="TotalLimit"/>.</summary>
    public static int Length(DiscordEmbed embed)
    {
        ArgumentNullException.ThrowIfNull(embed);
        return (embed.Title?.Length ?? 0) + (embed.Description?.Length ?? 0) + (embed.Author?.Name.Length ?? 0) + (embed.Fields ?? []).Sum(f => f.Name.Length + f.Value.Length);
    }

    /// <summary>
    /// Discord's error answer, <c>{"message": "...", "code": 50006}</c>, as a failure the user can act on:
    /// <c>Discord: Cannot send an empty message (50006)</c>, with the first field error of an invalid form. Null when
    /// the body is not Discord's (a proxy's error page).
    /// </summary>
    public static string? Error(string? body)
    {
        using var doc = Parse(body);
        if (doc?.RootElement is not { ValueKind: JsonValueKind.Object } root || !root.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = $"Discord: {message.GetString()}";
        if (root.TryGetProperty("code", out var code) && code.TryGetInt32(out var number) && number != 0)
        {
            text += $" ({number})";
        }

        if (root.TryGetProperty("errors", out var errors) && FirstError(errors, string.Empty) is { } detail)
        {
            text += $": {detail}";
        }

        return Cut(text, 500);
    }

    /// <summary>The <c>retry_after</c> of Discord's 429 answer: seconds, with a fraction.</summary>
    public static TimeSpan? RetryAfter(string? body)
    {
        using var doc = Parse(body);
        return doc?.RootElement is { ValueKind: JsonValueKind.Object } root && root.TryGetProperty("retry_after", out var value) && value.TryGetDouble(out var seconds) && seconds >= 0
            ? TimeSpan.FromSeconds(seconds)
            : null;
    }

    private static DiscordEmbed Grab(DiscordSettings s, MovieRef? movie, VersionRef? version, ReleaseRef release, string description, DiscordColor color, string at, string? reason = null)
    {
        var chosen = s.GrabFields ?? DiscordSettings.DefaultGrabFields;
        var fields = new FieldList<DiscordGrabField>(chosen)
            .Add("Reason", reason)
            .Add(DiscordGrabField.Overview, "Overview", Overview(movie))
            .Add(DiscordGrabField.Rating, "Rating", Rating(movie))
            .Add(DiscordGrabField.Genres, "Genres", Genres(movie))
            .Add(DiscordGrabField.Version, "Version", version?.Name, inline: true)
            .Add(DiscordGrabField.Quality, "Quality", release.Quality, inline: true)
            .Add(DiscordGrabField.Group, "Group", release.ReleaseGroup)
            .Add(DiscordGrabField.Size, "Size", release.Size > 0 ? Bytes(release.Size) : null, inline: true)
            .Add(DiscordGrabField.Links, "Links", Links(movie))
            .Add(DiscordGrabField.Release, "Release", Code(release.Title))
            .Add(DiscordGrabField.Indexer, "Indexer", release.Indexer)
            .Add(DiscordGrabField.CustomFormats, "Custom Formats", Formats(release.CustomFormats))
            .Add(DiscordGrabField.CustomFormatScore, "Custom Format Score", release.CustomFormatScore?.ToString(CultureInfo.InvariantCulture))
            .Add(DiscordGrabField.Tags, "Tags", Tags(version))
            .Fields;
        return Embed(s, movie, release.Title, description, color, at, Image(movie, ImageType.Poster, chosen.Contains(DiscordGrabField.Poster)), Image(movie, ImageType.Fanart, chosen.Contains(DiscordGrabField.Fanart)), fields);
    }

    private static DiscordEmbed Import(DiscordSettings s, MovieRef movie, VersionRef version, FileRef file, ReleaseRef? release, bool upgrade, string at)
    {
        var chosen = s.ImportFields ?? DiscordSettings.DefaultImportFields;
        var fields = new FieldList<DiscordImportField>(chosen)
            .Add(DiscordImportField.Overview, "Overview", Overview(movie))
            .Add(DiscordImportField.Rating, "Rating", Rating(movie))
            .Add(DiscordImportField.Genres, "Genres", Genres(movie))
            .Add(DiscordImportField.Version, "Version", version.Name, inline: true)
            .Add(DiscordImportField.Quality, "Quality", file.Quality, inline: true)
            .Add(DiscordImportField.Codecs, "Codecs", Codecs(file.MediaInfo), inline: true)
            .Add(DiscordImportField.Group, "Group", file.ReleaseGroup)
            .Add(DiscordImportField.Size, "Size", Bytes(file.Size), inline: true)
            .Add(DiscordImportField.Languages, "Languages", Streams(file.MediaInfo?.AudioLanguages))
            .Add(DiscordImportField.Subtitles, "Subtitles", Streams(file.MediaInfo?.Subtitles))
            .Add(DiscordImportField.Links, "Links", Links(movie))
            .Add(DiscordImportField.Release, "Release", Code(file.SceneName ?? release?.Title))
            .Add(DiscordImportField.Tags, "Tags", Tags(version))
            .Add(DiscordImportField.CustomFormats, "Custom Formats", Formats(file.CustomFormats))
            .Add(DiscordImportField.CustomFormatScore, "Custom Format Score", file.CustomFormatScore?.ToString(CultureInfo.InvariantCulture))
            .Fields;
        return Embed(
            s,
            movie,
            null,
            upgrade ? "Movie Upgraded" : "Movie Imported",
            upgrade ? DiscordColor.Upgrade : DiscordColor.Success,
            at,
            Image(movie, ImageType.Poster, chosen.Contains(DiscordImportField.Poster)),
            Image(movie, ImageType.Fanart, chosen.Contains(DiscordImportField.Fanart)),
            fields);
    }

    /// <summary>Radarr's "manual interaction required", which is what a failed import asks for; Lidarr's warning color.</summary>
    private static DiscordEmbed ImportFailure(DiscordSettings s, ImportFailed e, string at)
    {
        var chosen = s.ManualInteractionFields ?? DiscordSettings.DefaultManualInteractionFields;
        var fields = new FieldList<DiscordManualInteractionField>(chosen)
            .Add("Reason", e.Reason)
            .Add(DiscordManualInteractionField.Overview, "Overview", Overview(e.Movie))
            .Add(DiscordManualInteractionField.Rating, "Rating", Rating(e.Movie))
            .Add(DiscordManualInteractionField.Genres, "Genres", Genres(e.Movie))
            .Add(DiscordManualInteractionField.Version, "Version", e.Version?.Name, inline: true)
            .Add(DiscordManualInteractionField.Quality, "Quality", e.Release.Quality, inline: true)
            .Add(DiscordManualInteractionField.Group, "Group", e.Release.ReleaseGroup)
            .Add(DiscordManualInteractionField.Size, "Size", e.Release.Size > 0 ? Bytes(e.Release.Size) : null, inline: true)
            .Add(DiscordManualInteractionField.Links, "Links", Links(e.Movie))
            .Add(DiscordManualInteractionField.DownloadTitle, "Download", Code(e.Release.Title))
            .Add(DiscordManualInteractionField.Tags, "Tags", Tags(e.Version))
            .Fields;
        return Embed(
            s,
            e.Movie,
            e.Release.Title,
            "Import Failed",
            DiscordColor.Warning,
            at,
            Image(e.Movie, ImageType.Poster, chosen.Contains(DiscordManualInteractionField.Poster)),
            Image(e.Movie, ImageType.Fanart, chosen.Contains(DiscordManualInteractionField.Fanart)),
            fields);
    }

    /// <summary>Added and deleted films: the links, and the poster and fanart when the import fields have them, like Radarr.</summary>
    private static DiscordEmbed Film(DiscordSettings s, MovieRef movie, string description, DiscordColor color, string at)
    {
        var chosen = s.ImportFields ?? DiscordSettings.DefaultImportFields;
        return Embed(
            s,
            movie,
            null,
            description,
            color,
            at,
            Image(movie, ImageType.Poster, chosen.Contains(DiscordImportField.Poster)),
            Image(movie, ImageType.Fanart, chosen.Contains(DiscordImportField.Fanart)),
            Fields(("Links", Links(movie), false)));
    }

    /// <summary>A version added to or removed from a film: which one, its profile and tags, the size it took on disk.</summary>
    private static DiscordEmbed VersionChange(DiscordSettings s, MovieRef movie, VersionRef version, string description, DiscordColor color, string at) =>
        Embed(s, movie, null, description, color, at, Poster(s, movie), null, Fields(
            ("Version", version.Name, true),
            ("Quality Profile", version.QualityProfile, true),
            ("Size", version.SizeOnDisk is long size and > 0 ? Bytes(size) : null, true),
            ("Tags", Tags(version), false)));

    /// <summary>The poster as a thumbnail, when the import fields have it, as for an added film.</summary>
    private static DiscordImage? Poster(DiscordSettings s, MovieRef movie) =>
        Image(movie, ImageType.Poster, (s.ImportFields ?? DiscordSettings.DefaultImportFields).Contains(DiscordImportField.Poster));

    /// <param name="title">The title when the event is about no film: a release, a health check.</param>
    private static DiscordEmbed Embed(DiscordSettings s, MovieRef? movie, string? title, string description, DiscordColor color, string at, DiscordImage? thumbnail, DiscordImage? image, List<DiscordField>? fields) => new(
        movie is null ? title : Title(movie),
        description,
        movie is { TmdbId: > 0 } ? $"https://www.themoviedb.org/movie/{movie.TmdbId}" : null,
        (int)color,
        new DiscordAuthor(s.Author ?? InstanceName),
        thumbnail,
        image,
        at,
        fields);

    /// <summary>Why the download failed, and what Bigarr did about it.</summary>
    private static string Reason(DownloadFailed e)
    {
        var followUp = (e.Blocklisted, e.SearchRequested) switch
        {
            (true, true) => "Blocklisted, searching for another release.",
            (true, false) => "Blocklisted.",
            (false, true) => "Searching for another release.",
            _ => null,
        };
        return followUp is null ? e.Reason : $"{e.Reason}\n{followUp}";
    }

    private static string? Overview(MovieRef? movie) => Cut(movie?.Overview, OverviewLimit + Ellipsis.Length);

    private static string? Rating(MovieRef? movie) =>
        movie?.Ratings?.Tmdb is { Value: > 0 } rating ? rating.Value.ToString("0.0", CultureInfo.InvariantCulture) : null;

    private static string? Genres(MovieRef? movie) => movie?.Genres is { Count: > 0 } genres ? string.Join(", ", genres.Take(5)) : null;

    private static string? Tags(VersionRef? version) =>
        version?.Tags is { Count: > 0 } tags ? string.Join(", ", tags.Select(t => t.Label).Where(l => !string.IsNullOrWhiteSpace(l)).Order(StringComparer.Ordinal).Take(5)) : null;

    private static string? Formats(List<string>? names) => names is { Count: > 0 } ? string.Join("|", names) : null;

    private static string? Streams(List<string>? languages) => languages is { Count: > 0 } ? string.Join("/", languages.Distinct(StringComparer.Ordinal)) : null;

    /// <summary>Radarr's codecs field: <c>x265 / EAC3 5.1</c>.</summary>
    private static string? Codecs(MediaInfoRef? info)
    {
        if (info is null || (info.VideoCodec is null && info.AudioCodec is null))
        {
            return null;
        }

        var channels = info.AudioChannels is { } count ? count.ToString("0.0", CultureInfo.InvariantCulture) : null;
        return $"{info.VideoCodec} / {string.Join(' ', new[] { info.AudioCodec, channels }.Where(p => !string.IsNullOrEmpty(p)))}".Trim();
    }

    /// <summary>Radarr's links: TMDb always, Trakt and IMDb when the IMDb id is known.</summary>
    private static string? Links(MovieRef? movie)
    {
        if (movie is null)
        {
            return null;
        }

        List<string> links = [$"[TMDb](https://themoviedb.org/movie/{movie.TmdbId})"];
        if (!string.IsNullOrWhiteSpace(movie.ImdbId))
        {
            links.Add($"[Trakt](https://trakt.tv/movies/{movie.ImdbId})");
            links.Add($"[IMDb](https://imdb.com/title/{movie.ImdbId}/)");
        }

        return string.Join(" / ", links);
    }

    /// <summary>The film's TMDB image of that type, when chosen; Discord only fetches absolute http(s) URLs.</summary>
    private static DiscordImage? Image(MovieRef? movie, ImageType type, bool chosen) =>
        chosen
        && movie?.Images?.FirstOrDefault(i => i.CoverType == type)?.RemoteUrl is { } url
        && Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? new DiscordImage(url)
            : null;

    private static string Relative(VersionRef version, string path) => version.Folder is null ? Path.GetFileName(path) : Path.GetRelativePath(version.Folder, path);

    /// <summary>A code block, as Radarr shows release names and paths; its content is cut so the fences survive.</summary>
    private static string? Code(string? text) => string.IsNullOrWhiteSpace(text) ? null : $"{Fence}{Cut(text, FieldValueLimit - (2 * Fence.Length))}{Fence}";

    /// <summary>Fixed fields, the empty ones left out like Radarr does.</summary>
    private static List<DiscordField> Fields(params (string Name, string? Value, bool Inline)[] fields) =>
        fields.Where(f => !string.IsNullOrWhiteSpace(f.Value)).Select(f => new DiscordField(f.Name, f.Value!, f.Inline)).ToList();

    /// <summary>At most <paramref name="max"/> characters, the cut marked with an ellipsis, never through a surrogate pair.</summary>
    private static string? Cut(string? text, int max)
    {
        if (text is null || text.Length <= max)
        {
            return text;
        }

        if (max <= Ellipsis.Length)
        {
            return text[..max];
        }

        var keep = max - Ellipsis.Length;
        if (char.IsHighSurrogate(text[keep - 1]))
        {
            keep--;
        }

        // A dangling backslash would escape the ellipsis, as Radarr's GetTitle avoids.
        return $"{text.AsSpan(0, keep).TrimEnd('\\')}{Ellipsis}";
    }

    /// <summary>Like <see cref="Cut"/>, but a code block is cut inside its fences.</summary>
    private static string CutValue(string value, int max)
    {
        if (value.Length <= max)
        {
            return value;
        }

        var fenced = value.Length > 2 * Fence.Length && value.StartsWith(Fence, StringComparison.Ordinal) && value.EndsWith(Fence, StringComparison.Ordinal);
        return fenced && max > (2 * Fence.Length) + Ellipsis.Length
            ? $"{Fence}{Cut(value[Fence.Length..^Fence.Length], max - (2 * Fence.Length))}{Fence}"
            : Cut(value, max)!;
    }

    /// <summary>The longest description or field value gives way first; as a last resort, fields go from the end.</summary>
    private static void Shorten(List<DiscordEmbed> embeds)
    {
        var excess = embeds.Sum(Length) - TotalLimit;
        while (excess > 0)
        {
            var longest = embeds
                .SelectMany((e, i) => (e.Fields ?? []).Select((f, j) => (Embed: i, Field: j, f.Value.Length)).Prepend((Embed: i, Field: -1, Length: e.Description?.Length ?? 0)))
                .MaxBy(p => p.Length);
            if (longest.Length <= ShortestText)
            {
                break;
            }

            var target = Math.Max(ShortestText, longest.Length - excess);
            var embed = embeds[longest.Embed];
            embeds[longest.Embed] = longest.Field < 0
                ? embed with { Description = Cut(embed.Description, target) }
                : embed with { Fields = embed.Fields!.Select((f, j) => j == longest.Field ? f with { Value = CutValue(f.Value, target) } : f).ToList() };
            excess = embeds.Sum(Length) - TotalLimit;
        }

        for (var i = embeds.Count - 1; i >= 0 && excess > 0; i--)
        {
            while (excess > 0 && embeds[i].Fields is { Count: > 0 } fields)
            {
                excess -= fields[^1].Name.Length + fields[^1].Value.Length;
                embeds[i] = embeds[i] with { Fields = fields.Take(fields.Count - 1).ToList() };
            }
        }
    }

    /// <summary>Discord nests form errors by path (<c>embeds.0.title._errors[0].message</c>): the first one, with its path.</summary>
    private static string? FirstError(JsonElement node, string path)
    {
        if (node.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (node.TryGetProperty("_errors", out var list) && list.ValueKind == JsonValueKind.Array && list.GetArrayLength() > 0
            && list[0].ValueKind == JsonValueKind.Object && list[0].TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
        {
            return path.Length == 0 ? message.GetString() : $"{path}: {message.GetString()}";
        }

        foreach (var child in node.EnumerateObject())
        {
            if (FirstError(child.Value, path.Length == 0 ? child.Name : $"{path}.{child.Name}") is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static JsonDocument? Parse(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The fields of one embed in Radarr's order: those the settings leave out and the empty ones are skipped, as Radarr does.</summary>
    private sealed class FieldList<T>(IReadOnlyCollection<T> chosen)
        where T : struct, Enum
    {
        public List<DiscordField> Fields { get; } = [];

        public FieldList<T> Add(T field, string name, string? value, bool inline = false) => chosen.Contains(field) ? Add(name, value, inline) : this;

        /// <summary>A field every message of the kind carries, whatever the settings.</summary>
        public FieldList<T> Add(string name, string? value, bool inline = false)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                Fields.Add(new DiscordField(name, value, inline));
            }

            return this;
        }
    }
}
