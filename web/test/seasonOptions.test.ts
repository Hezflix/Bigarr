import { describe, expect, test } from 'bun:test'
import { seasonOptions } from '../src/components/seasonOptions'

describe('season options', () => {
  test('list the seasons in order with the specials last', () => {
    expect(seasonOptions([0, 1, 2, 3])).toEqual([1, 2, 3, 0])
  })

  test('sort the seasons whatever order they come in', () => {
    expect(seasonOptions([3, 0, 10, 2, 1])).toEqual([1, 2, 3, 10, 0])
  })

  test('list a season once when several episodes carry it', () => {
    expect(seasonOptions([1, 1, 2, 0, 2, 0])).toEqual([1, 2, 0])
  })

  test('keep a show without specials as it is', () => {
    expect(seasonOptions([2, 1])).toEqual([1, 2])
  })

  test('do not reorder the list they are given', () => {
    const seasons = [0, 2, 1]
    seasonOptions(seasons)
    expect(seasons).toEqual([0, 2, 1])
  })
})
