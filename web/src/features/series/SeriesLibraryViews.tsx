import type { ReactNode } from 'react'
import { Link } from '@tanstack/react-router'
import { cn } from 'cn'
import { useTranslation } from 'react-i18next'
import type { RootFolder, SeriesStatus, SeriesType, Tag } from '@/api/client'
import { Badge } from '@/components/ui/badge'
import { CardContent } from '@/components/ui/card'
import { DropdownMenuSeparator } from '@/components/ui/dropdown-menu'
import { formatBytes, formatDate } from '@/i18n'
import type { Density, PosterSize, SortDirection, VersionName } from '@/features/library/core'
import {
  AddedSub,
  CheckboxSub,
  FilterMenuShell,
  LibrarySearchBar,
  MonitoredSub,
  ScopeSub,
  SortSub,
  StatusSub,
  toggle,
  VersionPresenceSubs,
  ViewOptionsMenu,
} from '@/features/library/LibraryControls'
import { LibraryPosterGrid } from '@/features/library/PosterGrid'
import { LibraryTable, type ItemLink, type TableColumnSpec } from '@/features/library/LibraryTable'
import type { Selection } from '@/features/library/selection'
import { SizeSplit } from '@/features/library/SizeSplit'
import { VersionChip } from '@/features/library/VersionChip'
import { EpisodeProgress } from './EpisodeProgress'
import {
  defaultVersion,
  emptySeriesFilter,
  seriesCardFields,
  seriesColumns,
  seriesFilterCount,
  seriesSortedByVersion,
  seriesSortKeys,
  type SeriesCardField,
  type SeriesColumn,
  type SeriesFilter,
  type SeriesPrefs,
  type SeriesRow,
  type SeriesRowVersion,
  type SeriesSortKey,
} from './seriesView'

/** Names behind the ids a summary carries. */
export type SeriesLookups = {
  profiles: ReadonlyMap<number, string>
  tags: ReadonlyMap<number, string>
  rootFolders: ReadonlyMap<number, string>
}

const seriesLink: ItemLink<SeriesRow> = (s, props) => <Link to="/series/$seriesId" params={{ seriesId: String(s.id) }} {...props} />

/** "Size · 4k", "Episodes · 4k", "Next airing": a sort's label. */
export function useSeriesSortLabel() {
  const { t } = useTranslation()
  const metricLabel = (metric: 'size' | 'progress') => (metric === 'size' ? t('library.sort.size') : t('seriesLibrary.sort.progress'))
  return (key: SeriesSortKey) => {
    const sorted = seriesSortedByVersion(key)
    if (sorted) return t('library.sort.ofVersion', { metric: metricLabel(sorted.metric), version: sorted.version })
    switch (key) {
      case 'network':
      case 'nextAiring':
      case 'progress':
      case 'seasons':
        return t(`seriesLibrary.sort.${key}`)
      default:
        return t(`library.sort.${key as 'title'}`)
    }
  }
}

/** A version chip with the series wording and the version's episode count on hover. */
function SeriesVersionChip({ version, className }: { version: SeriesRowVersion; className?: string }) {
  const { t } = useTranslation()
  return (
    <VersionChip
      version={version}
      legend="seriesLibrary"
      detail={t('seriesLibrary.episodes', { files: version.statistics.episodeFileCount, count: version.statistics.episodeCount })}
      className={className}
    />
  )
}

// ---- controls --------------------------------------------------------------

type ControlsProps = {
  prefs: SeriesPrefs
  onChange: (patch: Partial<SeriesPrefs>) => void
  term: string
  onTermChange: (term: string) => void
  names: VersionName[]
  qualities: string[]
  networks: string[]
  tags: Tag[]
  rootFolders: RootFolder[]
}

/** The film library's controls on series: search, layout, filter (with the series' own conditions), view options. */
export function SeriesLibraryControls({ prefs, onChange, term, onTermChange, names, qualities, networks, tags, rootFolders }: ControlsProps) {
  const { t } = useTranslation()
  const sortLabel = useSeriesSortLabel()
  // With several versions, each one's size and progress sort too, right after the series'.
  const keys: SeriesSortKey[] = seriesSortKeys.flatMap((key) =>
    (key === 'size' || key === 'progress') && names.length > 1 ? [key, ...names.map((n) => `${key}:${n.name}` as const)] : [key],
  )
  return (
    <LibrarySearchBar term={term} onTermChange={onTermChange} placeholder={t('seriesLibrary.searchPlaceholder')} mode={prefs.mode} onMode={(mode) => onChange({ mode })}>
      <SeriesFilterMenu
        filter={prefs.filter}
        onChange={(filter) => onChange({ filter })}
        names={names}
        qualities={qualities}
        networks={networks}
        tags={tags}
        rootFolders={rootFolders}
      />
      <ViewOptionsMenu
        view={prefs}
        onChange={onChange}
        sort={<SortSub keys={keys} label={sortLabel} sort={prefs.sort} direction={prefs.direction} onChange={onChange} />}
        cardFields={{ all: seriesCardFields, value: prefs.cardFields, label: (field) => t(`seriesLibrary.fields.${field}`) }}
        columns={{ all: seriesColumns, value: prefs.columns, label: (column) => t(`seriesLibrary.columns.${column}`) }}
        splittable={names.length > 1}
      />
    </LibrarySearchBar>
  )
}

const seriesStatuses: SeriesStatus[] = ['continuing', 'upcoming', 'ended', 'deleted']
const seriesTypes: SeriesType[] = ['standard', 'anime', 'daily']

function SeriesFilterMenu({
  filter,
  onChange,
  names,
  qualities,
  networks,
  tags,
  rootFolders,
}: {
  filter: SeriesFilter
  onChange: (filter: SeriesFilter) => void
  names: VersionName[]
  qualities: string[]
  networks: string[]
  tags: Tag[]
  rootFolders: RootFolder[]
}) {
  const { t } = useTranslation()
  const set = (patch: Partial<SeriesFilter>) => onChange({ ...filter, ...patch })
  return (
    <FilterMenuShell active={seriesFilterCount(filter)} onClear={() => onChange(emptySeriesFilter)}>
      <ScopeSub value={filter.scope} onChange={(scope) => set({ scope })} names={names} />
      <MonitoredSub value={filter.monitored} onChange={(monitored) => set({ monitored })} />
      <StatusSub value={filter.status} onChange={(status) => set({ status })} labels={{ pending: t('seriesLibrary.filter.pending') }} />
      <CheckboxSub
        label={t('library.filter.quality')}
        options={qualities.map((q) => ({ value: q, label: q }))}
        values={filter.qualities}
        onToggle={(value) => set({ qualities: toggle(filter.qualities, value) })}
      />
      <CheckboxSub label={t('library.filter.tags')} options={tags.map((tag) => ({ value: tag.id, label: tag.label }))} values={filter.tags} onToggle={(value) => set({ tags: toggle(filter.tags, value) })} />
      <CheckboxSub
        label={t('library.filter.rootFolder')}
        options={rootFolders.map((root) => ({ value: root.id, label: root.path }))}
        values={filter.rootFolders}
        onToggle={(value) => set({ rootFolders: toggle(filter.rootFolders, value) })}
      />
      <DropdownMenuSeparator />
      <CheckboxSub
        label={t('seriesLibrary.filter.seriesStatus')}
        options={seriesStatuses.map((value) => ({ value, label: t(`seriesStatus.${value}`) }))}
        values={filter.seriesStatuses}
        onToggle={(value) => set({ seriesStatuses: toggle(filter.seriesStatuses, value) })}
        testId="series-filter-status"
      />
      <CheckboxSub
        label={t('seriesLibrary.filter.seriesType')}
        options={seriesTypes.map((value) => ({ value, label: t(`seriesType.${value}`) }))}
        values={filter.seriesTypes}
        onToggle={(value) => set({ seriesTypes: toggle(filter.seriesTypes, value) })}
        testId="series-filter-type"
      />
      <CheckboxSub
        label={t('seriesLibrary.filter.network')}
        options={networks.map((value) => ({ value, label: value === '' ? t('seriesLibrary.filter.noNetwork') : value }))}
        values={filter.networks}
        onToggle={(value) => set({ networks: toggle(filter.networks, value) })}
        testId="series-filter-network"
      />
      <DropdownMenuSeparator />
      <VersionPresenceSubs names={names} has={filter.hasVersions} lacks={filter.lacksVersions} onChange={(patch) => set(patch)} />
      <AddedSub value={filter.addedWithinDays} onChange={(addedWithinDays) => set({ addedWithinDays })} />
    </FilterMenuShell>
  )
}

// ---- posters ---------------------------------------------------------------

type GridProps = Selection & {
  items: SeriesRow[]
  posterSize: PosterSize
  density: Density
  fields: SeriesCardField[]
  sizeSplit: boolean
  /** The version the list is sorted by the size of: it stands out in the split. */
  emphasis: string | null
}

export function SeriesPosterGrid({ items, posterSize, density, fields, sizeSplit, emphasis, ...selection }: GridProps) {
  return (
    <LibraryPosterGrid
      items={items}
      posterSize={posterSize}
      density={density}
      // The episode bar takes about two lines.
      lines={fields.length + (fields.includes('episodes') ? 1 : 0)}
      renderInfo={(s) => (fields.length > 0 ? <SeriesCardInfo series={s} density={density} posterSize={posterSize} fields={fields} sizeSplit={sizeSplit} emphasis={emphasis} /> : null)}
      link={seriesLink}
      testId="series"
      {...selection}
    />
  )
}

function SeriesCardInfo({
  series: s,
  density,
  posterSize,
  fields,
  sizeSplit,
  emphasis,
}: {
  series: SeriesRow
  density: Density
  posterSize: PosterSize
  fields: SeriesCardField[]
  sizeSplit: boolean
  emphasis: string | null
}) {
  const { t } = useTranslation()
  const show = (field: SeriesCardField) => fields.includes(field)
  const version = defaultVersion(s)
  const size = s.versions.reduce((sum, v) => sum + (v.size ?? 0), 0)
  const qualities = [...new Set(s.versions.flatMap((v) => v.qualities))]
  const meta = [
    show('year') ? (s.year ?? t('common.none')) : null,
    show('network') ? s.network : null,
    show('size') && size > 0 ? formatBytes(size) : null,
  ].filter((part) => part !== null && part !== '')

  return (
    <CardContent className={cn('flex flex-col gap-1', density === 'compact' ? 'px-2 py-1.5' : 'px-3 py-2.5')}>
      {show('title') && <span className={cn('leading-snug font-medium', posterSize === 'small' || density === 'compact' ? 'line-clamp-1 text-xs' : 'line-clamp-2 text-sm')}>{s.title}</span>}
      {meta.length > 0 && <span className="truncate text-xs text-muted-foreground tabular-nums">{meta.join(' · ')}</span>}
      {sizeSplit && show('size') && <SizeSplit item={s} emphasis={emphasis} />}
      {show('nextAiring') && s.nextAiring && <span className="truncate text-xs text-muted-foreground tabular-nums">{t('seriesLibrary.nextAiring', { date: formatDate(s.nextAiring) })}</span>}
      {show('episodes') && version && <EpisodeProgress statistics={version.statistics} monitored={version.monitored} />}
      {show('quality') && <span className="truncate text-xs text-muted-foreground">{qualities.length > 0 ? qualities.join(' · ') : t('library.noFile')}</span>}
      {show('versions') && (s.versions.length > 0 || s.seriesType !== 'standard') && (
        <div className="pointer-events-none relative z-10 mt-0.5 flex flex-wrap gap-1">
          {s.seriesType !== 'standard' && <Badge variant="secondary">{t(`seriesType.${s.seriesType}`)}</Badge>}
          {s.versions.map((v) => (
            <SeriesVersionChip key={v.id} version={v} className="pointer-events-auto" />
          ))}
        </div>
      )}
    </CardContent>
  )
}

// ---- table -----------------------------------------------------------------

const widths: Record<SeriesColumn, string> = {
  year: '4rem',
  network: 'minmax(6rem, 0.8fr)',
  type: '5.5rem',
  status: '7rem',
  seasons: '4.5rem',
  episodes: '9.5rem',
  versions: 'minmax(8rem, 1.2fr)',
  quality: 'minmax(8rem, 1fr)',
  profile: 'minmax(6rem, 0.8fr)',
  size: '5.5rem',
  nextAiring: '7rem',
  added: '7rem',
  tags: 'minmax(6rem, 0.8fr)',
  rootFolder: 'minmax(8rem, 1fr)',
  newSeasons: '7rem',
}

const sortable: Partial<Record<SeriesColumn, SeriesSortKey>> = {
  year: 'year',
  network: 'network',
  seasons: 'seasons',
  episodes: 'progress',
  quality: 'quality',
  size: 'size',
  nextAiring: 'nextAiring',
  added: 'added',
}

const perVersion: Partial<Record<SeriesColumn, 'size' | 'progress'>> = { size: 'size', episodes: 'progress' }

type Split = 'under' | 'inline' | null

type TableProps = Selection & {
  items: SeriesRow[]
  columns: SeriesColumn[]
  density: Density
  posterSize: PosterSize
  lookups: SeriesLookups
  sort: SeriesSortKey
  direction: SortDirection
  onSort: (key: SeriesSortKey) => void
  sizeSplit: boolean
  versionNames: string[]
}

/** The film table on series: same headers, sorts, density, thumbnails and row link. */
export function SeriesTable({ items, columns, density, posterSize, lookups, sort, direction, onSort, sizeSplit, versionNames, ...selection }: TableProps) {
  const { t } = useTranslation()
  const sortLabel = useSeriesSortLabel()
  const split: Split = sizeSplit && versionNames.length > 1 ? (density === 'comfortable' ? 'under' : 'inline') : null
  const sortedBy = seriesSortedByVersion(sort)
  const emphasis = sortedBy?.metric === 'size' ? sortedBy.version : null
  const progressOfVersion = sortedBy?.metric === 'progress' ? sortedBy.version : null
  const width = (column: SeriesColumn) => (column === 'size' && split !== null ? (split === 'under' ? '11rem' : '16.5rem') : widths[column])
  const specs: TableColumnSpec<SeriesColumn>[] = columns.map((c) => ({ id: c, label: t(`seriesLibrary.columns.${c}`), width: width(c), sortKey: sortable[c], metric: perVersion[c] }))

  return (
    <LibraryTable
      items={items}
      columns={specs}
      density={density}
      posterSize={posterSize}
      sort={sort}
      direction={direction}
      onSort={(key) => onSort(key as SeriesSortKey)}
      versionNames={versionNames}
      sortedMetric={sortedBy?.metric ?? null}
      sortLabel={(key) => sortLabel(key as SeriesSortKey)}
      metricMenuLabel={(metric) => (metric === 'size' ? t('library.sort.sizeOf') : t('seriesLibrary.sort.progressOf'))}
      renderCell={(s, column) => <Cell series={s} column={column} lookups={lookups} split={split} emphasis={emphasis} progressOf={progressOfVersion} compact={density === 'compact'} />}
      link={seriesLink}
      testId="series"
      {...selection}
    />
  )
}

function Cell({
  series: s,
  column,
  lookups,
  split,
  emphasis,
  progressOf,
  compact,
}: {
  series: SeriesRow
  column: SeriesColumn
  lookups: SeriesLookups
  split: Split
  emphasis: string | null
  /** Sorted by one version's episodes, the cell shows that version's. */
  progressOf: string | null
  compact: boolean
}): ReactNode {
  const { t } = useTranslation()
  const distinct = <T,>(values: T[]) => [...new Set(values)]
  const none = <span className="text-muted-foreground">{t('common.none')}</span>
  switch (column) {
    case 'year':
      return <span className="tabular-nums text-muted-foreground">{s.year ?? t('common.none')}</span>
    case 'network':
      return s.network ?? none
    case 'type':
      return <span className="text-muted-foreground">{t(`seriesType.${s.seriesType}`)}</span>
    case 'status':
      return <span className="text-muted-foreground">{t(`seriesStatus.${s.status}`)}</span>
    case 'seasons':
      return <span className="tabular-nums">{s.seasonCount}</span>
    case 'episodes': {
      const version = progressOf === null ? defaultVersion(s) : s.versions.find((v) => v.name === progressOf)
      return version ? <EpisodeProgress statistics={version.statistics} monitored={version.monitored} inline={compact} /> : none
    }
    case 'versions':
      return (
        <span className="flex gap-1">
          {s.versions.map((v) => (
            <SeriesVersionChip key={v.id} version={v} />
          ))}
        </span>
      )
    case 'quality': {
      const qualities = distinct(s.versions.flatMap((v) => v.qualities))
      return qualities.length > 0 ? qualities.join(' · ') : <span className="text-muted-foreground">{t('library.noFile')}</span>
    }
    case 'profile':
      return distinct(s.versions.map((v) => lookups.profiles.get(v.qualityProfileId) ?? v.qualityProfile)).join(' · ')
    case 'size': {
      const size = s.versions.reduce((sum, v) => sum + (v.size ?? 0), 0)
      const total = <span className="tabular-nums">{size > 0 ? formatBytes(size) : none}</span>
      if (split === 'under') {
        return (
          <span className="flex flex-col leading-tight">
            {total}
            <SizeSplit item={s} emphasis={emphasis} />
          </span>
        )
      }
      if (split === 'inline') {
        return (
          <span className="flex min-w-0 items-baseline gap-2">
            <span className="min-w-[4.5rem] shrink-0">{total}</span>
            <SizeSplit item={s} emphasis={emphasis} className="min-w-0" />
          </span>
        )
      }
      return total
    }
    case 'nextAiring':
      return <span className="tabular-nums text-muted-foreground">{s.nextAiring ? formatDate(s.nextAiring) : t('common.none')}</span>
    case 'added':
      return <span className="tabular-nums text-muted-foreground">{formatDate(s.addedAt)}</span>
    case 'tags': {
      const labels = distinct(s.versions.flatMap((v) => v.tags)).map((id) => lookups.tags.get(id) ?? `#${id}`)
      return (
        <span className="flex gap-1">
          {labels.map((label) => (
            <Badge key={label} variant="secondary" className="px-1.5 py-0 text-[11px]">
              {label}
            </Badge>
          ))}
        </span>
      )
    }
    case 'rootFolder':
      return (
        <span className="text-muted-foreground" title={distinct(s.versions.map((v) => lookups.rootFolders.get(v.rootFolderId) ?? '')).join('\n')}>
          {distinct(s.versions.map((v) => lookups.rootFolders.get(v.rootFolderId) ?? `#${v.rootFolderId}`)).join(' · ')}
        </span>
      )
    case 'newSeasons':
      return <span className="text-muted-foreground">{distinct(s.versions.map((v) => t(`seriesLibrary.newSeasons.${v.monitorNewItems}`))).join(' · ')}</span>
  }
}
