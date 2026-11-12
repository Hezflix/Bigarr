import { describe, expect, test } from 'bun:test'
import type { MovieSummary, SeriesSummary, VersionSummary } from '../src/api/client'
import { mainVersionIn, sortItems, totalSize, versionConditionsMatch, versionNames, emptyConditions } from '../src/features/library/core'
import { emptyFilter, matchesFilter, presets, rootFolderView, sortMovies } from '../src/features/library/libraryView'
import {
  emptySeriesFilter,
  progressOf,
  sameSeriesFilter,
  seriesMatchesFilter,
  seriesMatchesSearch,
  seriesPresets,
  seriesRootFolderView,
  sortSeries,
  toRow,
} from '../src/features/series/seriesView'

const now = Date.parse('2026-11-11T12:00:00Z')

// ---- fixtures ----------------------------------------------------------------

const filmVersion = (patch: Partial<VersionSummary>): VersionSummary => ({
  id: 1,
  name: '1080p',
  mode: 'managed',
  isDefault: true,
  monitored: true,
  qualityProfileId: 1,
  status: 'available',
  quality: 'Bluray-1080p',
  minimumAvailability: 'released',
  rootFolderId: 1,
  tags: [],
  size: 10,
  cutoffMet: true,
  plays: 0,
  lastPlayedAt: null,
  ...patch,
})

const film = (id: number, title: string, versions: VersionSummary[], patch: Partial<MovieSummary> = {}): MovieSummary => ({
  id,
  tmdbId: id,
  title,
  sortTitle: title.toLowerCase(),
  year: 2000 + id,
  posterUrl: null,
  effectiveStatus: 'released',
  monitored: true,
  addedAt: '2026-11-01T00:00:00Z',
  tmdbRating: null,
  imdbRating: null,
  versions,
  plays: 0,
  lastPlayedAt: null,
  ...patch,
})

const films = [
  film(1, 'Alien', [filmVersion({ id: 11, size: 30 }), filmVersion({ id: 12, name: '4k', isDefault: false, size: 80, rootFolderId: 2, quality: 'Bluray-2160p' })]),
  film(2, 'Brazil', [filmVersion({ id: 21, status: 'missing', size: null, quality: null, cutoffMet: null })]),
  film(3, 'Cube', [filmVersion({ id: 31, cutoffMet: false, size: 5 }), filmVersion({ id: 32, name: '4k', isDefault: false, monitored: false, status: 'missing', size: null, quality: null, cutoffMet: null })]),
]

type SeriesVersionPatch = Partial<SeriesSummary['versions'][number]>

const seriesVersion = (patch: SeriesVersionPatch): SeriesSummary['versions'][number] => ({
  id: 1,
  name: '1080p',
  isDefault: true,
  monitored: true,
  qualityProfileId: 7,
  qualityProfile: 'HD',
  statistics: { episodeCount: 10, episodeFileCount: 10, monitoredEpisodeCount: 10, totalEpisodeCount: 12, sizeOnDisk: 100 },
  mode: 'managed',
  rootFolderId: 5,
  tags: [],
  monitorNewItems: 'all',
  status: 'available',
  qualities: ['WEBDL-1080p'],
  cutoffMet: true,
  ...patch,
})

const show = (id: number, title: string, versions: SeriesSummary['versions'], patch: Partial<SeriesSummary> = {}): SeriesSummary => ({
  id,
  tvdbId: id,
  tmdbId: null,
  title,
  sortTitle: title.toLowerCase(),
  year: 2010 + id,
  status: 'continuing',
  network: 'HBO',
  seriesType: 'standard',
  posterUrl: null,
  addedAt: '2026-11-01T00:00:00Z',
  monitored: true,
  seasonCount: 2,
  nextAiring: null,
  versions,
  ...patch,
})

const shows = [
  show(1, 'Andor', [seriesVersion({ id: 11 }), seriesVersion({ id: 12, name: '4k', isDefault: false, rootFolderId: 6, qualities: ['WEBDL-2160p'], statistics: { episodeCount: 10, episodeFileCount: 4, monitoredEpisodeCount: 10, totalEpisodeCount: 12, sizeOnDisk: 400 }, status: 'missing' })], {
    nextAiring: '2026-12-01',
  }),
  show(2, 'Bluey', [seriesVersion({ id: 21, statistics: { episodeCount: 0, episodeFileCount: 0, monitoredEpisodeCount: 0, totalEpisodeCount: 0, sizeOnDisk: 0 }, status: 'pending', qualities: [], cutoffMet: null })], {
    status: 'upcoming',
    network: null,
    nextAiring: '2026-11-20',
  }),
  show(3, 'Chernobyl', [seriesVersion({ id: 31, qualities: ['HDTV-720p', 'WEBDL-1080p'], cutoffMet: false })], { status: 'ended', seriesType: 'anime', network: 'Sky' }),
].map(toRow)

const titles = (items: { title: string }[]) => items.map((i) => i.title)

// ---- shared core ---------------------------------------------------------------

describe('library core', () => {
  test('sortItems puts missing keys last in both directions and breaks ties by sort title', () => {
    const rows = [
      { sortTitle: 'b', key: 2 },
      { sortTitle: 'a', key: null },
      { sortTitle: 'c', key: 2 },
      { sortTitle: 'd', key: 1 },
    ]
    expect(sortItems(rows, (r) => r.key, 'asc').map((r) => r.sortTitle)).toEqual(['d', 'b', 'c', 'a'])
    expect(sortItems(rows, (r) => r.key, 'desc').map((r) => r.sortTitle)).toEqual(['b', 'c', 'd', 'a'])
  })

  test('version names, total size and the main version of a root folder', () => {
    expect(versionNames(films)).toEqual([
      { name: '1080p', count: 3 },
      { name: '4k', count: 2 },
    ])
    expect(totalSize(films[0])).toBe(110)
    expect(mainVersionIn(films, 2)).toBe('4k')
    expect(mainVersionIn(films, 99)).toBeUndefined()
  })

  test('version conditions read the status, the cutoff and the quality list', () => {
    const v = films[2].versions[0]
    expect(versionConditionsMatch(v, { ...emptyConditions, status: 'cutoffUnmet' })).toBe(true)
    expect(versionConditionsMatch(v, { ...emptyConditions, status: 'noFile' })).toBe(false)
    expect(versionConditionsMatch(films[1].versions[0], { ...emptyConditions, qualities: ['Bluray-1080p'] })).toBe(false)
    expect(versionConditionsMatch(shows[2].versions[0], { ...emptyConditions, qualities: ['HDTV-720p'] }, (x) => (x as (typeof shows)[number]['versions'][number]).qualities)).toBe(true)
  })
})

// ---- the film library keeps its behaviour --------------------------------------

describe('film library view', () => {
  test('presets and their counts', () => {
    const counts = Object.fromEntries(presets(versionNames(films)).map((p) => [p.id, films.filter((m) => matchesFilter(m, p.filter, now)).length]))
    expect(counts).toEqual({ all: 3, missing: 1, 'missing-4k': 0, cutoffUnmet: 1, recent: 3, unmonitored: 0, neverPlayed: 3 })
  })

  test('a named version scope counts films without it as having no file for it', () => {
    const no4k = { ...emptyFilter, scope: 'version:4k' as const, status: 'noFile' as const }
    expect(titles(films.filter((m) => matchesFilter(m, no4k, now)))).toEqual(['Brazil', 'Cube'])
  })

  test('sorts by total size, by one version size, and the storage bar view', () => {
    expect(titles(sortMovies(films, 'size', 'desc', new Map()))).toEqual(['Alien', 'Cube', 'Brazil'])
    expect(titles(sortMovies(films, 'size:4k', 'asc', new Map()))).toEqual(['Alien', 'Brazil', 'Cube'])
    expect(rootFolderView(films, 2)).toEqual({ filter: { ...emptyFilter, status: 'available', rootFolders: [2] }, sort: 'size:4k', direction: 'desc' })
  })
})

// ---- the series library ---------------------------------------------------------

describe('series library view', () => {
  test('rows carry the size, the qualities and the episode statistics of each version', () => {
    const andor = shows[0]
    expect(andor.versions.map((v) => [v.name, v.size, v.quality])).toEqual([
      ['1080p', 100, 'WEBDL-1080p'],
      ['4k', 400, 'WEBDL-2160p'],
    ])
    expect(shows[1].versions[0].size).toBeNull()
    expect(shows[2].versions[0].quality).toBe('HDTV-720p · WEBDL-1080p')
    expect(progressOf(andor.versions[1])).toBe(0.4)
    expect(progressOf(shows[1].versions[0])).toBe(-1)
  })

  test('search matches the title, the network or the year', () => {
    expect(titles(shows.filter((s) => seriesMatchesSearch(s, 'sky')))).toEqual(['Chernobyl'])
    expect(titles(shows.filter((s) => seriesMatchesSearch(s, '2012')))).toEqual(['Bluey'])
  })

  test('series-level and version-level filters', () => {
    const run = (patch: Partial<typeof emptySeriesFilter>) => titles(shows.filter((s) => seriesMatchesFilter(s, { ...emptySeriesFilter, ...patch }, now)))
    expect(run({ seriesStatuses: ['ended', 'upcoming'] })).toEqual(['Bluey', 'Chernobyl'])
    expect(run({ seriesTypes: ['anime'] })).toEqual(['Chernobyl'])
    expect(run({ networks: [''] })).toEqual(['Bluey'])
    expect(run({ status: 'missing' })).toEqual(['Andor'])
    expect(run({ status: 'missing', scope: 'default' })).toEqual([])
    expect(run({ status: 'cutoffUnmet' })).toEqual(['Chernobyl'])
    expect(run({ qualities: ['HDTV-720p'] })).toEqual(['Chernobyl'])
    expect(run({ rootFolders: [6] })).toEqual(['Andor'])
    expect(run({ scope: 'version:4k', status: 'noFile' })).toEqual(['Andor', 'Bluey', 'Chernobyl'])
    expect(run({ hasVersions: ['4k'] })).toEqual(['Andor'])
    expect(run({ lacksVersions: ['4k'] })).toEqual(['Bluey', 'Chernobyl'])
  })

  test('presets: the film ones, plus continuing (next airing first) and ended', () => {
    const list = seriesPresets(versionNames(shows))
    expect(list.map((p) => p.id)).toEqual(['all', 'missing', 'missing-4k', 'cutoffUnmet', 'recent', 'unmonitored', 'continuing', 'ended'])
    const preset = (id: string) => {
      const found = list.find((p) => p.id === id)
      if (!found) throw new Error(`no preset ${id}`)
      return found
    }
    const count = (id: string) => shows.filter((s) => seriesMatchesFilter(s, preset(id).filter, now)).length
    expect([count('missing'), count('missing-4k'), count('cutoffUnmet'), count('continuing'), count('ended')]).toEqual([0, 1, 1, 1, 1])
    expect(preset('continuing').sort).toEqual({ key: 'nextAiring', direction: 'asc' })
    expect(sameSeriesFilter({ ...emptySeriesFilter, scope: 'all' }, emptySeriesFilter)).toBe(true)
  })

  test('sorts: next airing soonest first with the unknown last, progress, per-version size, network', () => {
    expect(titles(sortSeries(shows, 'nextAiring', 'asc', new Map()))).toEqual(['Bluey', 'Andor', 'Chernobyl'])
    expect(titles(sortSeries(shows, 'nextAiring', 'desc', new Map()))).toEqual(['Andor', 'Bluey', 'Chernobyl'])
    expect(titles(sortSeries(shows, 'progress', 'desc', new Map()))).toEqual(['Andor', 'Chernobyl', 'Bluey'])
    expect(titles(sortSeries(shows, 'progress:4k', 'desc', new Map()))).toEqual(['Andor', 'Bluey', 'Chernobyl'])
    expect(titles(sortSeries(shows, 'size', 'desc', new Map()))).toEqual(['Andor', 'Chernobyl', 'Bluey'])
    expect(titles(sortSeries(shows, 'network', 'asc', new Map()))).toEqual(['Andor', 'Chernobyl', 'Bluey'])
    const rank = new Map([
      ['HDTV-720p', 0],
      ['WEBDL-1080p', 1],
      ['WEBDL-2160p', 2],
    ])
    expect(titles(sortSeries(shows, 'quality', 'desc', rank))).toEqual(['Andor', 'Chernobyl', 'Bluey'])
  })

  test('the storage bar opens a series root folder on its files, biggest version first', () => {
    expect(seriesRootFolderView(shows, 6)).toEqual({ filter: { ...emptySeriesFilter, status: 'available', rootFolders: [6] }, sort: 'size:4k', direction: 'desc' })
  })
})
