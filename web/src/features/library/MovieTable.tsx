import { useRef, type ReactNode } from 'react'
import { Link } from '@tanstack/react-router'
import { useWindowVirtualizer } from '@tanstack/react-virtual'
import { cn } from 'cn'
import { ArrowDownIcon, ArrowUpIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import type { MovieSummary } from '@/api/client'
import { Poster } from '@/components/Poster'
import { Badge } from '@/components/ui/badge'
import { formatBytes, formatDate } from '@/i18n'
import { useDocumentOffset, useOverflows } from './layout'
import { totalSize, type Density, type SortDirection, type SortKey, type TableColumn } from './libraryView'
import type { Selection } from './selection'
import { SelectionMark } from './SelectionMark'
import { VersionChip } from './VersionChip'

/** Names behind the ids a summary carries. */
export type Lookups = {
  profiles: ReadonlyMap<number, string>
  tags: ReadonlyMap<number, string>
  rootFolders: ReadonlyMap<number, string>
}

export const rowHeight: Record<Density, number> = { compact: 36, comfortable: 52 }

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

type Props = Selection & {
  movies: MovieSummary[]
  columns: TableColumn[]
  density: Density
  lookups: Lookups
  sort: SortKey
  direction: SortDirection
  onSort: (key: SortKey) => void
}

/** Radarr's table view: one row per film, columns picked in the view menu, headers sort. */
export function MovieTable({ movies, columns, density, lookups, sort, direction, onSort, ...selection }: Props) {
  const { t } = useTranslation()
  const containerRef = useRef<HTMLDivElement>(null)
  const frameRef = useRef<HTMLDivElement>(null)
  const contentRef = useRef<HTMLDivElement>(null)
  const offset = useDocumentOffset(containerRef)
  const overflows = useOverflows(frameRef, contentRef)
  const height = rowHeight[density]
  const virtualizer = useWindowVirtualizer({ count: movies.length, estimateSize: () => height, overscan: 10, scrollMargin: offset })
  const template = [selection.selecting ? '2rem' : null, 'minmax(14rem, 2.5fr)', ...columns.map((c) => widths[c])].filter(Boolean).join(' ')

  const header = (key: TableColumn | 'title', label: string) => {
    const sortKey = sortable[key]
    if (!sortKey) return <span key={key}>{label}</span>
    return (
      <button key={key} type="button" className="flex items-center gap-1 text-left hover:text-foreground" onClick={() => onSort(sortKey)}>
        {label}
        {sort === sortKey && (direction === 'asc' ? <ArrowUpIcon className="size-3" /> : <ArrowDownIcon className="size-3" />)}
      </button>
    )
  }

  return (
    <div ref={frameRef} className={cn('rounded-xl border', overflows && 'overflow-x-auto')} data-testid="library-table" role="table">
      <div ref={contentRef} className="min-w-fit">
        <div
          role="row"
          className={cn('grid items-center gap-3 border-b bg-muted px-3 py-2 text-xs font-medium text-muted-foreground', !overflows && 'sticky top-0 z-10 rounded-t-xl')}
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
  template,
  style,
  selecting,
  selected,
  onToggle,
}: Selection & { movie: MovieSummary; columns: TableColumn[]; density: Density; lookups: Lookups; template: string; style: React.CSSProperties }) {
  const isSelected = selected.has(movie.id)
  return (
    <div
      role="row"
      data-testid="movie-row"
      data-movie-id={movie.id}
      aria-selected={selecting ? isSelected : undefined}
      onClick={selecting ? (event) => onToggle(movie.id, event.shiftKey) : undefined}
      className={cn(
        'absolute top-0 left-0 grid w-full items-center gap-3 border-b px-3 text-sm last:border-b-0 hover:bg-accent/40',
        selecting && 'cursor-pointer select-none',
        isSelected && 'bg-primary/10 hover:bg-primary/15',
      )}
      style={{ ...style, gridTemplateColumns: template }}
    >
      {selecting && <SelectionMark checked={isSelected} />}
      <MovieTitleCell movie={movie} density={density} selecting={selecting} />
      {columns.map((column) => (
        <div key={column} className="min-w-0 truncate">
          <Cell movie={movie} column={column} lookups={lookups} />
        </div>
      ))}
    </div>
  )
}

/**
 * Poster thumbnail (comfortable density) and title; a link outside selection mode. The link is laid over the cell
 * rather than wrapped around it, so the thumbnail stays mounted, and painted, when selection mode toggles.
 */
export function MovieTitleCell({ movie, density, selecting }: { movie: MovieSummary; density: Density; selecting: boolean }) {
  return (
    <div className="group/title relative flex min-w-0 items-center gap-2.5">
      {density === 'comfortable' && <Poster url={movie.posterUrl} title={movie.title} size="thumb" className="rounded-sm" />}
      <span className={cn('truncate font-medium', !selecting && 'underline-offset-4 group-hover/title:underline')}>{movie.title}</span>
      {!selecting && <Link to="/movies/$movieId" params={{ movieId: String(movie.id) }} aria-label={movie.title} className="absolute inset-0" />}
    </div>
  )
}

function Cell({ movie, column, lookups }: { movie: MovieSummary; column: TableColumn; lookups: Lookups }): ReactNode {
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
      return <span className="tabular-nums">{size > 0 ? formatBytes(size) : <span className="text-muted-foreground">{t('common.none')}</span>}</span>
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
