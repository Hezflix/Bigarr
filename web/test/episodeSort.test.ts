import { describe, expect, test } from 'bun:test'
import type { Episode, EpisodeFile } from '../src/api/client'
import { defaultEpisodeSort, nextEpisodeSort, sortEpisodes } from '../src/features/series/episodeSort'
import { readSeasonTablePrefs } from '../src/features/series/seasonTablePrefs'

const episode = (id: number, title: string, airDate: string | null, absolute: number | null = null) =>
  ({ id, title, airDate, airDateUtc: airDate ? `${airDate}T01:00:00Z` : null, displayAbsoluteNumber: absolute }) as unknown as Episode

const season = [episode(1, 'Pilot', '2008-01-20', 1), episode(2, "Cat's in the Bag", '2008-01-27', 2), episode(3, 'And the Bag', null, 3), episode(4, 'Breakage', '2008-01-25', null)]
const sizes: Record<number, number> = { 1: 900, 2: 1500, 4: 1200 }
const fileOf = (e: Episode) => (sizes[e.id] ? ({ size: sizes[e.id] } as EpisodeFile) : undefined)
const ids = (list: Episode[]) => list.map((e) => e.id)

describe('sortEpisodes', () => {
  test('by number keeps the season order, or reverses it', () => {
    expect(ids(sortEpisodes(season, defaultEpisodeSort, fileOf))).toEqual([1, 2, 3, 4])
    expect(ids(sortEpisodes(season, { key: 'number', direction: 'desc' }, fileOf))).toEqual([4, 3, 2, 1])
  })

  test('by title, natural order', () => {
    expect(ids(sortEpisodes(season, { key: 'title', direction: 'asc' }, fileOf))).toEqual([3, 4, 2, 1])
  })

  test('an episode without an air date goes last both ways', () => {
    expect(ids(sortEpisodes(season, { key: 'airDate', direction: 'asc' }, fileOf))).toEqual([1, 4, 2, 3])
    expect(ids(sortEpisodes(season, { key: 'airDate', direction: 'desc' }, fileOf))).toEqual([2, 4, 1, 3])
  })

  test('by size, episodes without a file last', () => {
    expect(ids(sortEpisodes(season, { key: 'size', direction: 'desc' }, fileOf))).toEqual([2, 4, 1, 3])
  })

  test('by absolute number, none last', () => {
    expect(ids(sortEpisodes(season, { key: 'absolute', direction: 'desc' }, fileOf))).toEqual([3, 2, 1, 4])
  })
})

describe('nextEpisodeSort', () => {
  test('a second click reverses, a new column starts in its own direction', () => {
    expect(nextEpisodeSort(defaultEpisodeSort, 'number')).toEqual({ key: 'number', direction: 'desc' })
    expect(nextEpisodeSort(defaultEpisodeSort, 'size')).toEqual({ key: 'size', direction: 'desc' })
    expect(nextEpisodeSort(defaultEpisodeSort, 'title')).toEqual({ key: 'title', direction: 'asc' })
  })
})

describe('readSeasonTablePrefs', () => {
  test('keeps a known sort, drops an unknown one', () => {
    expect(readSeasonTablePrefs({ sort: { key: 'airDate', direction: 'desc' } }).sort).toEqual({ key: 'airDate', direction: 'desc' })
    expect(readSeasonTablePrefs({ sort: { key: 'gone', direction: 'desc' } }).sort).toEqual(defaultEpisodeSort)
    expect(readSeasonTablePrefs({ sort: { key: 'title', direction: 'sideways' } }).sort).toEqual(defaultEpisodeSort)
    expect(readSeasonTablePrefs(null).sort).toEqual(defaultEpisodeSort)
  })
})
