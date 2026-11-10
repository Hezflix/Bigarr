import { useEffect, useRef, type ReactNode } from 'react'
import { Link } from '@tanstack/react-router'
import { useWindowVirtualizer } from '@tanstack/react-virtual'
import { cn } from 'cn'
import { ArrowDownIcon, ArrowUpDownIcon, ArrowUpIcon, ChevronDownIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import type { MovieSummary } from '@/api/client'
import { Poster, type PosterSize as PosterImage } from '@/components/Poster'
import { Badge } from '@/components/ui/badge'
import { DropdownMenu, DropdownMenuContent, DropdownMenuLabel, DropdownMenuRadioGroup, DropdownMenuRadioItem, DropdownMenuTrigger } from '@/components/ui/dropdown-menu'
import { formatBytes, formatDate } from '@/i18n'
import { useDocumentOffset, useOverflows } from './layout'
import { sortedByVersion, totalSize, type Density, type PosterSize, type SortDirection, type SortKey, type TableColumn, type VersionMetric } from './libraryView'
import type { Selection } from './selection'
import { SelectionMark } from './SelectionMark'
import { PlaysSplit, SizeSplit, useSortLabel } from './SizeSplit'
import { VersionChip } from './VersionChip'

/** Names behind the ids a summary carries. */
export type Lookups = {
  profiles: ReadonlyMap<number, string>
  tags: ReadonlyMap<number, string>
  rootFolders: ReadonlyMap<number, string>
}

/** The thumbnail of comfortable rows at each poster size, and its width in px. */
const thumbnails: Record<PosterSize, { image: PosterImage; width: number }> = {
  small: { image: 'thumb', width: 28 },
  medium: { image: 'thumbMedium', width: 40 },
  large: { image: 'thumbLarge', width: 56 },
}

/** Compact rows hold one line of text, with slimmer chips; comfortable rows fit the 2:3 thumbnail with 5 px above and below. */
export function rowHeight(density: Density, posterSize: PosterSize): number {
  return density === 'compact' ? 28 : thumbnails[posterSize].width * 1.5 + 10
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

const sortable: Partial<Record<TableColumn | 'title', SortKey>> = {
  title: 'title',
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
  const containerRef = useRef<HTMLDivElement>(null)
  const frameRef = useRef<HTMLDivElement>(null)
  const contentRef = useRef<HTMLDivElement>(null)
  const offset = useDocumentOffset(containerRef)
  const overflows = useOverflows(frameRef, contentRef)
  const height = rowHeight(density, posterSize)
  const virtualizer = useWindowVirtualizer({ count: movies.length, estimateSize: () => height, overscan: 10, scrollMargin: offset })
  // The virtualizer keeps the row positions it computed: a new row height has to move every row.
  useEffect(() => virtualizer.measure(), [virtualizer, height])
  // The split needs room: "1080p 39 GB · 4k 82 GB" under the total, or next to it on a compact row.
  const width = (column: TableColumn) => {
    if (column === 'size' && split !== null) return split === 'under' ? '11rem' : '16.5rem'
    if (column === 'plays' && split !== null) return split === 'under' ? '8.5rem' : '12rem'
    return widths[column]
  }
  const template = [selection.selecting ? '2rem' : null, 'minmax(14rem, 2.5fr)', ...columns.map(width)].filter(Boolean).join(' ')

  // With several versions, the Size, Plays and Last played headers sort like the others and their chevron picks what
  // they sort by: the film's value or one version's. One button and a menu, however many versions the library has.
  const metricHeader = (metric: VersionMetric) => {
    const current: SortKey = sortedBy?.metric === metric ? sort : metric
    const active = sort === current
    const Arrow = !active ? ArrowUpDownIcon : direction === 'asc' ? ArrowUpIcon : ArrowDownIcon
    return (
      <span key={metric} role="columnheader" aria-sort={!active ? 'none' : direction === 'asc' ? 'ascending' : 'descending'} className="flex min-w-0 items-center gap-0.5">
        <button
          type="button"
          className={cn(
            'group/sort -ml-1.5 flex min-w-0 items-center gap-1 rounded-md px-1.5 py-1 text-left outline-none transition-colors hover:bg-accent hover:text-accent-foreground focus-visible:ring-2 focus-visible:ring-ring/50',
            active && 'text-foreground',
          )}
          onClick={() => onSort(current)}
          data-testid={`sort-${metric}`}
        >
          <span className="truncate">{current !== metric ? sortLabel(current) : t(`library.columns.${metric}`)}</span>
          <Arrow className={cn('size-3 shrink-0', !active && 'opacity-0 group-hover/sort:opacity-50 group-focus-visible/sort:opacity-50')} aria-hidden="true" />
        </button>
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <button
              type="button"
              className="shrink-0 rounded-md p-1 text-muted-foreground/70 outline-none transition-colors hover:bg-accent hover:text-accent-foreground focus-visible:ring-2 focus-visible:ring-ring/50 data-[state=open]:bg-accent data-[state=open]:text-accent-foreground"
              aria-label={t(`library.sort.${metric}Of`)}
              data-testid={`sort-${metric}-menu`}
            >
              <ChevronDownIcon className="size-3" aria-hidden="true" />
            </button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="start" className="w-44">
            <DropdownMenuLabel className="text-xs font-normal text-muted-foreground">{t(`library.sort.${metric}Of`)}</DropdownMenuLabel>
            <DropdownMenuRadioGroup value={current} onValueChange={(value) => value !== sort && onSort(value as SortKey)}>
              <DropdownMenuRadioItem value={metric} data-testid={`sort-${metric}-total`}>
                {t('library.sort.allVersions')}
              </DropdownMenuRadioItem>
              {versionNames.map((name) => (
                <DropdownMenuRadioItem key={name} value={`${metric}:${name}`} data-testid={`sort-${metric}:${name}`}>
                  {name}
                </DropdownMenuRadioItem>
              ))}
            </DropdownMenuRadioGroup>
          </DropdownMenuContent>
        </DropdownMenu>
      </span>
    )
  }

  const header = (key: TableColumn | 'title', label: string) => {
    const metric = key === 'title' ? undefined : perVersion[key]
    if (metric && versions) return metricHeader(metric)
    const sortKey = sortable[key]
    if (!sortKey) {
      return (
        <span key={key} role="columnheader" className="truncate">
          {label}
        </span>
      )
    }
    const active = sort === sortKey
    // The sorted column shows its direction; the others show a faint hint on hover that they sort too.
    const Arrow = !active ? ArrowUpDownIcon : direction === 'asc' ? ArrowUpIcon : ArrowDownIcon
    return (
      <span key={key} role="columnheader" aria-sort={active ? (direction === 'asc' ? 'ascending' : 'descending') : 'none'} className="min-w-0">
        <button
          type="button"
          className={cn(
            'group/sort -mx-1.5 flex max-w-[calc(100%+0.75rem)] items-center gap-1 rounded-md px-1.5 py-1 text-left outline-none transition-colors hover:bg-accent hover:text-accent-foreground focus-visible:ring-2 focus-visible:ring-ring/50',
            active && 'text-foreground',
          )}
          onClick={() => onSort(sortKey)}
          data-testid={`sort-${sortKey}`}
        >
          <span className="truncate">{label}</span>
          <Arrow className={cn('size-3 shrink-0', !active && 'opacity-0 group-hover/sort:opacity-50 group-focus-visible/sort:opacity-50')} aria-hidden="true" />
        </button>
      </span>
    )
  }

  return (
    <div ref={frameRef} className={cn('rounded-xl border', overflows && 'overflow-x-auto')} data-testid="library-table" role="table">
      <div ref={contentRef} className="min-w-fit">
        <div
          role="row"
          className={cn(
            'grid items-center gap-3 border-b bg-muted px-3 text-xs font-medium text-muted-foreground',
            density === 'compact' ? 'py-0.5' : 'py-1',
            !overflows && 'sticky top-0 z-10 rounded-t-xl',
          )}
          style={{ gridTemplateColumns: template }}
        >
          {selection.selecting && <span />}
          {header('title', t('library.columns.title'))}
          {columns.map((c) => header(c, t(`library.columns.${c}`)))}
        </div>
        <div ref={containerRef} className="relative" style={{ height: virtualizer.getTotalSize() }}>
          {virtualizer.getVirtualItems().map((item) => {
            const movie = movies[item.index]
            return (
              <TableRowView
                key={movie.id}
                movie={movie}
                columns={columns}
                density={density}
                posterSize={posterSize}
                lookups={lookups}
                split={split}
                emphasis={emphasis}
                playsEmphasis={playsEmphasis}
                lastPlayedOf={lastPlayedOf}
                template={template}
                style={{ height, transform: `translateY(${item.start - virtualizer.options.scrollMargin}px)` }}
                {...selection}
              />
            )
          })}
        </div>
      </div>
    </div>
  )
}

function TableRowView({
  movie,
  columns,
  density,
  posterSize,
  lookups,
  split,
  emphasis,
  playsEmphasis,
  lastPlayedOf,
  template,
  style,
  selecting,
  selected,
  onToggle,
}: Selection & {
  movie: MovieSummary
  columns: TableColumn[]
  density: Density
  posterSize: PosterSize
  lookups: Lookups
  split: Split
  emphasis: string | null
  playsEmphasis: string | null
  lastPlayedOf: string | null
  template: string
  style: React.CSSProperties
}) {
  const isSelected = selected.has(movie.id)
  return (
    <div
      role="row"
      data-testid="movie-row"
      data-movie-id={movie.id}
      aria-selected={selecting ? isSelected : undefined}
      onClick={selecting ? (event) => onToggle(movie.id, event.shiftKey) : undefined}
      className={cn(
        'group/row absolute top-0 left-0 grid w-full items-center gap-3 border-b px-3 text-sm last:border-b-0 hover:bg-accent/40',
        // Compact: the version and tag chips lose their vertical padding to fit the shorter row.
        density === 'compact' && '[&_[data-slot=badge]]:py-0',
        selecting && 'cursor-pointer select-none',
        isSelected && 'bg-primary/10 hover:bg-primary/15',
      )}
      style={{ ...style, gridTemplateColumns: template }}
    >
      {selecting && <SelectionMark checked={isSelected} />}
      <MovieTitleCell movie={movie} image={density === 'comfortable' ? thumbnails[posterSize].image : null} selecting={selecting} />
      {columns.map((column) => (
        <div key={column} role="cell" className="min-w-0 truncate">
          <Cell movie={movie} column={column} lookups={lookups} split={split} emphasis={emphasis} playsEmphasis={playsEmphasis} lastPlayedOf={lastPlayedOf} />
        </div>
      ))}
    </div>
  )
}

/**
 * Poster thumbnail (comfortable density, sized by the poster size) and title. Outside selection mode an empty link is
 * laid over the whole row (the row is its positioned ancestor): a click anywhere opens the film, and ctrl/cmd/middle
 * click or "open in new tab" work like on any link, with no link or button nested in another. The link sits next to the
 * content rather than around it, so the thumbnail stays mounted, and painted, when selection mode toggles.
 */
function MovieTitleCell({ movie, image, selecting }: { movie: MovieSummary; image: PosterImage | null; selecting: boolean }) {
  return (
    <div role="cell" className="flex min-w-0 items-center gap-2.5">
      {image && <Poster url={movie.posterUrl} title={movie.title} size={image} className="rounded-sm" />}
      <span className={cn('truncate font-medium', !selecting && 'underline-offset-4 group-hover/row:underline')}>{movie.title}</span>
      {!selecting && (
        <Link
          to="/movies/$movieId"
          params={{ movieId: String(movie.id) }}
          aria-label={movie.title}
          className="absolute inset-0 outline-none focus-visible:ring-2 focus-visible:ring-ring/50 focus-visible:ring-inset"
        />
      )}
    </div>
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
            <SizeSplit movie={movie} emphasis={emphasis} />
          </span>
        )
      }
      if (split === 'inline') {
        // The total keeps a fixed width so the splits line up from row to row.
        return (
          <span className="flex min-w-0 items-baseline gap-2">
            <span className="min-w-[4.5rem] shrink-0">{total}</span>
            <SizeSplit movie={movie} emphasis={emphasis} className="min-w-0" />
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
