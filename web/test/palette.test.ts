import { describe, expect, test } from 'bun:test'
import type { TFunction } from 'i18next'
import type { Episode } from '../src/api/client'
import { contentScore, contentTier, paletteFilter } from '../src/features/palette/filter'
import { isPaletteShortcut, isSearchShortcut } from '../src/features/palette/keys'
import { parseRecent, pushRecent, type RecentItem } from '../src/features/palette/recent'
import { parseSeriesScope, seriesScopeOptions } from '../src/features/palette/seriesScopes'

const page = (to: string): RecentItem => ({ kind: 'page', to })
const movie = (id: number, title = `Film ${id}`): RecentItem => ({ kind: 'movie', id, title, year: 2000 + id })

describe('pushRecent', () => {
  test('puts the item first and drops its older copy', () => {
    expect(pushRecent([page('/a'), movie(1), page('/b')], movie(1))).toEqual([movie(1), page('/a'), page('/b')])
  })

  test('a film and a series with the same id are different items', () => {
    const series: RecentItem = { kind: 'series', id: 1, title: 'Show' }
    expect(pushRecent([movie(1)], series)).toEqual([series, movie(1)])
  })

  test('keeps the newest copy of a renamed film', () => {
    expect(pushRecent([movie(1, 'Old')], movie(1, 'New'))).toEqual([movie(1, 'New')])
  })

  test('cuts the list to the limit', () => {
    const list = [page('/a'), page('/b'), page('/c')]
    expect(pushRecent(list, page('/d'), 3)).toEqual([page('/d'), page('/a'), page('/b')])
  })
})

describe('parseRecent', () => {
  test('reads back what was stored', () => {
    const list = [page('/a'), movie(2)]
    expect(parseRecent(JSON.stringify(list))).toEqual(list)
  })

  test('drops what no longer has the shape of an item', () => {
    const raw = JSON.stringify([page('/a'), { kind: 'page', to: 'relative' }, { kind: 'movie', id: '3' }, { kind: 'other' }, null, 4])
    expect(parseRecent(raw)).toEqual([page('/a')])
  })

  test('anything but a JSON list is an empty list', () => {
    expect(parseRecent(null)).toEqual([])
    expect(parseRecent('not json')).toEqual([])
    expect(parseRecent('{"kind":"page"}')).toEqual([])
  })
})

describe('shortcuts', () => {
  const key = (key: string, mods: Partial<Record<'metaKey' | 'ctrlKey' | 'altKey' | 'shiftKey', boolean>> = {}) => ({
    key,
    metaKey: false,
    ctrlKey: false,
    altKey: false,
    shiftKey: false,
    ...mods,
  })

  test('Cmd+K and Ctrl+K open the palette, on every platform', () => {
    expect(isPaletteShortcut(key('k', { metaKey: true }))).toBe(true)
    expect(isPaletteShortcut(key('k', { ctrlKey: true }))).toBe(true)
    expect(isPaletteShortcut(key('K', { ctrlKey: true }))).toBe(true)
  })

  test('K alone or with another modifier does not', () => {
    expect(isPaletteShortcut(key('k'))).toBe(false)
    expect(isPaletteShortcut(key('k', { metaKey: true, shiftKey: true }))).toBe(false)
    expect(isPaletteShortcut(key('k', { ctrlKey: true, altKey: true }))).toBe(false)
  })

  test('"/" outside a field is the search shortcut', () => {
    expect(isSearchShortcut(key('/'), null)).toBe(true)
    expect(isSearchShortcut(key('/', { ctrlKey: true }), null)).toBe(false)
    expect(isSearchShortcut(key('?'), null)).toBe(false)
  })
})

describe('paletteFilter', () => {
  test('every typed word must start a word of the entry', () => {
    expect(paletteFilter('Settings Indexers /settings/indexers', 'settings ind')).toBeGreaterThan(0)
    expect(paletteFilter('Settings Indexers /settings/indexers', 'ind set')).toBeGreaterThan(0)
    expect(paletteFilter('Activity History /activity/history', 'chihiro')).toBe(0)
    expect(paletteFilter('Activity Blocklist /activity/blocklist', 'bla')).toBe(0)
  })

  test('accents and case do not count, keywords count', () => {
    expect(paletteFilter('Settings Media servers /settings/mediaservers', 'MÉDIA')).toBeGreaterThan(0)
    expect(paletteFilter('action:rss', 'sync', ['RSS sync now'])).toBeGreaterThan(0)
  })

  test('server rows always pass', () => {
    expect(paletteFilter('content:movie:3', 'chihiro')).toBe(1)
    expect(paletteFilter('add:series', 'anything')).toBeGreaterThan(0)
    expect(paletteFilter('add:series', 'sync')).toBeLessThan(paletteFilter('Activity Releases /activity/releases', 'rel'))
  })
})

describe('series scopes (Tab on a series search)', () => {
  const t = ((key: string, values?: Record<string, unknown>) => `${key}${values ? ` ${JSON.stringify(values)}` : ''}`) as unknown as TFunction
  const ep = (id: number, season: number, number: number, title: string, patch: Partial<Episode> = {}) =>
    ({ id, seasonNumber: season, episodeNumber: number, title, airDate: null, absoluteEpisodeNumber: null, ownedByFilm: null, ...patch }) as unknown as Episode
  const episodes = [ep(3, 2, 1, 'Third'), ep(1, 1, 1, 'Pilot'), ep(2, 1, 2, 'Second'), ep(9, 0, 1, 'A special'), ep(10, 0, 2, 'Owned', { ownedByFilm: { movieId: 1 } as Episode['ownedByFilm'] })]
  const options = seriesScopeOptions(t, episodes, false)
  const find = (search: string) => options.filter((o) => paletteFilter(`param:${o.id}`, search, [o.label, ...(o.keywords ?? [])]) > 0).map((o) => o.id)

  test('lists the series, the seasons with specials last, then the episodes in order, without those a film owns', () => {
    expect(options.map((o) => o.id)).toEqual(['series', 'season:1', 'season:2', 'season:0', 'episode:9', 'episode:1', 'episode:2', 'episode:3'])
    expect(options.filter((o) => o.onlyWhenTyping).map((o) => o.group)).toEqual(Array(4).fill('palette.param.episodesGroup'))
  })

  test('finds an episode by code in any spelling, or by a word of its title', () => {
    for (const search of ['s1e2', 's01e02', 'S1E02', '1x02', '1x2']) expect(find(search)).toEqual(['episode:2'])
    expect(find('pilot')).toEqual(['episode:1'])
  })

  test('"s2" finds the season and its episodes; the id never matches', () => {
    expect(find('s2')).toEqual(['season:2', 'episode:3'])
    expect(find('3')).toEqual([])
  })

  test('maps an id back to its scope', () => {
    expect(parseSeriesScope('series', episodes, false)).toEqual({ kind: 'series' })
    expect(parseSeriesScope('season:1', episodes, false)).toEqual({ kind: 'season', season: 1, orderSeason: false, episodeIds: [1, 2] })
    expect(parseSeriesScope('season:0', episodes, false)).toEqual({ kind: 'season', season: 0, orderSeason: false, episodeIds: [9] })
    expect(parseSeriesScope('episode:2', episodes, false)).toEqual({ kind: 'episode', episode: episodes[2] })
    expect(parseSeriesScope('episode:404', episodes, false)).toBeNull()
  })

  describe('a show on another order', () => {
    // TVDB S01E01-S01E03 become the order's S01E01, S02E01 and S02E02; TVDB S02E01 has no counterpart.
    const ordered = [
      ep(1, 1, 1, 'Pilot', { displaySeasonNumber: 1, displayEpisodeNumber: 1 }),
      ep(2, 1, 2, 'Second', { displaySeasonNumber: 2, displayEpisodeNumber: 1 }),
      ep(3, 1, 3, 'Third', { displaySeasonNumber: 2, displayEpisodeNumber: 2 }),
      ep(4, 2, 1, 'Extra', { notInOrder: true }),
    ]
    const alt = seriesScopeOptions(t, ordered, true)
    const findAlt = (search: string) => alt.filter((o) => paletteFilter(`param:${o.id}`, search, [o.label, ...(o.keywords ?? [])]) > 0).map((o) => o.id)

    test("lists the order's seasons and codes, those it leaves out last", () => {
      expect(alt.map((o) => o.id)).toEqual(['series', 'season:1', 'season:2', 'episode:1', 'episode:2', 'episode:3', 'episode:4'])
      expect(alt.find((o) => o.id === 'episode:3')?.label).toBe('S02E02 · Third')
      expect(alt.find((o) => o.id === 'episode:4')?.label).toBe('S02E01 · Extra')
    })

    test("types the order's codes, not TVDB's", () => {
      expect(findAlt('2x02')).toEqual(['episode:3'])
      expect(findAlt('s1e3')).toEqual([])
    })

    test("a season is the order's, with its episodes", () => {
      expect(parseSeriesScope('season:2', ordered, true)).toEqual({ kind: 'season', season: 2, orderSeason: true, episodeIds: [2, 3] })
    })
  })
})

describe('ranking pages and actions against library hits', () => {
  const discover = (search: string) => paletteFilter('Requests Discover /discover', search, ['Discover'])
  const hit = (tier: string) => paletteFilter(`content:movie:1:${tier}`, 'discover')

  test('a page named like the term ranks above library hits that start with it or only hold its words', () => {
    expect(discover('discover')).toBeGreaterThan(hit('prefix'))
    expect(discover('discover')).toBeGreaterThan(hit('words'))
    expect(discover('discover')).toBeGreaterThan(hit('typo'))
  })

  test('a page whose name starts with the term ranks above loose and typo hits, below exact and prefix titles', () => {
    const score = discover('disc')
    expect(score).toBeGreaterThan(hit('words'))
    expect(score).toBeGreaterThan(hit('typo'))
    expect(score).toBeLessThan(hit('prefix'))
  })

  test('a film titled exactly like the term still comes first', () => {
    expect(hit('exact')).toBeGreaterThan(discover('discover'))
  })

  test('typo hits rank below a page that merely matches', () => {
    expect(paletteFilter('Settings Library /settings/library', 'library', ['Library'])).toBeGreaterThan(hit('typo'))
    expect(paletteFilter('Settings Indexers /settings/indexers', 'discover', ['Indexers'])).toBe(0)
  })

  test('"Add" stays last and an action is ranked by its label', () => {
    expect(paletteFilter('add:movie', 'discover')).toBeLessThan(0.01)
    expect(paletteFilter('action:search-missing', 'search missing', ['Search missing movies'])).toBeGreaterThan(1)
    expect(paletteFilter('action:search-missing', 'missing', ['Search missing movies'])).toBeLessThan(1)
  })
})

describe('contentTier', () => {
  test('the whole title, with or without its article, is exact', () => {
    expect(contentTier('frost queen', 'The Frost Queen')).toBe('exact')
    expect(contentTier('the matrix', 'Matrix')).toBe('exact')
    expect(contentTier('Amelie', 'Amélie')).toBe('exact')
  })

  test('a title that starts with the term, word for word, is a prefix hit', () => {
    expect(contentTier('frost', 'The Frost Queen 2')).toBe('prefix')
    expect(contentTier('discover', 'Discover Weekly')).toBe('prefix')
  })

  test('words that start words of the title anywhere are a loose hit', () => {
    expect(contentTier('discover', 'The Discoverers')).toBe('words')
    expect(contentTier('queen frost', 'The Frost Queen')).toBe('words')
    expect(contentTier('frost qu', 'The Frost Queen')).toBe('words')
  })

  test('what only a forgiven slip found is a typo hit', () => {
    expect(contentTier('reine des glacs', 'La Reine des glaces')).toBe('typo')
    expect(contentTier('discovr', 'Discovery Road')).toBe('typo')
    expect(contentTier('discover', 'Dis-Cover Girl')).toBe('typo')
  })

  test('a trailing year is not a word of the title', () => {
    expect(contentTier('alien 1979', 'Alien')).toBe('exact')
  })

  test('scores order the tiers', () => {
    expect(contentScore('exact')).toBeGreaterThan(contentScore('prefix'))
    expect(contentScore('prefix')).toBeGreaterThan(contentScore('words'))
    expect(contentScore('words')).toBeGreaterThan(contentScore('typo'))
  })
})
