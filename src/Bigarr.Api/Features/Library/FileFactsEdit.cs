using System.Globalization;
using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Parsing.Languages;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Library;

/// <summary>
/// Radarr's and Sonarr's "edit file": what the user says a file on disk is, over what the import read in its name. The
/// film and the episode edits share these rules; each keeps its own service because their files and journals differ.
/// Nothing on disk moves: a new release group shows in the name only after a rename.
/// </summary>
public static class FileFactsEdit
{
    /// <summary>The column's length for the release group and the edition.</summary>
    public const int MaxTextLength = 255;

    /// <summary>A text left out stays; a blank one clears the fact. Null means "leave it".</summary>
    public static Failure? Validate(string? releaseGroup, string? edition, IReadOnlyList<string>? languages)
    {
        if (releaseGroup?.Trim().Length > MaxTextLength || edition?.Trim().Length > MaxTextLength)
        {
            return Failure.Invalid("Too long", $"A release group or an edition has at most {MaxTextLength} characters.");
        }

        var unknown = languages?.Where(l => Find(l) is null).ToList() ?? [];
        return unknown.Count > 0
            ? Failure.Invalid("Unknown language", $"{string.Join(", ", unknown)}: not a language Bigarr knows.")
            : null;
    }

    /// <summary>A quality Bigarr has a definition for, other than Unknown (the parser's placeholder).</summary>
    public static async Task<Failure?> ValidateQualityAsync(int? qualityId, BigarrDbContext db, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        return qualityId is { } id && (id == 0 || !await db.QualityDefinitions.AnyAsync(q => q.Id == id, ct))
            ? Failure.Invalid("Unknown quality", $"Quality {id} is not one Bigarr knows.")
            : null;
    }

    /// <summary>The text as stored: trimmed, null when blank.</summary>
    public static string? Text(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>The languages as the parser spells them, once each, in the order given. Empty is allowed: no language known.</summary>
    public static List<string> Languages(IEnumerable<string> names) =>
        names.Select(Find).OfType<Language>().Select(l => l.Name).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>Journals <c>before → after</c> under <paramref name="key"/> when they differ ("—" for none). True when they did.</summary>
    public static bool Record(Dictionary<string, string> changes, string key, string? before, string? after)
    {
        ArgumentNullException.ThrowIfNull(changes);
        if (string.Equals(before, after, StringComparison.Ordinal))
        {
            return false;
        }

        changes[key] = string.Create(CultureInfo.InvariantCulture, $"{before ?? "—"} → {after ?? "—"}");
        return true;
    }

    /// <summary>
    /// The language Bigarr knows by this name, ignoring case. Unknown is one: the scan stores it for a file whose name says
    /// none, so a file keeps it through an edit of its other facts. Any and Original only describe what a profile wants.
    /// </summary>
    public static Language? Find(string name) =>
        Language.All.FirstOrDefault(l => l.Id >= 0 && string.Equals(l.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>The languages as one line for the journal, null for none.</summary>
    public static string? Line(IReadOnlyList<string> languages) => languages.Count == 0 ? null : string.Join(", ", languages);
}
