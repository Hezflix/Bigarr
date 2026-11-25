/** What the series manual import computes over its rows: the series and version they share, renumbering, moving to a season. */

export type EpisodeRef = { id: number; seasonNumber: number; episodeNumber: number }

/** The value most rows hold (ties: the first seen), null when none holds one. */
export function mostCommon(values: (number | null)[]): number | null {
  const counts = new Map<number, number>()
  for (const v of values) if (v !== null) counts.set(v, (counts.get(v) ?? 0) + 1)
  let best: number | null = null
  for (const [v, n] of counts) if (best === null || n > (counts.get(best) ?? 0)) best = v
  return best
}

/** The one series the rows go to, a row without one counting as `fallback`; null when they go to several. */
export function sharedSeries(rows: { seriesId: number | null }[], fallback: number | null): number | null {
  const ids = new Set(rows.map((r) => r.seriesId ?? fallback))
  if (ids.size !== 1) return null
  const [only] = ids
  return only ?? null
}

export type Renumbered = { path: string; episodeId: number | null; seasonNumber: number; episodeNumber: number }

/**
 * Files in name order get one episode each, from `start` in `season`, one by one. A file past the season's last episode
 * gets null (nothing to give it), so the caller can say so instead of guessing.
 */
export function renumber(files: { path: string; name: string }[], episodes: EpisodeRef[], season: number, start: number): Renumbered[] {
  const sorted = [...files].sort((a, b) => a.name.localeCompare(b.name, undefined, { numeric: true, sensitivity: 'base' }))
  return sorted.map((f, i) => {
    const episodeNumber = start + i
    const episode = episodes.find((e) => e.seasonNumber === season && e.episodeNumber === episodeNumber)
    return { path: f.path, episodeId: episode?.id ?? null, seasonNumber: season, episodeNumber }
  })
}

/**
 * The same episode numbers in another season (a pack labelled with the wrong season). Numbers the season does not have
 * are dropped; null when none is left.
 */
export function moveToSeason(episodeIds: number[], episodes: EpisodeRef[], season: number): number[] | null {
  const numbers = episodeIds.map((id) => episodes.find((e) => e.id === id)?.episodeNumber).filter((n): n is number => n !== undefined)
  const moved = numbers.map((n) => episodes.find((e) => e.seasonNumber === season && e.episodeNumber === n)?.id).filter((id): id is number => id !== undefined)
  return moved.length > 0 ? moved : null
}

/** The rows between two clicked ones, both included, in table order (shift-click). */
export function range(paths: string[], from: string, to: string): string[] {
  const a = paths.indexOf(from)
  const b = paths.indexOf(to)
  if (a < 0 || b < 0) return [to]
  return paths.slice(Math.min(a, b), Math.max(a, b) + 1)
}
