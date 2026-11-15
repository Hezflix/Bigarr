import { useCallback, useEffect, useSyncExternalStore } from 'react'

/** Something the palette lists under "Recent" when its input is empty: a page of the nav, a film or a series. */
export type RecentItem =
  | { kind: 'page'; to: string }
  | { kind: 'movie'; id: number; title: string; year?: number | null }
  | { kind: 'series'; id: number; title: string; year?: number | null }

export const recentLimit = 8
const storageKey = 'bigarr.palette.recent'

export const recentKey = (item: RecentItem) => (item.kind === 'page' ? `page:${item.to}` : `${item.kind}:${item.id}`)

/** `item` first, its older copy dropped, the list cut to `limit`. */
export function pushRecent(list: readonly RecentItem[], item: RecentItem, limit = recentLimit): RecentItem[] {
  const key = recentKey(item)
  return [item, ...list.filter((other) => recentKey(other) !== key)].slice(0, limit)
}

/** Whatever storage holds, kept only where it still has the shape of a recent item. */
export function parseRecent(raw: string | null): RecentItem[] {
  if (!raw) return []
  try {
    const value: unknown = JSON.parse(raw)
    if (!Array.isArray(value)) return []
    return value.filter(isRecentItem).slice(0, recentLimit)
  } catch {
    return []
  }
}

function isRecentItem(value: unknown): value is RecentItem {
  if (typeof value !== 'object' || value === null) return false
  const item = value as Record<string, unknown>
  if (item.kind === 'page') return typeof item.to === 'string' && item.to.startsWith('/')
  if (item.kind === 'movie' || item.kind === 'series') return typeof item.id === 'number' && typeof item.title === 'string'
  return false
}

// One copy in memory, shared by every reader; storage is only the persistence behind it.
let current: RecentItem[] | null = null
const listeners = new Set<() => void>()

function read(): RecentItem[] {
  if (current === null) {
    try {
      current = parseRecent(localStorage.getItem(storageKey))
    } catch {
      current = [] // storage can be unavailable (private mode, blocked site data)
    }
  }
  return current
}

function write(next: RecentItem[]) {
  current = next
  try {
    localStorage.setItem(storageKey, JSON.stringify(next))
  } catch {
    /* kept in memory for this tab */
  }
  for (const listener of listeners) listener()
}

export function recordRecent(item: RecentItem) {
  const list = read()
  const next = pushRecent(list, item)
  // Same order and same content: nothing to tell the readers.
  if (JSON.stringify(next) === JSON.stringify(list)) return
  write(next)
}

function subscribe(listener: () => void) {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

export function useRecent(): [RecentItem[], () => void] {
  const items = useSyncExternalStore(subscribe, read, read)
  const clear = useCallback(() => write([]), [])
  return [items, clear]
}

/** A detail page puts its film or series first under "Recent" once it has loaded. */
export function useRecordRecent(item: RecentItem | null) {
  // Keyed on the content: the page re-renders with a new object on every poll.
  const key = item ? JSON.stringify(item) : null
  useEffect(() => {
    if (key) recordRecent(JSON.parse(key) as RecentItem)
  }, [key])
}
