import { describe, expect, test } from 'bun:test'
import { excludedIndexersKey, pickedIndexerIds, pickKey, readExcluded, writeExcluded } from '../src/features/search/searchIndexers'
import type { KeyValueStore } from '../src/features/search/searchQueries'

const memoryStore = (): KeyValueStore & { data: Map<string, string> } => {
  const data = new Map<string, string>()
  return { data, getItem: (key) => data.get(key) ?? null, setItem: (key, value) => void data.set(key, value) }
}

const searchable = [{ id: 3 }, { id: 1 }, { id: 7 }]

describe('indexer pick', () => {
  test('every indexer is searched until one is left out, and then the server is told which ones', () => {
    expect(pickedIndexerIds(searchable, [])).toBeUndefined()
    expect(pickedIndexerIds(searchable, [1])).toEqual([3, 7])
    expect(pickedIndexerIds(searchable, [3, 1, 7])).toEqual([])
  })

  test('an indexer left out that no longer exists changes nothing', () => {
    expect(pickedIndexerIds(searchable, [42])).toBeUndefined()
  })

  test('the pick key ignores order', () => {
    expect(pickKey(undefined)).toBe('all')
    expect(pickKey([7, 3])).toBe(pickKey([3, 7]))
  })

  test('the indexers left out are kept for the tab, sorted and once each', () => {
    const store = memoryStore()
    expect(readExcluded(store)).toEqual([])
    writeExcluded([7, 1, 7], store)
    expect(store.data.get(excludedIndexersKey)).toBe('[1,7]')
    expect(readExcluded(store)).toEqual([1, 7])
  })

  test('a broken or blocked store reads as nothing left out', () => {
    const store = memoryStore()
    store.setItem(excludedIndexersKey, '{nope')
    expect(readExcluded(store)).toEqual([])
    store.setItem(excludedIndexersKey, '["a", 2]')
    expect(readExcluded(store)).toEqual([2])
    const blocked: KeyValueStore = {
      getItem: () => {
        throw new Error('SecurityError')
      },
      setItem: () => {
        throw new Error('QuotaExceededError')
      },
    }
    expect(readExcluded(blocked)).toEqual([])
    expect(() => writeExcluded([1], blocked)).not.toThrow()
  })
})
