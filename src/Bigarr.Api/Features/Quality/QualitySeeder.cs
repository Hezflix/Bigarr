using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Quality;
using Microsoft.EntityFrameworkCore;
using ParserDefinition = Bigarr.Parsing.Qualities.QualityDefinition;
using ParserModifier = Bigarr.Parsing.Qualities.Modifier;
using ParserQuality = Bigarr.Parsing.Qualities.Quality;

namespace Bigarr.Api.Features.Quality;

/// <summary>
/// Seeds the quality definitions from the vendored parser (ids identical to Radarr, see VENDORED.md) and the two
/// system profiles the library scan hands out: <c>1080p</c> and <c>4K</c>. Idempotent: never overwrites user edits.
/// </summary>
public sealed class QualitySeeder(BigarrDbContext db, ILogger<QualitySeeder> logger) : IStartupTask
{
    /// <summary>HD profile: 720p and 1080p, cutoff Bluray-1080p. Default of the add form and of everything the scan finds below 4K.</summary>
    public const string HdProfileName = "1080p";

    /// <summary>UHD profile: 2160p only — a 4K version never settles for a 1080p release, that is what the 1080p version is for.</summary>
    public const string UhdProfileName = "4K";

    /// <summary>The series twins of the two above (kind Series): what the series scan and a new series version start from.</summary>
    public const string SeriesHdProfileName = "Series 1080p";

    public const string SeriesUhdProfileName = "Series 4K";

    /// <summary>The single catch-all profile seeded until 2026-09-10, renamed in place so existing versions keep their profile.</summary>
    private const string LegacyProfileName = "Any";

    public const int FirstGroupId = 1000;

    public const int UhdResolution = 2160;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var definitions = ParserQuality.DefaultQualityDefinitions.OrderBy(d => d.Weight).ThenBy(d => d.Quality.Id).ToList();
        var existing = await db.QualityDefinitions.Select(q => q.Id).ToListAsync(cancellationToken);
        var added = 0;

        foreach (var def in definitions.Where(d => !existing.Contains(d.Quality.Id)))
        {
            db.QualityDefinitions.Add(new QualityDefinition
            {
                Id = def.Quality.Id,
                Name = def.Quality.Name,
                Title = def.Title,
                Source = (QualitySource)(int)def.Quality.Source,
                Resolution = def.Quality.Resolution,
                Modifier = (QualityModifier)(int)def.Quality.Modifier,
                MinSize = def.MinSize,
                PreferredSize = def.PreferredSize,
                MaxSize = def.MaxSize,
                SeriesMinSize = SonarrDefaultSizes.For(def.Quality.Id).Min,
                SeriesPreferredSize = SonarrDefaultSizes.For(def.Quality.Id).Preferred,
                SeriesMaxSize = SonarrDefaultSizes.For(def.Quality.Id).Max,
            });
            added++;
        }

        var legacy = await db.QualityProfiles.FirstOrDefaultAsync(p => p.Name == LegacyProfileName && p.IsSystem, cancellationToken);
        if (legacy is not null)
        {
            var hd = BuildHdProfile(definitions);
            legacy.Name = hd.Name;
            legacy.Items = hd.Items;
            legacy.Cutoff = hd.Cutoff;
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Renamed the system quality profile '{Legacy}' to '{Profile}'", LegacyProfileName, hd.Name);
        }

        var known = await db.QualityProfiles.Select(p => p.Name).ToListAsync(cancellationToken);
        foreach (var profile in new[] { BuildHdProfile(definitions), BuildUhdProfile(definitions), BuildSeriesHdProfile(definitions), BuildSeriesUhdProfile(definitions) }.Where(p => !known.Contains(p.Name)))
        {
            db.QualityProfiles.Add(profile);
            logger.LogInformation("Seeded the system quality profile '{Profile}'", profile.Name);
        }

        await db.SaveChangesAsync(cancellationToken);
        if (added > 0)
        {
            logger.LogInformation("Seeded {Count} quality definition(s) from the parser", added);
        }
    }

    public static QualityProfile BuildHdProfile(IReadOnlyList<ParserDefinition> definitions) =>
        BuildProfile(definitions, HdProfileName, 720, 1080, ParserQuality.Bluray1080p.Id);

    public static QualityProfile BuildUhdProfile(IReadOnlyList<ParserDefinition> definitions) =>
        BuildProfile(definitions, UhdProfileName, UhdResolution, UhdResolution, ParserQuality.Bluray2160p.Id);

    public static QualityProfile BuildSeriesHdProfile(IReadOnlyList<ParserDefinition> definitions)
    {
        var profile = BuildProfile(definitions, SeriesHdProfileName, 720, 1080, ParserQuality.Bluray1080p.Id);
        profile.Kind = ProfileKind.Series;
        return profile;
    }

    public static QualityProfile BuildSeriesUhdProfile(IReadOnlyList<ParserDefinition> definitions)
    {
        var profile = BuildProfile(definitions, SeriesUhdProfileName, UhdResolution, UhdResolution, ParserQuality.Bluray2160p.Id);
        profile.Kind = ProfileKind.Series;
        return profile;
    }

    /// <summary>
    /// Every quality, ordered like Radarr's defaults with the WEB-DL/WEBRip pairs grouped: the order IS the rank
    ///, so both profiles list everything and only the allowed flags differ. A file outside the profile keeps
    /// a rank that way — which is what makes the cutoff of a scanned version computable.
    /// </summary>
    private static QualityProfile BuildProfile(IReadOnlyList<ParserDefinition> definitions, string name, int minResolution, int maxResolution, int cutoffQualityId)
    {
        var items = new List<QualityProfileItem>();
        var nextGroupId = FirstGroupId;

        foreach (var group in definitions.GroupBy(d => string.IsNullOrEmpty(d.GroupName) ? $"__{d.Quality.Id}" : d.GroupName))
        {
            var members = group.ToList();
            var allowed = members.All(m => IsWanted(m, minResolution, maxResolution));

            if (members.Count == 1 && string.IsNullOrEmpty(members[0].GroupName))
            {
                items.Add(new QualityProfileItem { QualityIds = [members[0].Quality.Id], Allowed = allowed });
            }
            else
            {
                items.Add(new QualityProfileItem
                {
                    GroupId = nextGroupId++,
                    GroupName = members[0].GroupName,
                    QualityIds = members.Select(m => m.Quality.Id).ToList(),
                    Allowed = allowed,
                });
            }
        }

        var cutoffItem = items.First(i => i.QualityIds.Contains(cutoffQualityId));

        return new QualityProfile
        {
            Name = name,
            Items = items,
            Cutoff = cutoffItem.GroupId ?? cutoffQualityId,
            UpgradeAllowed = true,
            MinFormatScore = 0,
            CutoffFormatScore = 0,
            MinUpgradeFormatScore = 0,
            PreferredLanguage = "en",
            IsSystem = true,
        };
    }

    /// <summary>Discs and raw HD captures are never grabbed automatically, whatever their resolution.</summary>
    private static bool IsWanted(ParserDefinition definition, int minResolution, int maxResolution) =>
        definition.Quality.Resolution >= minResolution
        && definition.Quality.Resolution <= maxResolution
        && definition.Quality.Modifier is not (ParserModifier.BRDISK or ParserModifier.RAWHD);
}
