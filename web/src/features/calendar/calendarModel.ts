import { calendar, type CalendarEpisode, type CalendarMovie, type CalendarStatus } from '@/api/client'
import { usePersistedPrefs } from '@/features/library/core'
import type { ChipTone } from '@/features/library/VersionChip'

/** What the calendar lists: both, or one side only. */
export type CalendarKind = 'all' | 'films' | 'series'

export type CalendarView = 'week' | 'month'

/** Like the library table: comfortable entries carry the poster, compact ones are a line or two of text. */
export type CalendarDensity = 'comfortable' | 'compact'

export type CalendarPrefs = { kind: CalendarKind; view: CalendarView; density: CalendarDensity; unmonitored: boolean }

export const calendarKinds: readonly CalendarKind[] = ['all', 'films', 'series']

export const calendarDensities: readonly CalendarDensity[] = ['comfortable', 'compact']

export const defaultCalendarPrefs: CalendarPrefs = { kind: 'all', view: 'week', density: 'comfortable', unmonitored: false }

/** Fills what a stored value lacks and drops what this version does not know. */
export function readCalendarPrefs(stored: unknown): CalendarPrefs {
  const value = (stored ?? {}) as Partial<Record<keyof CalendarPrefs, unknown>>
  return {
    kind: calendarKinds.includes(value.kind as CalendarKind) ? (value.kind as CalendarKind) : defaultCalendarPrefs.kind,
    view: value.view === 'week' || value.view === 'month' ? value.view : defaultCalendarPrefs.view,
    density: calendarDensities.includes(value.density as CalendarDensity) ? (value.density as CalendarDensity) : defaultCalendarPrefs.density,
    unmonitored: typeof value.unmonitored === 'boolean' ? value.unmonitored : defaultCalendarPrefs.unmonitored,
  }
}

/** Per user and per browser, like the library's view options. */
const storageKey = (userId: number | undefined) => `bigarr.calendar.${userId ?? 'anonymous'}`

function readPrefs(key: string): CalendarPrefs {
  try {
    const raw = localStorage.getItem(key)
    return raw ? readCalendarPrefs(JSON.parse(raw)) : defaultCalendarPrefs
  } catch {
    /* storage unavailable or a corrupt value: the defaults */
    return defaultCalendarPrefs
  }
}

export function useCalendarPrefs(userId: number | undefined) {
  return usePersistedPrefs(storageKey(userId), readPrefs)
}

/**
 * An episode's calendar status in the library legend's colours: on disk is "available" (violet "cutoff unmet" when the
 * file is below the profile's cutoff), aired without a file is "missing", not aired yet (or airing now) is "pending".
 * An unmonitored entry keeps its colour, dashed and faded.
 */
export function episodeTone(status: CalendarStatus, cutoffMet?: boolean | null): ChipTone {
  switch (status) {
    case 'downloaded':
      return cutoffMet === false ? 'cutoffUnmet' : 'available'
    case 'downloading':
      return 'downloading'
    case 'missing':
    case 'unmonitored':
      return 'missing'
    default:
      return 'pending'
  }
}

/** A film's colour is its version's library chip: available below the cutoff is violet there too. */
export const filmTone = (film: CalendarMovie): ChipTone => (film.status === 'available' && film.cutoffMet === false ? 'cutoffUnmet' : film.status)

export type CalendarEntry =
  | { kind: 'film'; key: string; tone: ChipTone; monitored: boolean; film: CalendarMovie }
  | { kind: 'episode'; key: string; tone: ChipTone; monitored: boolean; episode: CalendarEpisode }

/** The local day `YYYY-MM-DD` of a date: the films' API takes and gives days, not instants. */
export function localDay(date: Date): string {
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`
}

const releaseOrder: Record<CalendarMovie['releaseType'], number> = { inCinemas: 0, digital: 1, physical: 2 }

/**
 * The entries of each local day: the films first (a date has no time, like an all-day event), cinema before digital
 * before physical, then the episodes by air time. Episodes go by their local air day, films by their date.
 */
export function entriesByDay(films: readonly CalendarMovie[], episodes: readonly CalendarEpisode[]): Map<string, CalendarEntry[]> {
  const map = new Map<string, CalendarEntry[]>()
  const add = (day: string, entry: CalendarEntry) => {
    const list = map.get(day)
    if (list) list.push(entry)
    else map.set(day, [entry])
  }

  const sortedFilms = [...films].sort(
    (a, b) =>
      releaseOrder[a.releaseType] - releaseOrder[b.releaseType] ||
      a.title.localeCompare(b.title) ||
      Number(b.isDefaultVersion) - Number(a.isDefaultVersion) ||
      a.versionId - b.versionId,
  )
  for (const film of sortedFilms) {
    add(film.date, { kind: 'film', key: `film-${film.versionId}-${film.releaseType}`, tone: filmTone(film), monitored: film.monitored, film })
  }

  for (const episode of episodes) {
    if (!episode.airDateUtc) continue
    add(localDay(new Date(episode.airDateUtc)), {
      kind: 'episode',
      key: `episode-${episode.versionId}-${episode.episodeId}`,
      tone: episodeTone(episode.status, episode.cutoffMet),
      monitored: episode.monitored,
      episode,
    })
  }

  return map
}

/** The address a calendar app subscribes to: the feed, the page's "unmonitored" choice, and the calendar-only key. */
export function subscriptionUrl(origin: string, key: string, unmonitored: boolean): string {
  return `${origin}${calendar.icsUrl}?apikey=${encodeURIComponent(key)}${unmonitored ? '&unmonitored=true' : ''}`
}
