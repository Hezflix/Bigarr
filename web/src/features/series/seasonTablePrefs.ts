import { usePersistedPrefs } from '@/features/library/core'
import { defaultEpisodeSort, type EpisodeSort, episodeSortKeys } from './episodeSort'

/** How the season tables show episodes: one setting for every season of every show. */
export type SeasonTablePrefs = { sort: EpisodeSort }

export const defaultSeasonTablePrefs: SeasonTablePrefs = { sort: defaultEpisodeSort }

/** Per user and per browser, like the library's view options. */
const storageKey = (userId: number | undefined) => `bigarr.seasonTable.${userId ?? 'anonymous'}`

/** Fills what a stored value lacks and drops what this version no longer knows (a sort key a later version removed). */
export function readSeasonTablePrefs(stored: unknown): SeasonTablePrefs {
  const value = (stored ?? {}) as Partial<SeasonTablePrefs>
  const sort = value.sort
  const known = !!sort && (episodeSortKeys as readonly string[]).includes(sort.key) && (sort.direction === 'asc' || sort.direction === 'desc')
  return { ...defaultSeasonTablePrefs, ...value, sort: known ? sort : defaultEpisodeSort }
}

function readPrefs(key: string): SeasonTablePrefs {
  try {
    const raw = localStorage.getItem(key)
    return raw ? readSeasonTablePrefs(JSON.parse(raw)) : defaultSeasonTablePrefs
  } catch {
    /* storage unavailable or a corrupt value: the defaults */
    return defaultSeasonTablePrefs
  }
}

export function useSeasonTablePrefs(userId: number | undefined) {
  return usePersistedPrefs(storageKey(userId), readPrefs)
}
