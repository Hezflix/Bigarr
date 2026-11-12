import { describe, expect, test } from 'bun:test'
import type { SureMatch } from '../src/api/client'
import { allChecked, checkedPairs } from '../src/features/playback/sureMatches'

const match = (itemId: number, movieId: number): SureMatch => ({
  item: { id: itemId, title: `Item ${itemId}`, year: 2001, plays: 1 },
  film: { movieId, title: `Film ${movieId}`, year: 2001, posterUrl: null, versions: 1, score: 90, reasons: ['tmdbSearch'] },
})

const matches = [match(1, 10), match(2, 20), match(3, 10)]

describe('checkedPairs', () => {
  test('every match is linked while none is unchecked', () => {
    expect(checkedPairs(matches, new Set())).toEqual([
      { itemId: 1, movieId: 10 },
      { itemId: 2, movieId: 20 },
      { itemId: 3, movieId: 10 },
    ])
  })

  test('an unchecked item is left out, in the list order', () => {
    expect(checkedPairs(matches, new Set([2]))).toEqual([
      { itemId: 1, movieId: 10 },
      { itemId: 3, movieId: 10 },
    ])
  })
})

describe('allChecked', () => {
  test('all, some or none', () => {
    expect(allChecked(matches, new Set())).toBe(true)
    expect(allChecked(matches, new Set([1]))).toBe('indeterminate')
    expect(allChecked(matches, new Set([1, 2, 3]))).toBe(false)
  })
})
