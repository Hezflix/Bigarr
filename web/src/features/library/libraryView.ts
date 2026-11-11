import type { MovieSummary, VersionSummary } from '@/api/client'
import {
  emptyConditions,
  mainVersionIn,
  sortItems,
  splitVersionSort,
  totalSize,
  usePersistedPrefs,
  versionConditionsMatch,
  viewMode,
  type Density,
  type FilterScope,
  type MonitoredFilter,
  type PosterSize,
  type SortDirection,
  type StatusFilter,
  type VersionName,
  type ViewMode,
} from './core'

export {
  densities,
  posterSizes,
  statusFilters,
  targetVersions,
  totalSize,
  versionNames,
  viewMode,
  viewModes,
  type Density,
  type FilterScope,
  type MonitoredFilter,
  type PosterSize,
  type SortDirection,
  type StatusFilter,
  type VersionName,
  type ViewMode,
} from './core'

export const sortKeys = ['title', 'year', 'added', 'size', 'quality', 'rating', 'plays', 'lastPlayed'] as const
/** The sorts that also exist per version: the size of its file, its plays, its last play. */
export const versionMetrics = ['size', 'plays', 'lastPlayed'] as const
export type VersionMetric = (typeof versionMetrics)[number]
/** A sort key, or `<metric>:<version>`: `size:4k` sorts by the size of the 4k file. */
export type SortKey = (typeof sortKeys)[number] | `${VersionMetric}:${string}`

/** The metric and version a per-version sort is about, if it is one. */
export function sortedByVersion(key: SortKey): { metric: VersionMetric; version: string } | null {
  return splitVersionSort(key, versionMetrics)
}

/** The version whose size the list is sorted by, if any. */
export function sortedVersion(key: SortKey): string | null {
  const sorted = sortedByVersion(key)
  return sorted?.metric === 'size' ? sorted.version : null
}

/** Direction of the first click on a column header: A to Z and oldest year first, but the biggest, newest, best first. */
export function firstDirection(key: SortKey): SortDirection {
  return key === 'title' || key === 'year' ? 'asc' : 'desc'
}

export const cardFields = ['title', 'year', 'versions', 'quality', 'size', 'rating', 'plays'] as const
export type CardField = (typeof cardFields)[number]

export const tableColumns = ['year', 'versions', 'status', 'quality', 'profile', 'availability', 'size', 'added', 'rating', 'tags', 'rootFolder', 'plays', 'lastPlayed'] as const
export type TableColumn = (typeof tableColumns)[number]

/**
 * Whether the version was played: never, ever, in the last N days, or not in the last N days (never played
 * included: "not played for a year" lists what nobody watched).
 */
export type PlayedFilter = 'any' | 'never' | 'ever' | `within:${number}` | `notWithin:${number}`
export const playedFilters: PlayedFilter[] = ['any', 'never', 'ever', 'within:30', 'within:365', 'notWithin:180', 'notWithin:365', 'notWithin:730']

export type LibraryFilter = {
  scope: FilterScope
  monitored: MonitoredFilter
  status: StatusFilter
  qualities: string[]
  tags: number[]
  rootFolders: number[]
  played: PlayedFilter
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
  /** The size per version under a film's total (table and posters), for films with more than one file. */
  sizeSplit: boolean
  savedViews: SavedView[]
}

export const emptyFilter: LibraryFilter = {
  scope: 'any',
  ...emptyConditions,
  played: 'any',
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
  columns: ['year', 'versions', 'quality', 'profile', 'size', 'added', 'plays'],
  sizeSplit: true,
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

export function useLibraryPrefs(userId: number | undefined) {
  return usePersistedPrefs(storageKey(userId), readPrefs)
}

// ---- filtering -------------------------------------------------------------

export function activeFilterCount(filter: LibraryFilter): number {
  return (
    (filter.monitored !== 'any' ? 1 : 0) +
    (filter.status !== 'any' ? 1 : 0) +
    filter.qualities.length +
    filter.tags.length +
    filter.rootFolders.length +
    (filter.played !== 'any' ? 1 : 0) +
    filter.hasVersions.length +
    filter.lacksVersions.length +
    (filter.addedWithinDays !== null ? 1 : 0)
  )
}

function hasVersionConditions(filter: LibraryFilter) {
  return filter.monitored !== 'any' || filter.status !== 'any' || filter.qualities.length > 0 || filter.tags.length > 0 || filter.rootFolders.length > 0 || filter.played !== 'any'
}

export function playedMatches(v: Pick<VersionSummary, 'plays' | 'lastPlayedAt'>, played: PlayedFilter, now: number): boolean {
  if (played === 'any') return true
  if (played === 'never') return v.plays === 0
  if (played === 'ever') return v.plays > 0
  const [kind, days] = played.split(':')
  const recent = v.lastPlayedAt !== null && now - Date.parse(v.lastPlayedAt) <= Number(days) * 86_400_000
  return kind === 'within' ? recent : !recent
}

function versionMatches(v: VersionSummary, filter: LibraryFilter, now: number): boolean {
  return playedMatches(v, filter.played, now) && versionConditionsMatch(v, filter)
}

export function matchesFilter(movie: MovieSummary, filter: LibraryFilter, now: number): boolean {
  if (filter.hasVersions.some((name) => !movie.versions.some((v) => v.name === name))) return false
  if (filter.lacksVersions.some((name) => movie.versions.some((v) => v.name === name))) return false
  if (filter.addedWithinDays !== null && now - Date.parse(movie.addedAt) > filter.addedWithinDays * 86_400_000) return false
  if (!hasVersionConditions(filter)) return true

  switch (filter.scope) {
    case 'any':
      return movie.versions.some((v) => versionMatches(v, filter, now))
    case 'all':
      // Every version never played: the plays whose version could not be told count too, the film was played.
      if (filter.played === 'never' && movie.plays > 0) return false
      return movie.versions.length > 0 && movie.versions.every((v) => versionMatches(v, filter, now))
    default: {
      const version = filter.scope === 'default' ? movie.versions.find((v) => v.isDefault) : movie.versions.find((v) => v.name === filter.scope.slice('version:'.length))
      // A film without that version has no file for it: "no 4k file" includes the films that never had a 4k version.
      if (!version) return filter.status === 'noFile' && filter.monitored === 'any' && filter.qualities.length === 0 && filter.tags.length === 0 && filter.rootFolders.length === 0 && filter.played === 'any'
      return versionMatches(version, filter, now)
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

/**
 * Sorts a copy. `qualityRank` maps a quality name to its position in the definitions list (resolution, then source),
 * so a film sorts by its best file.
 */
export function sortMovies(movies: MovieSummary[], sort: SortKey, direction: SortDirection, qualityRank: ReadonlyMap<string, number>): MovieSummary[] {
  const byVersion = sortedByVersion(sort)
  // Null: the film has no file for that version, or was never played.
  const key = (movie: MovieSummary): number | string | null => {
    if (byVersion !== null) {
      const v = movie.versions.find((candidate) => candidate.name === byVersion.version)
      if (!v) return null
      if (byVersion.metric === 'size') return v.size
      if (byVersion.metric === 'plays') return v.plays
      return v.lastPlayedAt === null ? null : Date.parse(v.lastPlayedAt)
    }
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
      case 'plays':
        return movie.plays
      case 'lastPlayed':
        return movie.lastPlayedAt === null ? null : Date.parse(movie.lastPlayedAt)
      default:
        return null
    }
  }

  return sortItems(movies, key, direction)
}

/**
 * The view a root folder opens from the storage bar: the films with a file in it, biggest first. The sort is the size of
 * the version name holding most of the folder's bytes (`4k` in `/movies-4k`): the folder's own files decide the order,
 * where the films' total size would count the files they keep in other folders.
 */
export function rootFolderView(movies: MovieSummary[], rootFolderId: number): Pick<LibraryPrefs, 'filter' | 'sort' | 'direction'> {
  const main = mainVersionIn(movies, rootFolderId)
  return { filter: { ...emptyFilter, status: 'available', rootFolders: [rootFolderId] }, sort: main ? `size:${main}` : 'size', direction: 'desc' }
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
    // Nobody ever played any version, the longest in the library first: what to look at when the disk fills up.
    { id: 'neverPlayed', filter: { ...emptyFilter, scope: 'all', played: 'never' }, sort: { key: 'added', direction: 'asc' } },
  ]
}
