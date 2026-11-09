using Bigarr.Domain.Tv;

namespace Bigarr.Api.Infrastructure.Xem;

/// <summary>
/// Writes XEM scene numbers onto a series' episodes. Adapted from Sonarr's <c>XemService.PerformUpdate</c> and
/// <c>ExtrapolateMappings</c> (develop c018578, src/NzbDrone.Core/DataAugmentation/Xem/XemService.cs, GPL-3.0,
/// Copyright (C) Sonarr contributors): same rules, run on Bigarr's episodes in memory.
/// </summary>
public static class XemNumbering
{
    /// <summary>Returns true when the series has a usable mapping (Sonarr's <c>UseSceneNumbering</c>).</summary>
    public static bool Apply(IReadOnlyCollection<Episode> episodes, IReadOnlyList<XemMapping> mappings)
    {
        ArgumentNullException.ThrowIfNull(episodes);
        ArgumentNullException.ThrowIfNull(mappings);

        foreach (var episode in episodes)
        {
            episode.SceneAbsoluteEpisodeNumber = null;
            episode.SceneSeasonNumber = null;
            episode.SceneEpisodeNumber = null;
            episode.UnverifiedSceneNumbering = false;
        }

        foreach (var mapping in mappings)
        {
            var candidates = episodes.Where(e => e.SeasonNumber == mapping.TvdbSeason && e.EpisodeNumber == mapping.TvdbEpisode).Take(2).ToList();
            if (candidates.Count != 1)
            {
                // Not on TVDB yet (or listed twice for a moment: Sonarr's SingleOrDefault would throw and skip the show).
                continue;
            }

            var episode = candidates[0];

            if (mapping.SceneAbsolute == 0 && mapping.SceneSeason == 0 && mapping.SceneEpisode == 0)
            {
                continue;
            }

            episode.SceneAbsoluteEpisodeNumber = mapping.SceneAbsolute;
            episode.SceneSeasonNumber = mapping.SceneSeason;
            episode.SceneEpisodeNumber = mapping.SceneEpisode;
        }

        if (episodes.Any(e => e.SceneEpisodeNumber.HasValue && e.SceneSeasonNumber != 0))
        {
            Extrapolate(episodes, mappings);
        }

        return mappings.Count > 0;
    }

    private static void Extrapolate(IReadOnlyCollection<Episode> episodes, IReadOnlyList<XemMapping> mappings)
    {
        var mappedSeasons = episodes.Where(e => e.SeasonNumber != 0 && e.SceneEpisodeNumber.HasValue).Select(e => e.SeasonNumber).ToHashSet();
        var sceneEpisodesBySeason = mappings.ToLookup(m => m.SceneSeason).ToDictionary(g => g.Key, g => g.Select(m => m.SceneEpisode).ToHashSet());
        var firstTvdbEpisodeBySeason = mappings.ToLookup(m => m.TvdbSeason).ToDictionary(g => g.Key, g => g.Min(m => m.TvdbEpisode));
        var lastSceneSeason = mappings.Max(m => m.SceneSeason);
        var lastTvdbSeason = mappings.Max(m => m.TvdbSeason);

        // Mark every episode not on XEM as unverified.
        foreach (var episode in episodes)
        {
            if (episode.SeasonNumber == 0 || episode.SceneEpisodeNumber.HasValue)
            {
                continue;
            }

            if (mappedSeasons.Contains(episode.SeasonNumber))
            {
                if (firstTvdbEpisodeBySeason[episode.SeasonNumber] <= episode.EpisodeNumber
                    || (sceneEpisodesBySeason.TryGetValue(episode.SeasonNumber, out var sceneEpisodes) && sceneEpisodes.Contains(episode.EpisodeNumber)))
                {
                    episode.UnverifiedSceneNumbering = true;
                }
            }
            else if (lastSceneSeason != lastTvdbSeason && episode.SeasonNumber > lastTvdbSeason)
            {
                episode.UnverifiedSceneNumbering = true;
            }
        }

        foreach (var episode in episodes)
        {
            if (episode.SeasonNumber == 0 || episode.SceneEpisodeNumber.HasValue || episode.SeasonNumber < lastTvdbSeason || !episode.UnverifiedSceneNumbering)
            {
                continue;
            }

            var seasonMappings = mappings.Where(m => m.TvdbSeason == episode.SeasonNumber).ToList();
            if (seasonMappings.Any(m => m.TvdbEpisode >= episode.EpisodeNumber))
            {
                continue;
            }

            if (seasonMappings.Count > 0)
            {
                var lastEpisodeMapping = seasonMappings.OrderBy(m => m.TvdbEpisode).Last();
                var lastSceneSeasonMapping = mappings.Where(m => m.SceneSeason == lastEpisodeMapping.SceneSeason).OrderBy(m => m.SceneEpisode).Last();
                if (lastSceneSeasonMapping.TvdbSeason == 0)
                {
                    continue;
                }

                var offset = episode.EpisodeNumber - lastEpisodeMapping.TvdbEpisode;
                episode.SceneSeasonNumber = lastEpisodeMapping.SceneSeason;
                episode.SceneEpisodeNumber = lastEpisodeMapping.SceneEpisode + offset;
                episode.SceneAbsoluteEpisodeNumber = lastEpisodeMapping.SceneAbsolute + offset;
            }
            else if (lastTvdbSeason != lastSceneSeason)
            {
                var offset = episode.SeasonNumber - lastTvdbSeason;
                episode.SceneSeasonNumber = lastSceneSeason + offset;
                episode.SceneEpisodeNumber = episode.EpisodeNumber;
            }
        }
    }
}
