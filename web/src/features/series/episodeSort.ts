import type { Episode, EpisodeFile } from '@/api/client'
import type { SortDirection } from '@/features/library/core'
import { audioLabel, type EpisodeColumn, episodeColumns, episodeStatus, fileFlags, fileLanguages, statusRank } from './episodeColumns'

/** What a season table sorts by. `number` is the season's own order (the series' order when it has one, TVDB's otherwise). */
export const episodeSortKeys = ['number', 'title', ...episodeColumns] as const

export type EpisodeSortKey = 'number' | 'title' | EpisodeColumn

export type EpisodeSort = { key: EpisodeSortKey; direction: SortDirection }

export const defaultEpisodeSort: EpisodeSort = { key: 'number', direction: 'asc' }

const collator = new Intl.Collator(undefined, { sensitivity: 'base', numeric: true })

/** First click on a header: numbers, names and dates in reading order; size, runtime and score biggest first. */
export const firstEpisodeDirection = (key: EpisodeSortKey): SortDirection => (key === 'size' || key === 'score' || key === 'runtime' || key === 'quality' ? 'desc' : 'asc')

/** Whether the tables can still show what they are sorted on once these columns are picked. */
export const sortStillShown = (sort: EpisodeSort, columns: readonly EpisodeColumn[]) =>
  sort.key === 'number' || sort.key === 'title' || columns.includes(sort.key)

/** A click on the sorted column reverses it; on another column, starts it in its first direction. */
export const nextEpisodeSort = (current: EpisodeSort, key: EpisodeSortKey): EpisodeSort =>
  current.key === key ? { key, direction: current.direction === 'asc' ? 'desc' : 'asc' } : { key, direction: firstEpisodeDirection(key) }

/** What the row holds for the version on screen. */
export type EpisodeRowContext = { file: EpisodeFile | undefined; monitored: boolean }

const text = (value: string | null | undefined) => (value ? value : null)

function keyOf(episode: Episode, key: Exclude<EpisodeSortKey, 'number'>, { file, monitored }: EpisodeRowContext): number | string | null {
  switch (key) {
    case 'absolute':
      return episode.displayAbsoluteNumber ?? null
    case 'title':
      return text(episode.title)
    case 'airDate':
      return episode.airDateUtc ? Date.parse(episode.airDateUtc) : episode.airDate ? Date.parse(episode.airDate) : null
    case 'runtime':
      return episode.runtime ?? null
    case 'status':
      return statusRank[episodeStatus(episode, monitored, file)]
    // Resolution first, then the revision; the name breaks ties between sources of one resolution.
    case 'quality':
      return file ? file.qualityResolution * 1000 + file.revisionVersion : null
    case 'size':
      return file?.size ?? null
    case 'languages':
      return text(fileLanguages(file).join(', '))
    case 'audio':
      return text(audioLabel(file))
    case 'video':
      return text(file?.mediaInfo?.videoCodec)
    case 'dynamicRange':
      return text(file?.mediaInfo?.videoDynamicRangeType)
    case 'subtitles':
      return file?.mediaInfo?.subtitles.length ? file.mediaInfo.subtitles.length : null
    case 'releaseGroup':
      return text(file?.releaseGroup)
    case 'formats':
      return text(file?.customFormats?.join(', '))
    case 'score':
      return file ? file.customFormatScore : null
    case 'flags':
      return text(fileFlags(file).join(', '))
    case 'relativePath':
      return text(file?.relativePath)
  }
}

/**
 * Sorts a copy of a season's episodes, given in the season's own order. A missing value (no air date, no file…) goes
 * last whatever the direction; ties keep the season's order.
 */
export function sortEpisodes(episodes: Episode[], sort: EpisodeSort, contextOf: (episode: Episode) => EpisodeRowContext): Episode[] {
  if (sort.key === 'number') return sort.direction === 'asc' ? episodes : [...episodes].reverse()
  const key = sort.key
  const sign = sort.direction === 'asc' ? 1 : -1
  return episodes
    .map((episode, index) => ({ episode, index, key: keyOf(episode, key, contextOf(episode)) }))
    .sort((a, b) => {
      if (a.key === null || b.key === null) return a.key === b.key ? a.index - b.index : a.key === null ? 1 : -1
      const order = typeof a.key === 'string' ? collator.compare(a.key, b.key as string) : a.key - (b.key as number)
      return order !== 0 ? order * sign : a.index - b.index
    })
    .map((entry) => entry.episode)
}
