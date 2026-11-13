import type { TFunction } from 'i18next'
import type { Episode } from '@/api/client'
import type { GrabTargetChoice } from './releaseTable'

/** What is searched: an episode, a season, or the whole series. */
export type SeriesSearchScope = { kind: 'episode'; episodeId: number; label: string } | { kind: 'season'; season: number; label: string; orderSeason?: boolean } | { kind: 'series'; label: string }

/**
 * What a release mapped to no episode can be grabbed for: the scope it was searched from. An episode search gives that
 * episode, a season search the season's episodes (the order's season when searched there), a series search every episode
 * or one season, the user picks. Specials a film owns are left out: the film engine grabs them.
 */
export function scopeChoices(scope: SeriesSearchScope, episodes: Episode[], t: TFunction): (GrabTargetChoice & { episodeIds: number[] })[] {
  const own = episodes.filter((e) => !e.ownedByFilm)
  if (scope.kind === 'episode') return [{ value: 'episode', label: scope.label, episodeIds: [scope.episodeId] }]
  if (scope.kind === 'season') {
    const ids = own.filter((e) => (scope.orderSeason ? !e.notInOrder && e.displaySeasonNumber === scope.season : e.seasonNumber === scope.season)).map((e) => e.id)
    return ids.length > 0 ? [{ value: `season-${scope.season}`, label: t('seriesSearch.targetSeason', { season: scope.label, count: ids.length }), episodeIds: ids }] : []
  }

  const seasons = [...new Set(own.map((e) => e.seasonNumber))].sort((a, b) => (a === 0 ? 1 : b === 0 ? -1 : a - b))
  const all = own.filter((e) => e.seasonNumber > 0).map((e) => e.id)
  return [
    ...(all.length > 0 ? [{ value: 'all', label: t('seriesSearch.targetAll', { count: all.length }), episodeIds: all }] : []),
    ...seasons.map((season) => {
      const ids = own.filter((e) => e.seasonNumber === season).map((e) => e.id)
      return { value: `season-${season}`, label: t(season === 0 ? 'seriesSearch.targetSpecials' : 'seriesSearch.targetSeasonNumber', { season, count: ids.length }), episodeIds: ids }
    }),
  ]
}

