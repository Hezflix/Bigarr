import { useCallback, useState } from 'react'
import type { VersionSelector, VersionStatus } from '@/api/client'

/**
 * What the film and the series libraries share. A library item is a film or a series as the list shows it, each with
 * versions (`1080p`, `4k`…) that carry the status, file and settings the views filter, sort and colour on. The film
 * summary fits as it comes; the series page maps its summary onto it once (`seriesView.ts`).
 */
export type LibraryVersion = {
  id: number
  name: string
  isDefault: boolean
  monitored: boolean
  qualityProfileId: number
  status: VersionStatus
  /** The quality of the file, or of the files ("WEBDL-1080p · HDTV-720p"). */
  quality: string | null
  rootFolderId: number
  tags: number[]
  /** Bytes on disk; null without a file. */
  size: number | null
  /** Null without a file; false when a file is below the profile cutoff. */
  cutoffMet: boolean | null
}

export type LibraryItem = {
  id: number
  title: string
  sortTitle: string
  year: number | null
  posterUrl: string | null
  addedAt: string
  versions: LibraryVersion[]
}

// ---- layout ----------------------------------------------------------------

export const viewModes = ['posters', 'table'] as const
export type ViewMode = (typeof viewModes)[number]

/** A stored or linked layout. The version matrix is gone: what pointed at it (`matrix`) opens the table. */
export function viewMode(value: unknown): ViewMode {
  return viewModes.find((mode) => mode === value) ?? 'table'
}

export const posterSizes = ['small', 'medium', 'large'] as const
export type PosterSize = (typeof posterSizes)[number]

export const densities = ['compact', 'comfortable'] as const
export type Density = (typeof densities)[number]

export type SortDirection = 'asc' | 'desc'

// ---- versions --------------------------------------------------------------

export type VersionName = { name: string; count: number }

/** Every version name of the library, the most common first: usually the default flavour, then `4k`… */
export function versionNames(items: LibraryItem[]): VersionName[] {
  const counts = new Map<string, number>()
  for (const item of items) {
    for (const v of item.versions) counts.set(v.name, (counts.get(v.name) ?? 0) + 1)
  }
  return [...counts].map(([name, count]) => ({ name, count })).sort((a, b) => b.count - a.count || a.name.localeCompare(b.name))
}

/** The versions of the item a bulk action would touch. */
export function targetVersions<V extends LibraryVersion>(item: { versions: V[] }, selector: VersionSelector): V[] {
  if (selector === 'all') return item.versions
  if (selector === 'default') return item.versions.filter((v) => v.isDefault)
  const name = selector.slice('version:'.length)
  return item.versions.filter((v) => v.name === name)
}

export function totalSize(item: { versions: Pick<LibraryVersion, 'size'>[] }): number {
  return item.versions.reduce((sum, v) => sum + (v.size ?? 0), 0)
}

// ---- filtering -------------------------------------------------------------

/** Which versions the version-level conditions look at: one of them, every one, the default one, or a named one. */
export type FilterScope = 'any' | 'all' | 'default' | `version:${string}`

export const statusFilters = ['any', 'missing', 'downloading', 'pending', 'available', 'cutoffUnmet', 'noFile'] as const
export type StatusFilter = (typeof statusFilters)[number]

export type MonitoredFilter = 'any' | 'monitored' | 'unmonitored'

/** The version-level conditions both libraries have. */
export type VersionConditions = {
  monitored: MonitoredFilter
  status: StatusFilter
  qualities: string[]
  tags: number[]
  rootFolders: number[]
}

export const emptyConditions: VersionConditions = { monitored: 'any', status: 'any', qualities: [], tags: [], rootFolders: [] }

/** Monitoring, quality, tags, root folder and status of one version. */
export function versionConditionsMatch(v: LibraryVersion, filter: VersionConditions, qualitiesOf: (v: LibraryVersion) => string[] = (x) => (x.quality === null ? [] : [x.quality])): boolean {
  if (filter.monitored === 'monitored' && !v.monitored) return false
  if (filter.monitored === 'unmonitored' && v.monitored) return false
  if (filter.qualities.length > 0 && !qualitiesOf(v).some((q) => filter.qualities.includes(q))) return false
  if (filter.tags.length > 0 && !v.tags.some((tag) => filter.tags.includes(tag))) return false
  if (filter.rootFolders.length > 0 && !filter.rootFolders.includes(v.rootFolderId)) return false
  switch (filter.status) {
    case 'any':
      return true
    case 'noFile':
      return v.status !== 'available'
    case 'cutoffUnmet':
      return v.cutoffMet === false
    default:
      return v.status === filter.status
  }
}

// ---- sorting ---------------------------------------------------------------

const collator = new Intl.Collator(undefined, { sensitivity: 'base', numeric: true })

/**
 * Sorts a copy by `key`. A null key (no file for that version, never played…) goes last whatever the direction; ties
 * and nulls fall back to the sort title.
 */
export function sortItems<T extends { sortTitle: string }>(items: T[], key: (item: T) => number | string | null, direction: SortDirection): T[] {
  const sign = direction === 'asc' ? 1 : -1
  return items
    .map((item) => ({ item, key: key(item) }))
    .sort((a, b) => {
      // Items without the file go last whatever the direction: sorted by 4k size, the 4k files come first.
      if (a.key === null || b.key === null) return a.key === b.key ? collator.compare(a.item.sortTitle, b.item.sortTitle) : a.key === null ? 1 : -1
      const order = typeof a.key === 'string' ? collator.compare(a.key, b.key as string) : a.key - (b.key as number)
      return order !== 0 ? order * sign : collator.compare(a.item.sortTitle, b.item.sortTitle)
    })
    .map((entry) => entry.item)
}

/** `size:4k` → `{ metric: 'size', version: '4k' }`, when `metric` is one of `metrics`. */
export function splitVersionSort<M extends string>(key: string, metrics: readonly M[]): { metric: M; version: string } | null {
  const cut = key.indexOf(':')
  if (cut < 0) return null
  const metric = metrics.find((m) => m === key.slice(0, cut))
  return metric ? { metric, version: key.slice(cut + 1) } : null
}

/**
 * The version name holding most of a root folder's bytes (`4k` in `/movies-4k`): the storage bar opens a folder sorted
 * by that version's size, so the folder's own files decide the order.
 */
export function mainVersionIn(items: LibraryItem[], rootFolderId: number): string | undefined {
  const bytes = new Map<string, number>()
  for (const item of items) {
    for (const v of item.versions) {
      if (v.rootFolderId === rootFolderId && v.size) bytes.set(v.name, (bytes.get(v.name) ?? 0) + v.size)
    }
  }
  return [...bytes].sort((a, b) => b[1] - a[1])[0]?.[0]
}

// ---- persistence -----------------------------------------------------------

/**
 * Prefs kept in local storage under `key` (per user and per browser). `read` turns the stored value, if any, into prefs:
 * it fills the keys a later version of the page added and drops what it no longer knows.
 */
export function usePersistedPrefs<P extends object>(key: string, read: (key: string) => P) {
  const [stored, setStored] = useState(() => ({ key, prefs: read(key) }))
  if (stored.key !== key) {
    setStored({ key, prefs: read(key) })
  }

  const update = useCallback((patch: Partial<P> | ((prefs: P) => Partial<P>)) => {
    setStored((current) => {
      const prefs = { ...current.prefs, ...(typeof patch === 'function' ? patch(current.prefs) : patch) }
      try {
        localStorage.setItem(current.key, JSON.stringify(prefs))
      } catch {
        /* the layout simply does not survive a reload */
      }
      return { key: current.key, prefs }
    })
  }, [])

  return [stored.key === key ? stored.prefs : read(key), update] as const
}
