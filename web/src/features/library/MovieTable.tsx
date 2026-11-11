import type { ReactNode } from 'react'
import { Link } from '@tanstack/react-router'
import { cn } from 'cn'
import { useTranslation } from 'react-i18next'
import type { MovieSummary } from '@/api/client'
import { Badge } from '@/components/ui/badge'
import { formatBytes, formatDate } from '@/i18n'
import { sortedByVersion, totalSize, type Density, type PosterSize, type SortDirection, type SortKey, type TableColumn, type VersionMetric } from './libraryView'
import { LibraryTable, type TableColumnSpec } from './LibraryTable'
import type { Selection } from './selection'
import { PlaysSplit, SizeSplit, useSortLabel } from './SizeSplit'
import { VersionChip } from './VersionChip'

/** Names behind the ids a summary carries. */
export type Lookups = {
  profiles: ReadonlyMap<number, string>
  tags: ReadonlyMap<number, string>
  rootFolders: ReadonlyMap<number, string>
}

const widths: Record<TableColumn, string> = {
  year: '4rem',
  versions: 'minmax(8rem, 1.2fr)',
  status: '7rem',
  quality: 'minmax(8rem, 1fr)',
  profile: 'minmax(6rem, 0.8fr)',
  availability: '7rem',
  size: '5.5rem',
  added: '7rem',
  rating: '4rem',
  tags: 'minmax(6rem, 0.8fr)',
  rootFolder: 'minmax(8rem, 1fr)',
  plays: '4.5rem',
  lastPlayed: '9rem',
}

const sortable: Partial<Record<TableColumn, SortKey>> = {
  year: 'year',
  added: 'added',
  size: 'size',
  quality: 'quality',
  rating: 'rating',
  plays: 'plays',
  lastPlayed: 'lastPlayed',
}

/** The columns whose header also sorts by one version's value, through a menu, when the library has several. */
const perVersion: Partial<Record<TableColumn, VersionMetric>> = { size: 'size', plays: 'plays', lastPlayed: 'lastPlayed' }

/** Where the Size cell puts the size per version: under the total, or on its line when the row has room for one line only. */
type Split = 'under' | 'inline' | null

type Props = Selection & {
  movies: MovieSummary[]
  columns: TableColumn[]
  density: Density
  posterSize: PosterSize
  lookups: Lookups
  sort: SortKey
  direction: SortDirection
  onSort: (key: SortKey) => void
  /** Show the size per version in the Size column. */
  sizeSplit: boolean
  /** The library's version names: with more than one, the Size header can sort by one version's size. */
  versionNames: string[]
}

/** Radarr's table view: one row per film, columns picked in the view menu, headers sort, a row opens its film. */
export function MovieTable({ movies, columns, density, posterSize, lookups, sort, direction, onSort, sizeSplit, versionNames, ...selection }: Props) {
  const { t } = useTranslation()
  const sortLabel = useSortLabel()
  const versions = versionNames.length > 1
  const split: Split = sizeSplit && versions ? (density === 'comfortable' ? 'under' : 'inline') : null
  const sortedBy = sortedByVersion(sort)
  const emphasis = sortedBy?.metric === 'size' ? sortedBy.version : null
  const playsEmphasis = sortedBy?.metric === 'plays' ? sortedBy.version : null
  const lastPlayedOf = sortedBy?.metric === 'lastPlayed' ? sortedBy.version : null
  // The split needs room: "1080p 39 GB · 4k 82 GB" under the total, or next to it on a compact row.
  const width = (column: TableColumn) => {
    if (column === 'size' && split !== null) return split === 'under' ? '11rem' : '16.5rem'
    if (column === 'plays' && split !== null) return split === 'under' ? '8.5rem' : '12rem'
    return widths[column]
  }
  const specs: TableColumnSpec<TableColumn>[] = columns.map((c) => ({ id: c, label: t(`library.columns.${c}`), width: width(c), sortKey: sortable[c], metric: perVersion[c] }))

  return (
    <LibraryTable
      items={movies}
      columns={specs}
      density={density}
      posterSize={posterSize}
      sort={sort}
      direction={direction}
      onSort={(key) => onSort(key as SortKey)}
      versionNames={versionNames}
      sortedMetric={sortedBy?.metric ?? null}
      sortLabel={(key) => sortLabel(key as SortKey)}
      metricMenuLabel={(metric) => t(`library.sort.${metric as VersionMetric}Of`)}
      renderCell={(movie, column) => (
        <Cell movie={movie} column={column} lookups={lookups} split={split} emphasis={emphasis} playsEmphasis={playsEmphasis} lastPlayedOf={lastPlayedOf} />
      )}
      link={(movie, props) => <Link to="/movies/$movieId" params={{ movieId: String(movie.id) }} {...props} />}
      testId="movie"
      {...selection}
    />
  )
}

function Cell({
  movie,
  column,
  lookups,
  split,
  emphasis,
  playsEmphasis,
  lastPlayedOf,
}: {
  movie: MovieSummary
  column: TableColumn
  lookups: Lookups
  split: Split
  emphasis: string | null
  playsEmphasis: string | null
  lastPlayedOf: string | null
}): ReactNode {
  const { t } = useTranslation()
  const distinct = <T,>(values: T[]) => [...new Set(values)]
  switch (column) {
    case 'year':
      return <span className="tabular-nums text-muted-foreground">{movie.year ?? t('common.none')}</span>
    case 'versions':
      return (
        <span className="flex gap-1">
          {movie.versions.map((v) => (
            <VersionChip key={v.id} version={v} />
          ))}
        </span>
      )
    case 'status':
      return <span className="text-muted-foreground">{t(`movieStatus.${movie.effectiveStatus}`)}</span>
    case 'quality': {
      const qualities = distinct(movie.versions.map((v) => v.quality).filter((q): q is string => q !== null))
      return qualities.length > 0 ? qualities.join(' · ') : <span className="text-muted-foreground">{t('library.noFile')}</span>
    }
    case 'profile':
      return distinct(movie.versions.map((v) => lookups.profiles.get(v.qualityProfileId) ?? `#${v.qualityProfileId}`)).join(' · ')
    case 'availability':
      return <span className="text-muted-foreground">{distinct(movie.versions.map((v) => t(`availability.${v.minimumAvailability}`))).join(' · ')}</span>
    case 'size': {
      const size = totalSize(movie)
      const total = <span className="tabular-nums">{size > 0 ? formatBytes(size) : <span className="text-muted-foreground">{t('common.none')}</span>}</span>
      if (split === 'under') {
        return (
          <span className="flex flex-col leading-tight">
            {total}
            <SizeSplit item={movie} emphasis={emphasis} />
          </span>
        )
      }
      if (split === 'inline') {
        // The total keeps a fixed width so the splits line up from row to row.
        return (
          <span className="flex min-w-0 items-baseline gap-2">
            <span className="min-w-[4.5rem] shrink-0">{total}</span>
            <SizeSplit item={movie} emphasis={emphasis} className="min-w-0" />
          </span>
        )
      }
      return total
    }
    case 'added':
      return <span className="tabular-nums text-muted-foreground">{formatDate(movie.addedAt)}</span>
    case 'rating': {
      const rating = movie.tmdbRating ?? movie.imdbRating
      return <span className="tabular-nums">{rating === null ? t('common.none') : rating.toFixed(1)}</span>
    }
    case 'tags': {
      const labels = distinct(movie.versions.flatMap((v) => v.tags)).map((id) => lookups.tags.get(id) ?? `#${id}`)
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
    case 'plays': {
      const total = <span className={cn('tabular-nums', movie.plays === 0 && 'text-muted-foreground')}>{movie.plays}</span>
      if (split === 'under') {
        return (
          <span className="flex flex-col leading-tight">
            {total}
            <PlaysSplit movie={movie} emphasis={playsEmphasis} />
          </span>
        )
      }
      if (split === 'inline') {
        return (
          <span className="flex min-w-0 items-baseline gap-2">
            <span className="min-w-[2rem] shrink-0">{total}</span>
            <PlaysSplit movie={movie} emphasis={playsEmphasis} className="min-w-0" />
          </span>
        )
      }
      return total
    }
    case 'lastPlayed': {
      // Sorted by one version's last play, the cell shows that version's.
      const at = lastPlayedOf === null ? movie.lastPlayedAt : (movie.versions.find((v) => v.name === lastPlayedOf)?.lastPlayedAt ?? null)
      return <span className="tabular-nums text-muted-foreground">{at === null ? t('library.neverPlayed') : formatDate(at)}</span>
    }
    case 'rootFolder':
      return (
        <span className="text-muted-foreground" title={distinct(movie.versions.map((v) => lookups.rootFolders.get(v.rootFolderId) ?? '')).join('\n')}>
          {distinct(movie.versions.map((v) => lookups.rootFolders.get(v.rootFolderId) ?? `#${v.rootFolderId}`)).join(' · ')}
        </span>
      )
  }
}
