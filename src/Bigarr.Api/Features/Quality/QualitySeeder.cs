using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Quality;
using Microsoft.EntityFrameworkCore;
using ParserQuality = Bigarr.Parsing.Qualities.Quality;

namespace Bigarr.Api.Features.Quality;

/// <summary>
/// Seeds the quality definitions from the vendored parser (ids identical to Radarr, see VENDORED.md) and the
/// system profile <c>Any</c> used by the library scan. Idempotent: never overwrites user edits.
/// </summary>
public sealed class QualitySeeder(BigarrDbContext db, ILogger<QualitySeeder> logger) : IStartupTask
{
    public const string AnyProfileName = "Any";

    public const int FirstGroupId = 1000;

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
            });
            added++;
        }

        if (!await db.QualityProfiles.AnyAsync(p => p.Name == AnyProfileName, cancellationToken))
        {
            db.QualityProfiles.Add(BuildAnyProfile(definitions));
            logger.LogInformation("Seeded the system quality profile '{Profile}'", AnyProfileName);
        }

        await db.SaveChangesAsync(cancellationToken);
        if (added > 0)
        {
            logger.LogInformation("Seeded {Count} quality definition(s) from the parser", added);
        }
    }

    /// <summary>Every quality allowed, ordered like Radarr's defaults, WEB-DL/WEBRip pairs grouped, cutoff at Bluray-1080p.</summary>
    public static QualityProfile BuildAnyProfile(IReadOnlyList<Bigarr.Parsing.Qualities.QualityDefinition> definitions)
    {
        var items = new List<QualityProfileItem>();
        var nextGroupId = FirstGroupId;

        foreach (var group in definitions.GroupBy(d => string.IsNullOrEmpty(d.GroupName) ? $"__{d.Quality.Id}" : d.GroupName))
        {
            var members = group.ToList();
            if (members.Count == 1 && string.IsNullOrEmpty(members[0].GroupName))
            {
                items.Add(new QualityProfileItem { QualityIds = [members[0].Quality.Id], Allowed = true });
            }
            else
            {
                items.Add(new QualityProfileItem
                {
                    GroupId = nextGroupId++,
                    GroupName = members[0].GroupName,
                    QualityIds = members.Select(m => m.Quality.Id).ToList(),
                    Allowed = true,
                });
            }
        }

        var bluray1080 = ParserQuality.Bluray1080p.Id;
        var cutoffItem = items.First(i => i.QualityIds.Contains(bluray1080));

        return new QualityProfile
        {
            Name = AnyProfileName,
            Items = items,
            Cutoff = cutoffItem.GroupId ?? bluray1080,
            UpgradeAllowed = true,
            MinFormatScore = 0,
            CutoffFormatScore = 0,
            MinUpgradeFormatScore = 0,
            PreferredLanguage = "en",
            IsSystem = true,
        };
    }
}
