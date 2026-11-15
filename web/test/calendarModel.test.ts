import { describe, expect, test } from 'bun:test'
import type { CalendarEpisode, CalendarMovie } from '../src/api/client'
import { defaultCalendarPrefs, entriesByDay, episodeTone, filmTone, localDay, readCalendarPrefs } from '../src/features/calendar/calendarModel'

const film = (versionId: number, title: string, releaseType: CalendarMovie['releaseType'], date: string, isDefaultVersion = true) =>
  ({ movieId: versionId, versionId, title, releaseType, date, isDefaultVersion, status: 'pending', monitored: true }) as CalendarMovie

const episode = (episodeId: number, airDateUtc: string | null, status: CalendarEpisode['status'] = 'unaired') =>
  ({ episodeId, versionId: 1, airDateUtc, status, monitored: status !== 'unmonitored' }) as CalendarEpisode

describe('readCalendarPrefs', () => {
  test('keeps what it knows, fills the rest', () => {
    expect(readCalendarPrefs({ kind: 'films', view: 'month', density: 'compact', unmonitored: true })).toEqual({ kind: 'films', view: 'month', density: 'compact', unmonitored: true })
    expect(readCalendarPrefs({ kind: 'series' })).toEqual({ ...defaultCalendarPrefs, kind: 'series' })
    expect(readCalendarPrefs({ kind: 'books', view: 'year', density: 'cosy', unmonitored: 'yes' })).toEqual(defaultCalendarPrefs)
    expect(readCalendarPrefs(null)).toEqual(defaultCalendarPrefs)
  })
})

describe('episodeTone', () => {
  test('maps the episode statuses onto the library legend', () => {
    expect(episodeTone('downloaded')).toBe('available')
    expect(episodeTone('downloaded', true)).toBe('available')
    expect(episodeTone('downloaded', false)).toBe('cutoffUnmet')
    expect(episodeTone('downloading')).toBe('downloading')
    expect(episodeTone('missing')).toBe('missing')
    expect(episodeTone('unmonitored')).toBe('missing')
    expect(episodeTone('unaired')).toBe('pending')
    expect(episodeTone('onAir')).toBe('pending')
  })
})

describe('filmTone', () => {
  test('a file below the cutoff is violet, like its library chip', () => {
    const onDisk = { ...film(1, 'Alpha', 'digital', '2026-11-12'), status: 'available' } as CalendarMovie
    expect(filmTone({ ...onDisk, cutoffMet: false })).toBe('cutoffUnmet')
    expect(filmTone({ ...onDisk, cutoffMet: true })).toBe('available')
    expect(filmTone({ ...onDisk, cutoffMet: null })).toBe('available')
    expect(filmTone(film(2, 'Beta', 'physical', '2026-11-12'))).toBe('pending')
  })
})

describe('entriesByDay', () => {
  test('films first, cinema then digital then physical, default version first; then episodes by local air day', () => {
    const noon = new Date(2026, 10, 12, 12, 0)
    const map = entriesByDay(
      [film(3, 'Beta', 'physical', '2026-11-12'), film(2, 'Alpha', 'digital', '2026-11-12', false), film(1, 'Alpha', 'digital', '2026-11-12'), film(4, 'Zulu', 'inCinemas', '2026-11-12')],
      [episode(10, noon.toISOString(), 'missing'), episode(11, null)],
    )
    expect(map.get('2026-11-12')?.map((e) => e.key)).toEqual(['film-4-inCinemas', 'film-1-digital', 'film-2-digital', 'film-3-physical', 'episode-1-10'])
    expect(map.get('2026-11-12')?.at(-1)?.tone).toBe('missing')
    expect([...map.keys()]).toEqual(['2026-11-12'])
  })

  test('localDay pads month and day', () => {
    expect(localDay(new Date(2026, 0, 5))).toBe('2026-01-05')
  })
})
