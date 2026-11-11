import type { EpisodeStatistics, NewItemMonitoring, SeriesStatus, SeriesSummary, SeriesType, VersionMode } from '@/api/client'
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
  type LibraryItem,
  type LibraryVersion,
  type MonitoredFilter,
  type PosterSize,
  type SortDirection,
  type StatusFilter,
  type VersionName,
  type ViewMode,
} from '@/features/library/core'

/**
 * The series library's model: the film library's (filters, sorts, presets, saved views, layout) on series. Rows are the
 * API summaries mapped once onto the shared library item, so the shared table, posters, chips and bulk bar take them.
 */

// ---- rows ------------------------------------------------------------------

export type SeriesRowVersion = LibraryVersion & {
  mode: VersionMode
  statistics: EpisodeStatistics
  monitorNewItems: NewItemMonitoring
  qualities: string[]
  qualityProfile: string
}

export type SeriesRow = Omit<LibraryItem, 'versions'> & {
  tvdbId: number | null
  status: SeriesStatus
  network: string | null
  seriesType: SeriesType
  seasonCount: number
  nextAiring: string | null
  versions: SeriesRowVersion[]
}

export function toRow(s: SeriesSummary): SeriesRow {
  return {
    id: s.id,
    title: s.title,
    sortTitle: s.sortTitle,
    year: s.year,
    posterUrl: s.posterUrl,
    addedAt: s.addedAt,
    tvdbId: s.tvdbId,
    status: s.status,
    network: s.network,
    seriesType: s.seriesType,
    seasonCount: s.seasonCount,
    nextAiring: s.nextAiring,
    versions: s.versions.map((v) => ({
      id: v.id,
      name: v.name,
      isDefault: v.isDefault,
      monitored: v.monitored,
      qualityProfileId: v.qualityProfileId,
      qualityProfile: v.qualityProfile,
      status: v.status,
      quality: v.qualities.length > 0 ? v.qualities.join(' · ') : null,
      qualities: v.qualities,
      rootFolderId: v.rootFolderId,
      tags: v.tags,
      size: v.statistics.sizeOnDisk > 0 ? v.statistics.sizeOnDisk : null,
      cutoffMet: v.cutoffMet,
      mode: v.mode,
      statistics: v.statistics,
      monitorNewItems: v.monitorNewItems,
    })),
  }
}

/** The default version speaks for the series where one bar is shown, as in the film library. */
export function defaultVersion(s: SeriesRow): SeriesRowVersion | undefined {
  return s.versions.find((v) => v.isDefault) ?? s.versions[0]
}

/** Files over the episodes wanted so far; -1 when nothing is wanted yet, so those sort after any started series. */
export function progressOf(v: SeriesRowVersion | undefined): number | null {
  if (!v) return null
  const { episodeFileCount: files, episodeCount: count } = v.statistics
  return count === 0 ? -1 : files / count
}

// ---- sorting ---------------------------------------------------------------

export const seriesSortKeys = ['title', 'year', 'added', 'size', 'progress', 'quality', 'network', 'nextAiring', 'seasons'] as const
/** The sorts that also exist per version: the size of its files, its episode progress. */
export const seriesVersionMetrics = ['size', 'progress'] as const
export type SeriesVersionMetric = (typeof seriesVersionMetrics)[number]
export type SeriesSortKey = (typeof seriesSortKeys)[number] | `${SeriesVersionMetric}:${string}`

export function seriesSortedByVersion(key: SeriesSortKey) {
  return splitVersionSort(key, seriesVersionMetrics)
}

/** A to Z, oldest year first, the next airing soonest first; the biggest, newest, most complete first otherwise. */
export function seriesFirstDirection(key: SeriesSortKey): SortDirection {
  return key === 'title' || key === 'year' || key === 'network' || key === 'nextAiring' ? 'asc' : 'desc'
}

/**
 * Sorts a copy. `qualityRank` maps a quality name to its position in the definitions list, so a series sorts by its
 * best file. A series without the value (no next airing, no network, no file for that version) goes last.
 */
export function sortSeries(items: SeriesRow[], sort: SeriesSortKey, direction: SortDirection, qualityRank: ReadonlyMap<string, number>): SeriesRow[] {
  const byVersion = seriesSortedByVersion(sort)
  const key = (s: SeriesRow): number | string | null => {
    if (byVersion !== null) {
      const v = s.versions.find((candidate) => candidate.name === byVersion.version)
      if (!v) return null
      return byVersion.metric === 'size' ? v.size : progressOf(v)
    }
    switch (sort) {
      case 'title':
        return s.sortTitle
      case 'year':
        return s.year ?? 0
      case 'added':
        return Date.parse(s.addedAt)
      case 'size':
        return totalSize(s)
      case 'progress':
        return progressOf(defaultVersion(s))
      case 'quality':
        return Math.max(-1, ...s.versions.flatMap((v) => v.qualities.map((q) => qualityRank.get(q) ?? -1)))
      case 'network':
        return s.network
      case 'nextAiring':
        return s.nextAiring === null ? null : Date.parse(s.nextAiring)
      case 'seasons':
        return s.seasonCount
      default:
        return null
    }
  }
  return sortItems(items, key, direction)
}

// ---- filtering -------------------------------------------------------------

export type SeriesFilter = {
  scope: FilterScope
  monitored: MonitoredFilter
  status: StatusFilter
  qualities: string[]
  tags: number[]
  rootFolders: number[]
  /** Series-level: the series has every one of these versions. */
  hasVersions: string[]
  /** Series-level: the series has none of these versions. */
  lacksVersions: string[]
  /** Series-level: added in the last N days. */
  addedWithinDays: number | null
  /** Series-level: continuing, ended, upcoming… (any of them). */
  seriesStatuses: SeriesStatus[]
  seriesTypes: SeriesType[]
  /** Series-level; the empty string is "no network". */
  networks: string[]
}

export const emptySeriesFilter: SeriesFilter = {
  scope: 'any',
  ...emptyConditions,
  hasVersions: [],
  lacksVersions: [],
  addedWithinDays: null,
  seriesStatuses: [],
  seriesTypes: [],
  networks: [],
}

export function seriesFilterCount(filter: SeriesFilter): number {
  return (
    (filter.monitored !== 'any' ? 1 : 0) +
    (filter.status !== 'any' ? 1 : 0) +
    filter.qualities.length +
    filter.tags.length +
    filter.rootFolders.length +
    filter.hasVersions.length +
    filter.lacksVersions.length +
    (filter.addedWithinDays !== null ? 1 : 0) +
    filter.seriesStatuses.length +
    filter.seriesTypes.length +
    filter.networks.length
  )
}

function hasVersionConditions(filter: SeriesFilter) {
  return filter.monitored !== 'any' || filter.status !== 'any' || filter.qualities.length > 0 || filter.tags.length > 0 || filter.rootFolders.length > 0
}

const versionMatches = (v: SeriesRowVersion, filter: SeriesFilter) => versionConditionsMatch(v, filter, (x) => (x as SeriesRowVersion).qualities)

export function seriesMatchesFilter(s: SeriesRow, filter: SeriesFilter, now: number): boolean {
  if (filter.seriesStatuses.length > 0 && !filter.seriesStatuses.includes(s.status)) return false
  if (filter.seriesTypes.length > 0 && !filter.seriesTypes.includes(s.seriesType)) return false
  if (filter.networks.length > 0 && !filter.networks.includes(s.network ?? '')) return false
  if (filter.hasVersions.some((name) => !s.versions.some((v) => v.name === name))) return false
  if (filter.lacksVersions.some((name) => s.versions.some((v) => v.name === name))) return false
  if (filter.addedWithinDays !== null && now - Date.parse(s.addedAt) > filter.addedWithinDays * 86_400_000) return false
  if (!hasVersionConditions(filter)) return true

  switch (filter.scope) {
    case 'any':
      return s.versions.some((v) => versionMatches(v, filter))
    case 'all':
      return s.versions.length > 0 && s.versions.every((v) => versionMatches(v, filter))
    default: {
      const version = filter.scope === 'default' ? s.versions.find((v) => v.isDefault) : s.versions.find((v) => v.name === filter.scope.slice('version:'.length))
      // A series without that version has no file for it: "no 4k file" includes the series that never had a 4k version.
      if (!version) return filter.status === 'noFile' && filter.monitored === 'any' && filter.qualities.length === 0 && filter.tags.length === 0 && filter.rootFolders.length === 0
      return versionMatches(version, filter)
    }
  }
}

/** Title, original sort title, network or year. */
export function seriesMatchesSearch(s: SeriesRow, term: string): boolean {
  if (term === '') return true
  return s.title.toLowerCase().includes(term) || (s.network?.toLowerCase().includes(term) ?? false) || (s.year !== null && String(s.year) === term)
}

export function sameSeriesFilter(a: SeriesFilter, b: SeriesFilter): boolean {
  return JSON.stringify(normalized(a)) === JSON.stringify(normalized(b))
}

function normalized(filter: SeriesFilter): SeriesFilter {
  const sorted = <T>(values: T[]) => [...values].sort()
  return {
    ...filter,
    scope: hasVersionConditions(filter) ? filter.scope : 'any',
    qualities: sorted(filter.qualities),
    tags: sorted(filter.tags),
    rootFolders: sorted(filter.rootFolders),
    hasVersions: sorted(filter.hasVersions),
    lacksVersions: sorted(filter.lacksVersions),
    seriesStatuses: sorted(filter.seriesStatuses),
    seriesTypes: sorted(filter.seriesTypes),
    networks: sorted(filter.networks),
  }
}

/** The storage bar's link: the series with a file in that root folder, biggest first (as for films). */
export function seriesRootFolderView(items: SeriesRow[], rootFolderId: number): Pick<SeriesPrefs, 'filter' | 'sort' | 'direction'> {
  const main = mainVersionIn(items, rootFolderId)
  return { filter: { ...emptySeriesFilter, status: 'available', rootFolders: [rootFolderId] }, sort: main ? `size:${main}` : 'size', direction: 'desc' }
}

// ---- presets ---------------------------------------------------------------

export type SeriesPreset = {
  id: string
  filter: SeriesFilter
  sort?: { key: SeriesSortKey; direction: SortDirection }
  /** For the label: the version a "Missing <name>" preset is about. */
  version?: string
}

/** The film library's built-in views, plus continuing and ended shows. */
export function seriesPresets(names: VersionName[]): SeriesPreset[] {
  return [
    { id: 'all', filter: emptySeriesFilter },
    { id: 'missing', filter: { ...emptySeriesFilter, scope: 'default', monitored: 'monitored', status: 'missing' } },
    ...names.slice(1, 3).map((v) => ({
      id: `missing-${v.name}`,
      version: v.name,
      filter: { ...emptySeriesFilter, scope: `version:${v.name}` as const, monitored: 'monitored' as const, status: 'missing' as const },
    })),
    { id: 'cutoffUnmet', filter: { ...emptySeriesFilter, status: 'cutoffUnmet' } },
    { id: 'recent', filter: { ...emptySeriesFilter, addedWithinDays: 30 }, sort: { key: 'added', direction: 'desc' } },
    { id: 'unmonitored', filter: { ...emptySeriesFilter, scope: 'all', monitored: 'unmonitored' } },
    // What airs next first: the shows to keep an eye on.
    { id: 'continuing', filter: { ...emptySeriesFilter, seriesStatuses: ['continuing'] }, sort: { key: 'nextAiring', direction: 'asc' } },
    { id: 'ended', filter: { ...emptySeriesFilter, seriesStatuses: ['ended'] } },
  ]
}

// ---- prefs -----------------------------------------------------------------

export const seriesCardFields = ['title', 'year', 'network', 'episodes', 'versions', 'quality', 'size', 'nextAiring'] as const
export type SeriesCardField = (typeof seriesCardFields)[number]

export const seriesColumns = ['year', 'network', 'type', 'status', 'seasons', 'episodes', 'versions', 'quality', 'profile', 'size', 'nextAiring', 'added', 'tags', 'rootFolder', 'newSeasons'] as const
export type SeriesColumn = (typeof seriesColumns)[number]

export type SeriesSavedView = { id: string; name: string; filter: SeriesFilter; sort: SeriesSortKey; direction: SortDirection; mode: ViewMode }

export type SeriesPrefs = {
  mode: ViewMode
  posterSize: PosterSize
  density: Density
  sort: SeriesSortKey
  direction: SortDirection
  filter: SeriesFilter
  cardFields: SeriesCardField[]
  columns: SeriesColumn[]
  sizeSplit: boolean
  savedViews: SeriesSavedView[]
}

export const defaultSeriesPrefs: SeriesPrefs = {
  mode: 'posters',
  posterSize: 'medium',
  density: 'comfortable',
  sort: 'title',
  direction: 'asc',
  filter: emptySeriesFilter,
  cardFields: ['title', 'year', 'network', 'episodes', 'versions'],
  columns: ['year', 'network', 'status', 'episodes', 'versions', 'size', 'nextAiring'],
  sizeSplit: true,
  savedViews: [],
}

/** Per user and per browser, apart from the film library's. */
const storageKey = (userId: number | undefined) => `bigarr.seriesLibrary.${userId ?? 'anonymous'}`

/** The layout the page used before it had a view menu: kept on the first visit. */
const legacyLayoutKey = 'bigarr.series.layout'

function readPrefs(key: string): SeriesPrefs {
  try {
    const raw = localStorage.getItem(key)
    if (!raw) {
      const legacy = localStorage.getItem(legacyLayoutKey)
      return legacy === 'table' ? { ...defaultSeriesPrefs, mode: 'table' } : defaultSeriesPrefs
    }
    const stored = JSON.parse(raw) as Partial<SeriesPrefs>
    return {
      ...defaultSeriesPrefs,
      ...stored,
      mode: stored.mode === undefined ? defaultSeriesPrefs.mode : viewMode(stored.mode),
      filter: { ...emptySeriesFilter, ...stored.filter },
      cardFields: (stored.cardFields ?? defaultSeriesPrefs.cardFields).filter((f) => seriesCardFields.includes(f)),
      columns: (stored.columns ?? defaultSeriesPrefs.columns).filter((c) => seriesColumns.includes(c)),
      savedViews: (stored.savedViews ?? []).map((view) => ({ ...view, mode: viewMode(view.mode), filter: { ...emptySeriesFilter, ...view.filter } })),
    }
  } catch {
    /* storage unavailable or a corrupt value: start from the defaults */
    return defaultSeriesPrefs
  }
}

export function useSeriesPrefs(userId: number | undefined) {
  return usePersistedPrefs(storageKey(userId), readPrefs)
}
