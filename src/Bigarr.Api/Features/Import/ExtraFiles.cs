using System.Text.RegularExpressions;
using Bigarr.Domain.Library;
using Bigarr.Domain.Movies;
using Bigarr.Parsing;

namespace Bigarr.Api.Features.Import;

/// <summary>What a side file's name says about it. <see cref="Group"/> (the path without extension) ties an <c>.idx</c> to its <c>.sub</c>.</summary>
public sealed record ExtraFileName(string Group, string Extension, ExtraFileKind Kind, string? Language, bool Forced, bool HearingImpaired)
{
    public static ExtraFileName Of(ExtraFile extra)
    {
        ArgumentNullException.ThrowIfNull(extra);
        return new(Path.ChangeExtension(extra.RelativePath, null), extra.Extension, extra.Kind, extra.Language, extra.Forced, extra.HearingImpaired);
    }

    /// <summary>
    /// <c>{movie}[.{copy}][.{language}][.forced][.sdh]{extension}</c>. The copy number comes first so media servers still
    /// find the language and the flags at the end of the name.
    /// </summary>
    public string For(string movieBaseName, int copy)
    {
        var name = movieBaseName;
        if (copy > 0)
        {
            name += "." + copy.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (Kind == ExtraFileKind.Subtitle)
        {
            name += (Language is null ? string.Empty : "." + Language) + (Forced ? ".forced" : string.Empty) + (HearingImpaired ? ".sdh" : string.Empty);
        }

        return name + Extension;
    }
}

/// <summary>A side file of a release that comes along with its video.</summary>
public sealed record ExtraCandidate(FileInfo Source, ExtraFileName Name);

/// <summary>
/// Radarr's "Import Extra Files": which side files of a release come along with its video, what their names say
/// (language, forced, SDH), and what they are called next to the movie file.
/// </summary>
public static partial class ExtraFiles
{
    private static readonly HashSet<string> SubtitleExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".srt", ".ass", ".ssa", ".sub", ".idx", ".sup", ".vtt", ".smi", ".sami", ".ttml", ".dfxp", ".mks",
    };

    private static readonly StringComparer PathComparer = OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    private static readonly EnumerationOptions Recursive = new() { RecurseSubdirectories = true, IgnoreInaccessible = true };

    /// <summary>The media management list as dotted lower-case extensions (<c>.srt</c>). Lenient: entries that are not extensions are skipped.</summary>
    public static IReadOnlySet<string> ParseExtensions(string? list) =>
        Entries(list).Where(e => ExtensionRegex().IsMatch(e) && !VideoFiles.Extensions.Contains("." + e)).Select(e => "." + e).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>The list as stored: <c>srt,ass</c>, lower case, no dots, no duplicates. Null with the reason when an entry is not usable.</summary>
    public static string? Normalize(string list, out string? error)
    {
        ArgumentNullException.ThrowIfNull(list);
        var entries = Entries(list).Distinct(StringComparer.Ordinal).ToList();
        foreach (var entry in entries)
        {
            if (!ExtensionRegex().IsMatch(entry))
            {
                error = $"'{entry}' is not a file extension: letters and digits only, like srt or ass.";
                return null;
            }

            if (VideoFiles.Extensions.Contains("." + entry))
            {
                error = $"{entry} is a video extension: the movie file is never one of its own extras.";
                return null;
            }
        }

        error = null;
        return string.Join(',', entries);
    }

    /// <summary>
    /// The side files to bring with <paramref name="video"/>. A release folder holding this one film gives everything
    /// under it with a listed extension, samples aside (<c>Subs/2_English.srt</c> included). A single-file release, or a
    /// folder shared with other films, gives only the siblings named after the video (<c>Movie.en.srt</c> for <c>Movie.mkv</c>).
    /// </summary>
    /// <param name="releasePath">What the download client reported, folder or file. Null when unknown (manual import): the video's own folder is judged.</param>
    public static List<ExtraCandidate> Find(string video, string? releasePath, IReadOnlySet<string> extensions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(video);
        ArgumentNullException.ThrowIfNull(extensions);

        var directory = Path.GetDirectoryName(Path.GetFullPath(video));
        if (extensions.Count == 0 || directory is null || !Directory.Exists(directory))
        {
            return [];
        }

        var baseName = Path.GetFileNameWithoutExtension(video);
        var folder = releasePath is null ? directory : Directory.Exists(releasePath) ? Path.GetFullPath(releasePath) : null;
        var files = folder is not null && HoldsOneFilm(folder, video)
            ? new DirectoryInfo(folder).EnumerateFiles("*", Recursive).Where(f => !InSample(Path.GetRelativePath(folder, f.FullName)))
            : new DirectoryInfo(directory).EnumerateFiles().Where(f => SharesBaseName(f.Name, baseName));

        return files
            .Where(f => extensions.Contains(f.Extension))
            .OrderBy(f => f.FullName, StringComparer.Ordinal)
            .Select(f => new ExtraCandidate(f, Describe(f.FullName, baseName)))
            .ToList();
    }

    /// <summary>
    /// What the name says, read like Radarr does: <c>.en</c>, <c>.eng</c>, <c>English</c>, <c>2_English</c>, then
    /// <c>.forced</c> / <c>.sdh</c> tags. A subtitle named exactly like its video says nothing: a title such as "It" is no language.
    /// </summary>
    public static ExtraFileName Describe(string path, string videoBaseName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var group = Path.ChangeExtension(path, null);
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (!SubtitleExtensions.Contains(extension))
        {
            return new ExtraFileName(group, extension, ExtraFileKind.Other, null, false, false);
        }

        if (string.Equals(Path.GetFileNameWithoutExtension(path), videoBaseName, StringComparison.OrdinalIgnoreCase))
        {
            return new ExtraFileName(group, extension, ExtraFileKind.Subtitle, null, false, false);
        }

        var language = LanguageParser.ParseSubtitleLanguage(path);
        var code = language.Id > 0 ? IsoLanguages.Get(language)?.TwoLetterCode : null;
        var tags = LanguageParser.ParseLanguageTags(path);
        return new ExtraFileName(
            group,
            extension,
            ExtraFileKind.Subtitle,
            code,
            tags.Contains("forced") || tags.Contains("foreign"),
            tags.Contains("sdh") || tags.Contains("cc") || tags.Contains("psdh"));
    }

    /// <summary>
    /// Destination paths next to <paramref name="movieBaseName"/> in <paramref name="directory"/>, numbered <c>.1</c>,
    /// <c>.2</c> when a name is <paramref name="taken"/> or already given to a previous one. Files of one group share a
    /// number so an <c>.idx</c> and its <c>.sub</c> stay a pair.
    /// </summary>
    public static List<string> Destinations(IReadOnlyList<ExtraFileName> names, string directory, string movieBaseName, Func<string, bool> taken)
    {
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(taken);

        var result = new string[names.Count];
        var given = new HashSet<string>(PathComparer);
        foreach (var group in names.Select((name, index) => (Name: name, Index: index)).GroupBy(n => n.Name.Group, PathComparer))
        {
            for (var copy = 0; ; copy++)
            {
                if (copy > 999)
                {
                    throw new IOException($"No free name left for the extras of {movieBaseName} in {directory}.");
                }

                var paths = group.Select(n => Path.Combine(directory, n.Name.For(movieBaseName, copy))).ToList();
                if (paths.Any(p => given.Contains(p) || taken(p)))
                {
                    continue;
                }

                foreach (var (entry, path) in group.Zip(paths))
                {
                    result[entry.Index] = path;
                    given.Add(path);
                }

                break;
            }
        }

        return [.. result];
    }

    /// <summary>One video that is not a sample under the folder: the release is this film alone, everything in it goes with it.</summary>
    private static bool HoldsOneFilm(string folder, string video)
    {
        var films = new DirectoryInfo(folder).EnumerateFiles("*", Recursive)
            .Where(f => VideoFiles.IsVideo(f.Name) && !InSample(Path.GetRelativePath(folder, f.FullName)))
            .Take(2)
            .ToList();
        return films.Count == 1 && PathComparer.Equals(films[0].FullName, Path.GetFullPath(video));
    }

    /// <summary><c>Sample/x.srt</c>, <c>movie-sample.srt</c>: what belongs to the sample stays with it.</summary>
    private static bool InSample(string relativePath) =>
        // LooksLikeSample drops what it takes for an extension: give it one so the whole segment is judged.
        relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(segment => VideoFiles.LooksLikeSample(segment + ".x"));

    private static bool SharesBaseName(string fileName, string baseName) =>
        string.Equals(Path.GetFileNameWithoutExtension(fileName), baseName, StringComparison.OrdinalIgnoreCase)
        || (fileName.Length > baseName.Length + 1
            && fileName.StartsWith(baseName, StringComparison.OrdinalIgnoreCase)
            && fileName[baseName.Length] is '.' or '_' or '-' or ' ');

    private static IEnumerable<string> Entries(string? list) =>
        (list ?? string.Empty)
            .Split([',', ';', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e => e.TrimStart('*').TrimStart('.').ToLowerInvariant())
            .Where(e => e.Length > 0);

    [GeneratedRegex("^[a-z0-9]{1,16}$", RegexOptions.CultureInvariant)]
    private static partial Regex ExtensionRegex();
}
