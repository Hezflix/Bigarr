import { useCallback, useState } from 'react'
import type { MovieSummary, VersionSelector, VersionSummary } from '@/api/client'

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

export const sortKeys = ['title', 'year', 'added', 'size', 'quality', 'rating'] as const
export type SortKey = (typeof sortKeys)[number]
export type SortDirection = 'asc' | 'desc'

/** Direction of the first click on a column header: A to Z and oldest year first, but the biggest, newest, best first. */
export function firstDirection(key: SortKey): SortDirection {
  return key === 'title' || key === 'year' ? 'asc' : 'desc'
}

export const cardFields = ['title', 'year', 'versions', 'quality', 'size', 'rating'] as const
export type CardField = (typeof cardFields)[number]

export const tableColumns = ['year', 'versions', 'status', 'quality', 'profile', 'availability', 'size', 'added', 'rating', 'tags', 'rootFolder'] as const
export type TableColumn = (typeof tableColumns)[number]

/** Which versions the version-level conditions look at: one of them, every one, the default one, or a named one. */
export type FilterScope = 'any' | 'all' | 'default' | `version:${string}`

export const statusFilters = ['any', 'missing', 'downloading', 'pending', 'available', 'cutoffUnmet', 'noFile'] as const
export type StatusFilter = (typeof statusFilters)[number]

export type MonitoredFilter = 'any' | 'monitored' | 'unmonitored'

export type LibraryFilter = {
  scope: FilterScope
  monitored: MonitoredFilter
  status: StatusFilter
  qualities: string[]
  tags: number[]
  rootFolders: number[]
  /** Film-level: the film has every one of these versions. */
  hasVersions: string[]
  /** Film-level: the film has none of these versions. */
  lacksVersions: string[]
  /** Film-level: added in the last N days. */
  addedWithinDays: number | null
}

export type SavedView = {
  id: string
  name: string
  filter: LibraryFilter
  sort: SortKey
  direction: SortDirection
  mode: ViewMode
}

export type LibraryPrefs = {
  mode: ViewMode
  posterSize: PosterSize
  density: Density
  sort: SortKey
  direction: SortDirection
  filter: LibraryFilter
  cardFields: CardField[]
  columns: TableColumn[]
  savedViews: SavedView[]
}

export const emptyFilter: LibraryFilter = {
  scope: 'any',
  monitored: 'any',
  status: 'any',
  qualities: [],
  tags: [],
  rootFolders: [],
  hasVersions: [],
  lacksVersions: [],
  addedWithinDays: null,
}

export const defaultPrefs: LibraryPrefs = {
  mode: 'posters',
  posterSize: 'medium',
  density: 'comfortable',
  sort: 'title',
  direction: 'asc',
  filter: emptyFilter,
  cardFields: ['title', 'year', 'versions'],
  columns: ['year', 'versions', 'quality', 'profile', 'size', 'added'],
  savedViews: [],
}

// ---- persistence -----------------------------------------------------------

/** Per user and per browser: two people sharing a machine keep their own library layout. */
const storageKey = (userId: number | undefined) => `bigarr.library.${userId ?? 'anonymous'}`

function readPrefs(key: string): LibraryPrefs {
  try {
    const raw = localStorage.getItem(key)
    if (!raw) return defaultPrefs
    const stored = JSON.parse(raw) as Partial<LibraryPrefs>
    // Spread over the defaults: a key added by a later version of the page gets its default.
    return {
      ...defaultPrefs,
      ...stored,
      mode: stored.mode === undefined ? defaultPrefs.mode : viewMode(stored.mode),
      filter: { ...emptyFilter, ...stored.filter },
      savedViews: (stored.savedViews ?? []).map((view) => ({ ...view, mode: viewMode(view.mode), filter: { ...emptyFilter, ...view.filter } })),
    }
  } catch {
    /* storage unavailable or a corrupt value: start from the defaults */
    return defaultPrefs
  }
}

function writePrefs(key: string, prefs: LibraryPrefs) {
  try {
    localStorage.setItem(key, JSON.stringify(prefs))
  } catch {
    /* the layout simply does not survive a reload */
  }
}

export function useLibraryPrefs(userId: number | undefined) {
  const key = storageKey(userId)
  const [stored, setStored] = useState(() => ({ key, prefs: readPrefs(key) }))
  if (stored.key !== key) {
    setStored({ key, prefs: readPrefs(key) })
  }

  const update = useCallback((patch: Partial<LibraryPrefs> | ((prefs: LibraryPrefs) => Partial<LibraryPrefs>)) => {
    setStored((current) => {
      const prefs = { ...current.prefs, ...(typeof patch === 'function' ? patch(current.prefs) : patch) }
      writePrefs(current.key, prefs)
      return { key: current.key, prefs }
    })
  }, [])

  return [stored.key === key ? stored.prefs : readPrefs(key), update] as const
}

// ---- versions --------------------------------------------------------------

export type VersionName = { name: string; count: number }

/** Every version name of the library, the most common first: usually the default flavour, then `4k`… */
export function versionNames(movies: MovieSummary[]): VersionName[] {
  const counts = new Map<string, number>()
  for (const movie of movies) {
    for (const v of movie.versions) counts.set(v.name, (counts.get(v.name) ?? 0) + 1)
  }
  return [...counts].map(([name, count]) => ({ name, count })).sort((a, b) => b.count - a.count || a.name.localeCompare(b.name))
}

/** The versions of the film a bulk action would touch. */
export function targetVersions(movie: MovieSummary, selector: VersionSelector): VersionSummary[] {
  if (selector === 'all') return movie.versions
  if (selector === 'default') return movie.versions.filter((v) => v.isDefault)
  const name = selector.slice('version:'.length)
  return movie.versions.filter((v) => v.name === name)
}

// ---- filtering -------------------------------------------------------------

export function activeFilterCount(filter: LibraryFilter): number {
  return (
    (filter.monitored !== 'any' ? 1 : 0) +
    (filter.status !== 'any' ? 1 : 0) +
    filter.qualities.length +
    filter.tags.length +
    filter.rootFolders.length +
    filter.hasVersions.length +
    filter.lacksVersions.length +
    (filter.addedWithinDays !== null ? 1 : 0)
  )
}

function hasVersionConditions(filter: LibraryFilter) {
  return filter.monitored !== 'any' || filter.status !== 'any' || filter.qualities.length > 0 || filter.tags.length > 0 || filter.rootFolders.length > 0
}

function versionMatches(v: VersionSummary, filter: LibraryFilter): boolean {
  if (filter.monitored === 'monitored' && !v.monitored) return false
  if (filter.monitored === 'unmonitored' && v.monitored) return false
  if (filter.qualities.length > 0 && (v.quality === null || !filter.qualities.includes(v.quality))) return false
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

export function matchesFilter(movie: MovieSummary, filter: LibraryFilter, now: number): boolean {
  if (filter.hasVersions.some((name) => !movie.versions.some((v) => v.name === name))) return false
  if (filter.lacksVersions.some((name) => movie.versions.some((v) => v.name === name))) return false
  if (filter.addedWithinDays !== null && now - Date.parse(movie.addedAt) > filter.addedWithinDays * 86_400_000) return false
  if (!hasVersionConditions(filter)) return true

  switch (filter.scope) {
    case 'any':
      return movie.versions.some((v) => versionMatches(v, filter))
    case 'all':
      return movie.versions.length > 0 && movie.versions.every((v) => versionMatches(v, filter))
    default: {
      const version = filter.scope === 'default' ? movie.versions.find((v) => v.isDefault) : movie.versions.find((v) => v.name === filter.scope.slice('version:'.length))
      // A film without that version has no file for it: "no 4k file" includes the films that never had a 4k version.
      if (!version) return filter.status === 'noFile' && filter.monitored === 'any' && filter.qualities.length === 0 && filter.tags.length === 0 && filter.rootFolders.length === 0
      return versionMatches(version, filter)
    }
  }
}

export function matchesSearch(movie: MovieSummary, term: string): boolean {
  if (term === '') return true
  return movie.title.toLowerCase().includes(term) || (movie.year !== null && String(movie.year) === term)
}

export function sameFilter(a: LibraryFilter, b: LibraryFilter): boolean {
  return JSON.stringify(normalized(a)) === JSON.stringify(normalized(b))
}

function normalized(filter: LibraryFilter): LibraryFilter {
  const sorted = <T>(values: T[]) => [...values].sort()
  return {
    ...filter,
    // Without version conditions the scope means nothing.
    scope: hasVersionConditions(filter) ? filter.scope : 'any',
    qualities: sorted(filter.qualities),
    tags: sorted(filter.tags),
    rootFolders: sorted(filter.rootFolders),
    hasVersions: sorted(filter.hasVersions),
    lacksVersions: sorted(filter.lacksVersions),
  }
}

// ---- sorting ---------------------------------------------------------------

const collator = new Intl.Collator(undefined, { sensitivity: 'base', numeric: true })

export function totalSize(movie: MovieSummary): number {
  return movie.versions.reduce((sum, v) => sum + (v.size ?? 0), 0)
}

/**
 * Sorts a copy. `qualityRank` maps a quality name to its position in the definitions list (resolution, then source),
 * so a film sorts by its best file.
 */
export function sortMovies(movies: MovieSummary[], sort: SortKey, direction: SortDirection, qualityRank: ReadonlyMap<string, number>): MovieSummary[] {
  const key = (movie: MovieSummary): number | string => {
    switch (sort) {
      case 'title':
        return movie.sortTitle
      case 'year':
        return movie.year ?? 0
      case 'added':
        return Date.parse(movie.addedAt)
      case 'size':
        return totalSize(movie)
      case 'quality':
        return Math.max(-1, ...movie.versions.map((v) => (v.quality === null ? -1 : (qualityRank.get(v.quality) ?? -1))))
      case 'rating':
        return movie.tmdbRating ?? movie.imdbRating ?? -1
    }
  }

  const sign = direction === 'asc' ? 1 : -1
  return movies
    .map((movie) => ({ movie, key: key(movie) }))
    .sort((a, b) => {
      const order = typeof a.key === 'string' ? collator.compare(a.key, b.key as string) : a.key - (b.key as number)
      return order !== 0 ? order * sign : collator.compare(a.movie.sortTitle, b.movie.sortTitle)
    })
    .map((entry) => entry.movie)
}

// ---- saved views -----------------------------------------------------------

export type Preset = {
  id: string
  filter: LibraryFilter
  sort?: { key: SortKey; direction: SortDirection }
  /** For the label: the version a "Missing <name>" preset is about. */
  version?: string
}

/**
 * Built-in views. "Missing <name>" is offered for the secondary flavours (`4k`…): every version name but the most
 * common one, which is the default almost everywhere and already covered by "Missing".
 */
export function presets(names: VersionName[]): Preset[] {
  return [
    { id: 'all', filter: emptyFilter },
    { id: 'missing', filter: { ...emptyFilter, scope: 'default', monitored: 'monitored', status: 'missing' } },
    // Films with that version wanted and still without a file; which films lack the version altogether is the "Lacks version" filter's job.
    ...names.slice(1, 3).map((v) => ({ id: `missing-${v.name}`, version: v.name, filter: { ...emptyFilter, scope: `version:${v.name}` as const, monitored: 'monitored' as const, status: 'missing' as const } })),
    { id: 'cutoffUnmet', filter: { ...emptyFilter, status: 'cutoffUnmet' } },
    { id: 'recent', filter: { ...emptyFilter, addedWithinDays: 30 }, sort: { key: 'added', direction: 'desc' } },
    { id: 'unmonitored', filter: { ...emptyFilter, scope: 'all', monitored: 'unmonitored' } },
  ]
}
