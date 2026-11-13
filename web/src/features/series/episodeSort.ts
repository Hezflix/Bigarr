import type { Episode, EpisodeFile } from '@/api/client'
import type { SortDirection } from '@/features/library/core'

/** What a season table sorts by. `number` is the season's own order (the series' order when it has one, TVDB's otherwise). */
export type EpisodeSortKey = 'number' | 'absolute' | 'title' | 'airDate' | 'size'

export type EpisodeSort = { key: EpisodeSortKey; direction: SortDirection }

export const defaultEpisodeSort: EpisodeSort = { key: 'number', direction: 'asc' }

const collator = new Intl.Collator(undefined, { sensitivity: 'base', numeric: true })

/** First click on a header: numbers, titles and dates in reading order, the biggest file first. */
export const firstEpisodeDirection = (key: EpisodeSortKey): SortDirection => (key === 'size' ? 'desc' : 'asc')

/** A click on the sorted column reverses it; on another column, starts it in its first direction. */
export const nextEpisodeSort = (current: EpisodeSort, key: EpisodeSortKey): EpisodeSort =>
  current.key === key ? { key, direction: current.direction === 'asc' ? 'desc' : 'asc' } : { key, direction: firstEpisodeDirection(key) }

function keyOf(episode: Episode, key: EpisodeSortKey, file: EpisodeFile | undefined): number | string | null {
  switch (key) {
    case 'number':
      return null
    case 'absolute':
      return episode.displayAbsoluteNumber ?? null
    case 'title':
      return episode.title || null
    case 'airDate':
      return episode.airDateUtc ? Date.parse(episode.airDateUtc) : episode.airDate ? Date.parse(episode.airDate) : null
    case 'size':
      return file?.size ?? null
  }
}

/**
 * Sorts a copy of a season's episodes, given in the season's own order. A missing value (no air date, no file…) goes
 * last whatever the direction; ties keep the season's order.
 */
export function sortEpisodes(episodes: Episode[], sort: EpisodeSort, fileOf: (episode: Episode) => EpisodeFile | undefined): Episode[] {
  if (sort.key === 'number') return sort.direction === 'asc' ? episodes : [...episodes].reverse()
  const sign = sort.direction === 'asc' ? 1 : -1
  return episodes
    .map((episode, index) => ({ episode, index, key: keyOf(episode, sort.key, fileOf(episode)) }))
    .sort((a, b) => {
      if (a.key === null || b.key === null) return a.key === b.key ? a.index - b.index : a.key === null ? 1 : -1
      const order = typeof a.key === 'string' ? collator.compare(a.key, b.key as string) : a.key - (b.key as number)
      return order !== 0 ? order * sign : a.index - b.index
    })
    .map((entry) => entry.episode)
}
