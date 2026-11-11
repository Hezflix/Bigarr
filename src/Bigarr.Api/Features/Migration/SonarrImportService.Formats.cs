using System.Text.Json;
using Bigarr.Decision.CustomFormats;
using Bigarr.Domain.Quality;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Migration;

/// <summary>What happened (or would happen, in the preview) to one Sonarr custom format.</summary>
/// <param name="Outcome"><c>new</c> (created), <c>reused</c> (same name and definition in Bigarr), <c>renamed</c> (same name,
/// another definition: imported as "name (Sonarr)"), <c>updated</c> (a format this Sonarr brought in, changed in Sonarr).</param>
/// <param name="Notes">Specifications translated approximately or not evaluated by Bigarr.</param>
public sealed record SonarrFormatPreview(int SonarrId, string SonarrName, string Outcome, string BigarrName, List<string> Notes);

/// <summary>The formats after a sync: Sonarr id → Bigarr id, the Bigarr formats this Sonarr owns, and what happened to each.</summary>
public sealed record SonarrFormatSync(Dictionary<int, int> Map, List<int> Owned, List<SonarrFormatPreview> Items)
{
    public int Changed => Items.Count(i => i.Outcome is "new" or "renamed" or "updated");
}

public sealed partial class SonarrImportService
{
    public const string SonarrSuffix = " (Sonarr)";

    /// <summary>
    /// Sonarr's custom formats into Bigarr's one shared library. A format already in Bigarr under the same name with the same
    /// definition (specifications compared once translated, labels aside) is reused; the same name with another definition
    /// is imported as "name (Sonarr)"; any other is created. Following, a format this Sonarr owns (it created it, or its
    /// "(Sonarr)" copy) follows Sonarr's definition and name, unless a Films or Both profile scores it: then Sonarr's change
    /// goes to a "(Sonarr)" copy, so the Radarr and Sonarr follows never rewrite each other's formats. A reused format is
    /// never rewritten. <paramref name="dryRun"/> computes the outcomes for the preview and writes nothing.
    /// </summary>
    public async Task<SonarrFormatSync> SyncFormatsAsync(
        JsonElement sonarrFormats,
        IReadOnlyDictionary<int, int>? map,
        IReadOnlyCollection<int>? owned,
        bool dryRun,
        SonarrImportProgress? progress,
        CancellationToken ct)
    {
        var translated = new List<SonarrCustomFormats.Translated>();
        if (sonarrFormats.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in sonarrFormats.EnumerateArray())
            {
                try
                {
                    translated.Add(SonarrCustomFormats.Translate(element));
                }
                catch (CustomFormatParseException ex)
                {
                    progress?.Issue(element.TryGetProperty("name", out var n) ? n.GetString() ?? "?" : "?", null, $"Custom format not imported: {ex.Message}");
                }
            }
        }

        var formats = await db.CustomFormats.ToListAsync(ct);
        var byId = formats.ToDictionary(f => f.Id);
        var keys = formats.ToDictionary<CustomFormat, CustomFormat, string>(f => f, KeyOfStored, ReferenceEqualityComparer.Instance);
        // A format a Films or Both profile scores belongs to films too: a Sonarr change never rewrites it.
        var filmUse = (await db.QualityProfiles.AsNoTracking()
                .Where(p => p.Kind != ProfileKind.Series)
                .SelectMany(p => p.FormatScores.Where(s => s.Score != 0).Select(s => s.CustomFormatId))
                .ToListAsync(ct))
            .ToHashSet();
        var ownedSet = new HashSet<int>(owned ?? []);
        var ownedNew = new HashSet<CustomFormat>(ReferenceEqualityComparer.Instance);
        var results = new List<(SonarrCustomFormats.Translated Format, CustomFormat Target, string Outcome)>();

        CustomFormat Create(string name, SonarrCustomFormats.Translated t)
        {
            var created = new CustomFormat { Name = name, SpecificationsJson = string.Empty };
            Write(created, t);
            formats.Add(created);
            keys[created] = t.Key;
            ownedNew.Add(created);
            if (!dryRun)
            {
                db.CustomFormats.Add(created);
            }

            return created;
        }

        bool IsOwned(CustomFormat format) => ownedNew.Contains(format) || (format.Id > 0 && ownedSet.Contains(format.Id));

        CustomFormat? Named(string name) => formats.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));

        // "name (Sonarr)", then "name (Sonarr 2)"…: reused when alike, rewritten when this Sonarr owns it and films do not use it.
        (CustomFormat Format, string Outcome) Copy(SonarrCustomFormats.Translated t)
        {
            var stem = t.Definition.Name.Length > MaxFormatName - 12 ? t.Definition.Name[..(MaxFormatName - 12)] : t.Definition.Name;
            for (var n = 1; ; n++)
            {
                var name = n == 1 ? stem + SonarrSuffix : $"{stem} (Sonarr {n})";
                if (Named(name) is not { } existing)
                {
                    return (Create(name, t), "renamed");
                }

                if (keys[existing] == t.Key)
                {
                    ownedNew.Add(existing);
                    return (existing, "renamed");
                }

                if (IsOwned(existing) && !filmUse.Contains(existing.Id))
                {
                    Write(existing, t);
                    keys[existing] = t.Key;
                    return (existing, "updated");
                }
            }
        }

        foreach (var t in translated)
        {
            CustomFormat target;
            string outcome;
            if (map is not null && map.TryGetValue(t.SonarrId, out var mappedId) && byId.TryGetValue(mappedId, out var mapped))
            {
                if (keys[mapped] == t.Key)
                {
                    target = mapped;
                    outcome = "reused";
                    // Renamed in Sonarr: a format this Sonarr owns takes the new name when it is free.
                    if (IsOwned(mapped) && !string.Equals(mapped.Name, t.Definition.Name, StringComparison.Ordinal)
                        && !mapped.Name.StartsWith(t.Definition.Name + " (Sonarr", StringComparison.Ordinal) && Named(t.Definition.Name) is null)
                    {
                        mapped.Name = t.Definition.Name;
                        outcome = "updated";
                    }
                }
                else if (IsOwned(mapped) && !filmUse.Contains(mapped.Id))
                {
                    if (!string.Equals(mapped.Name, t.Definition.Name, StringComparison.Ordinal) && !mapped.Name.StartsWith(t.Definition.Name + " (Sonarr", StringComparison.Ordinal) && Named(t.Definition.Name) is null)
                    {
                        mapped.Name = t.Definition.Name;
                    }

                    Write(mapped, t);
                    keys[mapped] = t.Key;
                    target = mapped;
                    outcome = "updated";
                }
                else
                {
                    (target, outcome) = Copy(t);
                }
            }
            else if (Named(t.Definition.Name) is not { } same)
            {
                target = Create(t.Definition.Name.Length > MaxFormatName ? t.Definition.Name[..MaxFormatName] : t.Definition.Name, t);
                outcome = "new";
            }
            else if (keys[same] == t.Key)
            {
                target = same;
                outcome = "reused";
            }
            else
            {
                (target, outcome) = Copy(t);
            }

            results.Add((t, target, outcome));
        }

        if (dryRun)
        {
            db.ChangeTracker.Clear();
            return new SonarrFormatSync([], [], results.Select(r => new SonarrFormatPreview(r.Format.SonarrId, r.Format.Definition.Name, r.Outcome, r.Target.Name, r.Format.Notes)).ToList());
        }

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        foreach (var (t, target, outcome) in results)
        {
            progress?.CustomFormat(outcome);
            if (t.Notes.Count > 0)
            {
                progress?.Issue(target.Name, null, string.Join(" ", t.Notes));
            }
        }

        var resultMap = results.ToDictionary(r => r.Format.SonarrId, r => r.Target.Id);
        var resultOwned = ownedSet.Concat(ownedNew.Select(f => f.Id)).Where(id => id > 0).Distinct().Order().ToList();
        var changed = results.Count(r => r.Outcome is "new" or "renamed" or "updated");
        if (changed > 0)
        {
            logger.LogInformation("Custom formats from Sonarr: {New} new, {Renamed} as a Sonarr copy, {Updated} updated, {Reused} reused",
                results.Count(r => r.Outcome == "new"), results.Count(r => r.Outcome == "renamed"), results.Count(r => r.Outcome == "updated"), results.Count(r => r.Outcome == "reused"));
        }

        return new SonarrFormatSync(resultMap, resultOwned, results.Select(r => new SonarrFormatPreview(r.Format.SonarrId, r.Format.Definition.Name, r.Outcome, r.Target.Name, r.Format.Notes)).ToList());
    }

    private const int MaxFormatName = 128;

    private static void Write(CustomFormat format, SonarrCustomFormats.Translated t)
    {
        format.IncludeWhenRenaming = t.Definition.IncludeWhenRenaming;
        format.SpecificationsJson = CustomFormatJson.SerializeSpecifications(t.Definition.Specifications);
        format.Note = t.Notes.Count == 0 ? null : Truncate("From Sonarr: " + string.Join(" ", t.Notes), 1024);
    }

    private static string KeyOfStored(CustomFormat format)
    {
        try
        {
            return SonarrCustomFormats.KeyOf(CustomFormatJson.ParseSpecifications(format.SpecificationsJson, format.Name));
        }
        catch (Exception ex) when (ex is CustomFormatParseException or JsonException)
        {
            return string.Empty;
        }
    }
}
