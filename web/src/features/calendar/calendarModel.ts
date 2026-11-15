import type { CalendarEpisode, CalendarMovie, CalendarStatus, VersionStatus } from '@/api/client'
import { usePersistedPrefs } from '@/features/library/core'

/** What the calendar lists: both, or one side only. */
export type CalendarKind = 'all' | 'films' | 'series'

export type CalendarView = 'week' | 'month'

export type CalendarPrefs = { kind: CalendarKind; view: CalendarView; unmonitored: boolean }

export const calendarKinds: readonly CalendarKind[] = ['all', 'films', 'series']

export const defaultCalendarPrefs: CalendarPrefs = { kind: 'all', view: 'week', unmonitored: false }

/** Fills what a stored value lacks and drops what this version does not know. */
export function readCalendarPrefs(stored: unknown): CalendarPrefs {
  const value = (stored ?? {}) as Partial<Record<keyof CalendarPrefs, unknown>>
  return {
    kind: calendarKinds.includes(value.kind as CalendarKind) ? (value.kind as CalendarKind) : defaultCalendarPrefs.kind,
    view: value.view === 'week' || value.view === 'month' ? value.view : defaultCalendarPrefs.view,
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
 * An episode's calendar status in the library legend's colours: on disk is "available", aired without a file is
 * "missing", not aired yet (or airing now) is "pending". An unmonitored entry keeps its colour, dashed and faded.
 */
export function episodeTone(status: CalendarStatus): VersionStatus {
  switch (status) {
    case 'downloaded':
      return 'available'
    case 'downloading':
      return 'downloading'
    case 'missing':
    case 'unmonitored':
      return 'missing'
    default:
      return 'pending'
  }
}

export type CalendarEntry =
  | { kind: 'film'; key: string; tone: VersionStatus; monitored: boolean; film: CalendarMovie }
  | { kind: 'episode'; key: string; tone: VersionStatus; monitored: boolean; episode: CalendarEpisode }

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
    add(film.date, { kind: 'film', key: `film-${film.versionId}-${film.releaseType}`, tone: film.status, monitored: film.monitored, film })
  }

  for (const episode of episodes) {
    if (!episode.airDateUtc) continue
    add(localDay(new Date(episode.airDateUtc)), {
      kind: 'episode',
      key: `episode-${episode.versionId}-${episode.episodeId}`,
      tone: episodeTone(episode.status),
      monitored: episode.monitored,
      episode,
    })
  }

  return map
}
