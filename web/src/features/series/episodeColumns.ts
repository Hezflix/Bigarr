import type { Episode, EpisodeFile } from '@/api/client'

/**
 * The optional columns of the season tables, in their display order: Sonarr's episode columns that Bigarr's data
 * fills. The number and the title are always shown. One setting for every season of every show.
 */
export const episodeColumns = [
  'absolute',
  'airDate',
  'runtime',
  'status',
  'quality',
  'size',
  'languages',
  'audio',
  'video',
  'dynamicRange',
  'subtitles',
  'releaseGroup',
  'formats',
  'score',
  'flags',
  'relativePath',
] as const

export type EpisodeColumn = (typeof episodeColumns)[number]

/** What the tables showed before the picker: number, title, air date and the file (quality, size, group). */
export const defaultEpisodeColumns: EpisodeColumn[] = ['absolute', 'airDate', 'quality', 'size', 'releaseGroup']

/** Keeps the known columns, in the declared order; anything else (a column a later version dropped) is ignored. */
export function knownColumns(stored: unknown): EpisodeColumn[] {
  if (!Array.isArray(stored)) return defaultEpisodeColumns
  return episodeColumns.filter((c) => stored.includes(c))
}

/** Sonarr's episode status for the version on screen. */
export type EpisodeStatus = 'downloaded' | 'missing' | 'unmonitored' | 'unaired'

export function episodeStatus(episode: Episode, monitored: boolean, file: EpisodeFile | undefined): EpisodeStatus {
  if (file) return 'downloaded'
  if (!episode.hasAired) return 'unaired'
  return monitored ? 'missing' : 'unmonitored'
}

/** Missing first: what needs attention sorts on top. */
export const statusRank: Record<EpisodeStatus, number> = { missing: 0, unmonitored: 1, unaired: 2, downloaded: 3 }

/** The probe reads the layout as a number (5.1, 2): shown with one decimal, like the film page. */
export function channelLayout(channels: number | null | undefined): string | null {
  return channels === null || channels === undefined || channels <= 0 ? null : channels.toFixed(1)
}

/** "EAC3 5.1": the audio codec and its channels, when the probe read them. */
export function audioLabel(file: EpisodeFile | undefined): string | null {
  const media = file?.mediaInfo
  if (!media) return null
  return [media.audioCodec, channelLayout(media.audioChannels)].filter(Boolean).join(' ') || null
}

/** Languages a file says it has; "Unknown" (no tag in the name) says nothing. */
export const fileLanguages = (file: EpisodeFile | undefined) => (file?.languages ?? []).filter((l) => l !== 'Unknown')

/** "freeleech, internal" as the API writes the flags; nothing for none. */
export const fileFlags = (file: EpisodeFile | undefined) => (file?.indexerFlags && file.indexerFlags !== 'none' ? file.indexerFlags.split(', ') : [])
