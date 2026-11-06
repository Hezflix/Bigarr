import { useRef, type ReactNode } from 'react'
import { Link } from '@tanstack/react-router'
import { useWindowVirtualizer } from '@tanstack/react-virtual'
import { cn } from 'cn'
import { ArrowDownIcon, ArrowUpDownIcon, ArrowUpIcon, ChevronDownIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import type { MovieSummary } from '@/api/client'
import { Poster } from '@/components/Poster'
import { Badge } from '@/components/ui/badge'
import { DropdownMenu, DropdownMenuContent, DropdownMenuLabel, DropdownMenuRadioGroup, DropdownMenuRadioItem, DropdownMenuTrigger } from '@/components/ui/dropdown-menu'
import { formatBytes, formatDate } from '@/i18n'
import { useDocumentOffset, useOverflows } from './layout'
import { sortedVersion, totalSize, type Density, type SortDirection, type SortKey, type TableColumn } from './libraryView'
import type { Selection } from './selection'
import { SelectionMark } from './SelectionMark'
import { SizeSplit, useSortLabel } from './SizeSplit'
import { VersionChip } from './VersionChip'

/** Names behind the ids a summary carries. */
export type Lookups = {
  profiles: ReadonlyMap<number, string>
  tags: ReadonlyMap<number, string>
  rootFolders: ReadonlyMap<number, string>
}

/** Compact rows hold one line of text, with slimmer chips; comfortable rows fit the poster thumbnail. */
export const rowHeight: Record<Density, number> = { compact: 28, comfortable: 52 }

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
}

const sortable: Partial<Record<TableColumn | 'title', SortKey>> = { title: 'title', year: 'year', added: 'added', size: 'size', quality: 'quality', rating: 'rating' }

/** Where the Size cell puts the size per version: under the total, or on its line when the row has room for one line only. */
type Split = 'under' | 'inline' | null

type Props = Selection & {
  movies: MovieSummary[]
  columns: TableColumn[]
  density: Density
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
export function MovieTable({ movies, columns, density, lookups, sort, direction, onSort, sizeSplit, versionNames, ...selection }: Props) {
  const { t } = useTranslation()
  const sortLabel = useSortLabel()
  const versions = versionNames.length > 1
  const split: Split = sizeSplit && versions ? (density === 'comfortable' ? 'under' : 'inline') : null
  const emphasis = sortedVersion(sort)
  const containerRef = useRef<HTMLDivElement>(null)
  const frameRef = useRef<HTMLDivElement>(null)
  const contentRef = useRef<HTMLDivElement>(null)
  const offset = useDocumentOffset(containerRef)
  const overflows = useOverflows(frameRef, contentRef)
  const height = rowHeight[density]
  const virtualizer = useWindowVirtualizer({ count: movies.length, estimateSize: () => height, overscan: 10, scrollMargin: offset })
  // The split needs room: "1080p 39 GB · 4k 82 GB" under the total, or next to it on a compact row.
  const width = (column: TableColumn) => (column === 'size' && split === 'under' ? '11rem' : column === 'size' && split === 'inline' ? '16.5rem' : widths[column])
  const template = [selection.selecting ? '2rem' : null, 'minmax(14rem, 2.5fr)', ...columns.map(width)].filter(Boolean).join(' ')

  // With several versions, the Size header sorts like the others and its chevron picks what it sorts by: the total or
  // one version's size. One button and a menu, however many versions the library has.
  const sizeHeader = () => {
    const current: SortKey = emphasis !== null ? sort : 'size'
    const active = sort === current
    const Arrow = !active ? ArrowUpDownIcon : direction === 'asc' ? ArrowUpIcon : ArrowDownIcon
    return (
      <span key="size" role="columnheader" aria-sort={!active ? 'none' : direction === 'asc' ? 'ascending' : 'descending'} className="flex min-w-0 items-center gap-0.5">
        <button
          type="button"
          className={cn(
            'group/sort -ml-1.5 flex min-w-0 items-center gap-1 rounded-md px-1.5 py-1 text-left outline-none transition-colors hover:bg-accent hover:text-accent-foreground focus-visible:ring-2 focus-visible:ring-ring/50',
            active && 'text-foreground',
          )}
          onClick={() => onSort(current)}
          data-testid="sort-size"
        >
          <span className="truncate">{emphasis !== null ? sortLabel(current) : t('library.columns.size')}</span>
          <Arrow className={cn('size-3 shrink-0', !active && 'opacity-0 group-hover/sort:opacity-50 group-focus-visible/sort:opacity-50')} aria-hidden="true" />
        </button>
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <button
              type="button"
              className="shrink-0 rounded-md p-1 text-muted-foreground/70 outline-none transition-colors hover:bg-accent hover:text-accent-foreground focus-visible:ring-2 focus-visible:ring-ring/50 data-[state=open]:bg-accent data-[state=open]:text-accent-foreground"
              aria-label={t('library.sort.sizeOf')}
              data-testid="sort-size-menu"
            >
              <ChevronDownIcon className="size-3" aria-hidden="true" />
            </button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="start" className="w-44">
            <DropdownMenuLabel className="text-xs font-normal text-muted-foreground">{t('library.sort.sizeOf')}</DropdownMenuLabel>
            <DropdownMenuRadioGroup value={current} onValueChange={(value) => value !== sort && onSort(value as SortKey)}>
              <DropdownMenuRadioItem value="size" data-testid="sort-size-total">
                {t('library.sort.allVersions')}
              </DropdownMenuRadioItem>
              {versionNames.map((name) => (
                <DropdownMenuRadioItem key={name} value={`size:${name}`} data-testid={`sort-size:${name}`}>
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
    if (key === 'size' && versions) return sizeHeader()
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
                lookups={lookups}
                split={split}
                emphasis={emphasis}
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
  lookups,
  split,
  emphasis,
  template,
  style,
  selecting,
  selected,
  onToggle,
}: Selection & {
  movie: MovieSummary
  columns: TableColumn[]
  density: Density
  lookups: Lookups
  split: Split
  emphasis: string | null
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
      <MovieTitleCell movie={movie} density={density} selecting={selecting} />
      {columns.map((column) => (
        <div key={column} role="cell" className="min-w-0 truncate">
          <Cell movie={movie} column={column} lookups={lookups} split={split} emphasis={emphasis} />
        </div>
      ))}
    </div>
  )
}

/**
 * Poster thumbnail (comfortable density) and title. Outside selection mode an empty link is laid over the whole row
 * (the row is its positioned ancestor): a click anywhere opens the film, and ctrl/cmd/middle click or "open in new tab"
 * work like on any link, with no link or button nested in another. The link sits next to the content rather than
 * around it, so the thumbnail stays mounted, and painted, when selection mode toggles.
 */
function MovieTitleCell({ movie, density, selecting }: { movie: MovieSummary; density: Density; selecting: boolean }) {
  return (
    <div role="cell" className="flex min-w-0 items-center gap-2.5">
      {density === 'comfortable' && <Poster url={movie.posterUrl} title={movie.title} size="thumb" className="rounded-sm" />}
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

function Cell({ movie, column, lookups, split, emphasis }: { movie: MovieSummary; column: TableColumn; lookups: Lookups; split: Split; emphasis: string | null }): ReactNode {
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
    case 'rootFolder':
      return (
        <span className="text-muted-foreground" title={distinct(movie.versions.map((v) => lookups.rootFolders.get(v.rootFolderId) ?? '')).join('\n')}>
          {distinct(movie.versions.map((v) => lookups.rootFolders.get(v.rootFolderId) ?? `#${v.rootFolderId}`)).join(' · ')}
        </span>
      )
  }
}
