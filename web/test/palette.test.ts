import { describe, expect, test } from 'bun:test'
import { paletteFilter } from '../src/features/palette/filter'
import { isPaletteShortcut, isSearchShortcut } from '../src/features/palette/keys'
import { parseRecent, pushRecent, type RecentItem } from '../src/features/palette/recent'

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
})
