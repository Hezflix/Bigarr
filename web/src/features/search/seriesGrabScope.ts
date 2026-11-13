import type { Episode } from '@/api/client'

/** What is searched: an episode, a season, or the whole series. */
export type SeriesSearchScope = { kind: 'episode'; episodeId: number; label: string } | { kind: 'season'; season: number; label: string; orderSeason?: boolean } | { kind: 'series'; label: string }

/**
 * The episodes a grab can go to for a version: those with a target in that version. Specials a film owns are left out:
 * the film engine grabs them, and the grab endpoint refuses them.
 */
export function pickableEpisodes(episodes: Episode[], versionId: number): Episode[] {
  return episodes.filter((e) => !e.ownedByFilm && e.versions.some((v) => v.versionId === versionId))
}

/**
 * The episodes the grab picker starts with. The scope only decides what was searched; what a grab goes to is always the
 * user's pick, prefilled with Bigarr's match when there is one. A release mapped to no episode starts from the scope it was
 * searched from: the episode, the season's episodes (the order's season when searched there), nothing for the whole series.
 */
export function prefillEpisodeIds(release: { episodes: { id: number }[] }, scope: SeriesSearchScope, pickable: Episode[]): number[] {
  const ids = new Set(pickable.map((e) => e.id))
  if (release.episodes.length > 0) return release.episodes.map((e) => e.id).filter((id) => ids.has(id))
  if (scope.kind === 'episode') return ids.has(scope.episodeId) ? [scope.episodeId] : []
  if (scope.kind === 'season')
    return pickable.filter((e) => (scope.orderSeason ? !e.notInOrder && e.displaySeasonNumber === scope.season : e.seasonNumber === scope.season)).map((e) => e.id)
  return []
}

/** The picker's seasons, in order with the specials last, each with its episodes in order. */
export function pickerSeasons(episodes: Episode[]): { season: number; episodes: Episode[] }[] {
  const seasons = [...new Set(episodes.map((e) => e.seasonNumber))].sort((a, b) => (a === 0 ? 1 : b === 0 ? -1 : a - b))
  return seasons.map((season) => ({ season, episodes: episodes.filter((e) => e.seasonNumber === season).sort((a, b) => a.episodeNumber - b.episodeNumber) }))
}

/** `S01E03` style, TVDB numbers. */
export const episodeCode = (e: Pick<Episode, 'seasonNumber' | 'episodeNumber'>) => `S${String(e.seasonNumber).padStart(2, '0')}E${String(e.episodeNumber).padStart(2, '0')}`
