import type { KeyValueStore } from '@/features/search/searchQueries'

/**
 * The indexers left out of the interactive search, for this browser tab only (sessionStorage): the pick is for the
 * searches at hand, a new tab starts with every indexer again. The ids left out are kept rather than those picked, so an
 * indexer added since is searched.
 */
export const excludedIndexersKey = 'bigarr.search.excludedIndexers'

function sessionStore(): KeyValueStore | null {
  try {
    return globalThis.sessionStorage ?? null
  } catch {
    return null
  }
}

export function readExcluded(store: KeyValueStore | null = sessionStore()): number[] {
  try {
    const raw = store?.getItem(excludedIndexersKey)
    const parsed: unknown = raw ? JSON.parse(raw) : []
    return Array.isArray(parsed) ? parsed.filter((id): id is number => Number.isInteger(id)) : []
  } catch {
    return []
  }
}

export function writeExcluded(ids: number[], store: KeyValueStore | null = sessionStore()): void {
  try {
    store?.setItem(excludedIndexersKey, JSON.stringify([...new Set(ids)].sort((a, b) => a - b)))
  } catch {
    // Not remembered: the pick still applies on this page.
  }
}

/**
 * What the search sends: undefined when every searchable indexer is in (the server then asks them all, including one
 * added meanwhile), else the ids picked. Ids left out that no longer exist are ignored.
 */
export function pickedIndexerIds(searchable: { id: number }[], excluded: number[]): number[] | undefined {
  const out = new Set(excluded)
  const picked = searchable.filter((i) => !out.has(i.id)).map((i) => i.id)
  return picked.length === searchable.length ? undefined : picked
}

/** A stable key for a pick, for query keys and "was this already searched": `all` or the sorted ids. */
export const pickKey = (ids: number[] | undefined) => (ids === undefined ? 'all' : [...ids].sort((a, b) => a - b).join(','))
