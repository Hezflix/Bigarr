import { describe, expect, test } from 'bun:test'
import type { IndexerQueryReport, IndexerQueryRequest } from '../src/api/client'
import { showQueriesKey, type KeyValueStore, parameterLine, readShowQueries, reportTotals, requestState, writeShowQueries } from '../src/features/search/searchQueries'

/** A Storage stand-in: a map, or one that throws like a browser with site data blocked. */
const memoryStore = (): KeyValueStore & { data: Map<string, string> } => {
  const data = new Map<string, string>()
  return {
    data,
    getItem: (key) => data.get(key) ?? null,
    setItem: (key, value) => {
      data.set(key, value)
    },
  }
}

const blockedStore: KeyValueStore = {
  getItem: () => {
    throw new Error('SecurityError')
  },
  setItem: () => {
    throw new Error('QuotaExceededError')
  },
}

describe('show queries toggle', () => {
  test('is off by default', () => {
    expect(readShowQueries(memoryStore())).toBe(false)
    expect(readShowQueries(null)).toBe(false)
  })

  test('is remembered per viewer, on and off', () => {
    const store = memoryStore()
    writeShowQueries(true, store)
    expect(store.data.get(showQueriesKey)).toBe('1')
    expect(readShowQueries(store)).toBe(true)

    writeShowQueries(false, store)
    expect(readShowQueries(store)).toBe(false)
  })

  test('a storage that throws reads as off and never breaks the toggle', () => {
    expect(readShowQueries(blockedStore)).toBe(false)
    expect(() => writeShowQueries(true, blockedStore)).not.toThrow()
    expect(() => writeShowQueries(true, null)).not.toThrow()
  })

  test('works without a browser storage at all', () => {
    expect(() => writeShowQueries(true)).not.toThrow()
    expect(typeof readShowQueries()).toBe('boolean')
  })
})

const request = (patch: Partial<IndexerQueryRequest>): IndexerQueryRequest => ({
  step: 'IDs',
  function: 'tvsearch',
  parameters: { tvdbid: '81189', season: '1', cat: '5000,5030,5040' },
  url: 'mock://indexer/api?t=tvsearch&tvdbid=81189&season=1&apikey=•••&cat=5000,5030,5040&limit=100&extended=1',
  statusCode: 200,
  durationMs: 12,
  waitedMs: 0,
  results: 7,
  errorKind: null,
  error: null,
  skipped: false,
  ...patch,
})

describe('queries panel', () => {
  test('a request line shows its parameters, categories aside', () => {
    expect(parameterLine(request({}))).toBe('tvdbid=81189 season=1')
  })

  test('each request says whether it went, failed or was skipped', () => {
    expect(requestState(request({}))).toBe('ok')
    expect(requestState(request({ statusCode: 500, results: null, errorKind: 'serverError', error: 'HTTP 500: the indexer failed.' }))).toBe('error')
    expect(requestState(request({ skipped: true, url: null, errorKind: 'disabled', error: 'paused' }))).toBe('skipped')
  })

  test('an indexer folds into requests sent, failed, skipped and results, capabilities aside', () => {
    const report: IndexerQueryReport = {
      indexerId: 1,
      indexer: 'Demo indexer',
      requests: [
        request({ step: 'Capabilities', function: 'caps', parameters: {}, results: null }),
        request({ results: 0 }),
        request({ step: 'Fallback: Title', parameters: { q: 'Demo', season: '1' }, results: 4 }),
        request({ step: 'Fallback: Title', results: null, errorKind: 'unreachable', error: 'timeout' }),
      ],
      releases: 4,
      parsed: 4,
      matched: 3,
      accepted: 2,
    }
    expect(reportTotals(report)).toEqual({ sent: 3, failed: 1, skipped: 0, results: 4 })
  })
})
