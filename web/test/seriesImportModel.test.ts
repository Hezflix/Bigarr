import { describe, expect, test } from 'bun:test'
import { moveToSeason, mostCommon, range, renumber, sharedSeries } from '../src/features/import/seriesImportModel'

// Season 1: E01–E03 (ids 11–13); season 2: E01–E10 (ids 201–210).
const episodes = [
  ...[1, 2, 3].map((n) => ({ id: 10 + n, seasonNumber: 1, episodeNumber: n })),
  ...Array.from({ length: 10 }, (_, i) => ({ id: 201 + i, seasonNumber: 2, episodeNumber: i + 1 })),
]

describe('mostCommon', () => {
  test('the most frequent value, nulls ignored, first seen on a tie', () => {
    expect(mostCommon([3, null, 5, 5, 3, 5])).toBe(5)
    expect(mostCommon([3, 5])).toBe(3)
    expect(mostCommon([null, null])).toBeNull()
  })
})

describe('sharedSeries', () => {
  test('rows without a series count as the fallback', () => {
    expect(sharedSeries([{ seriesId: 1 }, { seriesId: null }], 1)).toBe(1)
    expect(sharedSeries([{ seriesId: 1 }, { seriesId: 2 }], 1)).toBeNull()
    expect(sharedSeries([{ seriesId: null }], null)).toBeNull()
  })
})

describe('renumber', () => {
  const files = [
    { path: '/d/bb.210.mkv', name: 'bb.210.mkv' },
    { path: '/d/bb.209.mkv', name: 'bb.209.mkv' },
  ]

  test('name order, one episode each from the start', () => {
    expect(renumber(files, episodes, 2, 9)).toEqual([
      { path: '/d/bb.209.mkv', episodeId: 209, seasonNumber: 2, episodeNumber: 9 },
      { path: '/d/bb.210.mkv', episodeId: 210, seasonNumber: 2, episodeNumber: 10 },
    ])
  })

  test('numbers in names sort as numbers', () => {
    const named = ['ep10.mkv', 'ep9.mkv', 'ep1.mkv'].map((n) => ({ path: n, name: n }))
    expect(renumber(named, episodes, 2, 1).map((r) => [r.path, r.episodeNumber])).toEqual([
      ['ep1.mkv', 1],
      ['ep9.mkv', 2],
      ['ep10.mkv', 3],
    ])
  })

  test('past the end of the season there is no episode', () => {
    expect(renumber(files, episodes, 1, 3).map((r) => r.episodeId)).toEqual([13, null])
  })
})

describe('moveToSeason', () => {
  test('same numbers in the other season', () => {
    expect(moveToSeason([11, 12], episodes, 2)).toEqual([201, 202])
  })

  test('numbers the season lacks are dropped, none left is null', () => {
    expect(moveToSeason([203, 210], episodes, 1)).toEqual([13])
    expect(moveToSeason([210], episodes, 1)).toBeNull()
    expect(moveToSeason([], episodes, 1)).toBeNull()
  })
})

describe('range', () => {
  const paths = ['a', 'b', 'c', 'd']
  test('both ends included, either direction', () => {
    expect(range(paths, 'b', 'd')).toEqual(['b', 'c', 'd'])
    expect(range(paths, 'd', 'b')).toEqual(['b', 'c', 'd'])
    expect(range(paths, 'x', 'c')).toEqual(['c'])
  })
})
