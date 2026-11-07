using Bigarr.Api.Features.Quality;
using Bigarr.Api.Infrastructure.RadarrImport;
using Bigarr.Domain.Quality;

namespace Bigarr.Api.Features.Migration;

/// <summary>
/// The quality part of a Radarr profile in Bigarr's terms: its qualities and groups in order with their allowed flags, its
/// cutoff (a quality or a group) and whether upgrades are allowed. Radarr and Bigarr share the quality ids, the lowest to
/// highest order and the group convention (ids from 1000), so the copy is one to one. A quality Radarr does not list comes
/// first, not allowed, so every quality keeps a rank as Bigarr requires; one Bigarr does not know is left out.
/// </summary>
public static class RadarrProfileQualities
{
    public sealed record Translation(List<QualityProfileItem> Items, int Cutoff, bool UpgradeAllowed);

    /// <returns>Null when the profile carries no quality list (a Radarr too old, a test double), or when the copy would not
    /// make a valid Bigarr profile: nothing allowed, or a cutoff that is not an allowed entry.</returns>
    public static Translation? Translate(RadarrProfile source, IReadOnlyCollection<int> knownQualityIds)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(knownQualityIds);

        if (source.Items is not { Count: > 0 } radarrItems || source.Cutoff is not { } radarrCutoff)
        {
            return null;
        }

        var known = knownQualityIds.ToHashSet();
        var seen = new HashSet<int>();
        var groupIds = new HashSet<int>();
        var renamedGroups = new Dictionary<int, int>();
        var items = new List<QualityProfileItem>();
        foreach (var item in radarrItems)
        {
            if (item.Quality is { } quality && item.Items is not { Count: > 0 })
            {
                if (known.Contains(quality.Id) && seen.Add(quality.Id))
                {
                    items.Add(new QualityProfileItem { QualityIds = [quality.Id], Allowed = item.Allowed });
                }

                continue;
            }

            var qualityIds = (item.Items ?? []).Select(i => i.Quality?.Id).OfType<int>().Where(id => known.Contains(id) && seen.Add(id)).ToList();
            if (qualityIds.Count == 0)
            {
                continue;
            }

            // Radarr numbers its groups from 1000 like Bigarr; a group id out of that range or taken twice gets a free one.
            var groupId = item.Id is { } id && id >= QualitySeeder.FirstGroupId && groupIds.Add(id) ? id : NextGroupId(groupIds, radarrItems);
            if (item.Id is { } original && original < QualitySeeder.FirstGroupId)
            {
                renamedGroups[original] = groupId;
            }

            items.Add(new QualityProfileItem { GroupId = groupId, GroupName = string.IsNullOrWhiteSpace(item.Name) ? $"Group {groupId}" : item.Name.Trim(), QualityIds = qualityIds, Allowed = item.Allowed });
        }

        var missing = known.Except(seen).OrderBy(id => id).Select(id => new QualityProfileItem { QualityIds = [id], Allowed = false }).ToList();
        items.InsertRange(0, missing);

        // The cutoff names a quality or a group; a quality inside a group stands for its group.
        var cutoff = renamedGroups.GetValueOrDefault(radarrCutoff, radarrCutoff);
        var cutoffItem = items.FirstOrDefault(i => i.GroupId == cutoff) ?? items.FirstOrDefault(i => i.QualityIds.Contains(cutoff));
        if (cutoffItem is not { Allowed: true })
        {
            return null;
        }

        return new Translation(items, cutoffItem.GroupId ?? cutoffItem.QualityIds[0], source.UpgradeAllowed ?? true);
    }

    /// <summary>True when the profile already has this quality part.</summary>
    public static bool Matches(QualityProfile profile, Translation translation)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(translation);

        return profile.Cutoff == translation.Cutoff
            && profile.UpgradeAllowed == translation.UpgradeAllowed
            && profile.Items.Count == translation.Items.Count
            && profile.Items.Zip(translation.Items).All(pair =>
                pair.First.GroupId == pair.Second.GroupId
                && pair.First.GroupName == pair.Second.GroupName
                && pair.First.Allowed == pair.Second.Allowed
                && pair.First.QualityIds.SequenceEqual(pair.Second.QualityIds));
    }

    public static void Apply(QualityProfile profile, Translation translation)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(translation);

        profile.Items = translation.Items.Select(i => new QualityProfileItem { GroupId = i.GroupId, GroupName = i.GroupName, QualityIds = [.. i.QualityIds], Allowed = i.Allowed }).ToList();
        profile.Cutoff = translation.Cutoff;
        profile.UpgradeAllowed = translation.UpgradeAllowed;
    }

    private static int NextGroupId(HashSet<int> taken, List<RadarrProfileItem> radarrItems)
    {
        var reserved = radarrItems.Select(i => i.Id).OfType<int>().ToHashSet();
        var id = QualitySeeder.FirstGroupId;
        while (taken.Contains(id) || reserved.Contains(id))
        {
            id++;
        }

        taken.Add(id);
        return id;
    }
}
