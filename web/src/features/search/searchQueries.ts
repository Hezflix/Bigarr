import type { IndexerQueryReport, IndexerQueryRequest } from '@/api/client'

/**
 * The viewer's choice, per browser: the interactive search shows what each indexer was asked ("Show queries"). Off by
 * default. The key predates the toggle's name (it was "Advanced"): kept so the choice already made survives.
 */
export const showQueriesKey = 'bigarr.search.advanced'

export type KeyValueStore = Pick<Storage, 'getItem' | 'setItem'>

/** localStorage, or nothing when the browser refuses it (private mode, blocked site data). */
function browserStore(): KeyValueStore | null {
  try {
    return globalThis.localStorage ?? null
  } catch {
    return null
  }
}

export function readShowQueries(store: KeyValueStore | null = browserStore()): boolean {
  try {
    return store?.getItem(showQueriesKey) === '1'
  } catch {
    return false
  }
}

export function writeShowQueries(on: boolean, store: KeyValueStore | null = browserStore()): void {
  try {
    store?.setItem(showQueriesKey, on ? '1' : '0')
  } catch {
    // Not remembered: the toggle still works for this page.
  }
}

export type RequestState = 'ok' | 'error' | 'skipped'

export const requestState = (request: IndexerQueryRequest): RequestState => (request.skipped ? 'skipped' : request.errorKind || request.error ? 'error' : 'ok')

/** The parameters as a short line: `tvdbid=81189 season=1`, categories left to the details. */
export const parameterLine = (request: IndexerQueryRequest) =>
  Object.entries(request.parameters)
    .filter(([key]) => key !== 'cat')
    .map(([key, value]) => `${key}=${value}`)
    .join(' ')

/** What an indexer's line says when folded: requests sent, failed, skipped, releases returned. */
export function reportTotals(report: IndexerQueryReport) {
  const searches = report.requests.filter((r) => r.function !== 'caps')
  return {
    sent: searches.filter((r) => !r.skipped).length,
    failed: searches.filter((r) => requestState(r) === 'error').length,
    skipped: searches.filter((r) => r.skipped).length,
    results: searches.reduce((sum, r) => sum + (r.results ?? 0), 0),
  }
}
