import { useMemo, useRef } from 'react'
import { useWindowVirtualizer } from '@tanstack/react-virtual'
import { cn } from 'cn'
import { ChevronDownIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import type { MovieSummary, VersionSummary } from '@/api/client'
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from '@/components/ui/dropdown-menu'
import { useDocumentOffset, useOverflows } from './layout'
import type { Density, VersionName } from './libraryView'
import { MovieTitleCell, rowHeight } from './MovieTable'
import type { Selection } from './selection'
import { SelectionMark } from './SelectionMark'
import { VersionChip } from './VersionChip'

type Props = Selection & {
  movies: MovieSummary[]
  names: VersionName[]
  density: Density
  /** Adds the films of the current list that have (or lack) the version to the selection. */
  onSelectWhere: (predicate: (movie: MovieSummary) => boolean) => void
}

/**
 * One row per film, one column per version name: which films lack a 4k, which 1080p files are below the cutoff, at a
 * glance. In selection mode a column header selects the films that have, or lack, that version.
 */
export function VersionMatrix({ movies, names, density, onSelectWhere, ...selection }: Props) {
  const { t } = useTranslation()
  const containerRef = useRef<HTMLDivElement>(null)
  const frameRef = useRef<HTMLDivElement>(null)
  const contentRef = useRef<HTMLDivElement>(null)
  const offset = useDocumentOffset(containerRef)
  const overflows = useOverflows(frameRef, contentRef)
  const height = rowHeight[density]
  const virtualizer = useWindowVirtualizer({ count: movies.length, estimateSize: () => height, overscan: 10, scrollMargin: offset })
  const template = [selection.selecting ? '2rem' : null, 'minmax(14rem, 2fr)', '4rem', ...names.map(() => 'minmax(8.5rem, 1fr)')].filter(Boolean).join(' ')

  const counts = useMemo(() => new Map(names.map(({ name }) => [name, movies.reduce((n, m) => n + (has(m, name) ? 1 : 0), 0)])), [movies, names])

  return (
    <div ref={frameRef} className={cn('rounded-xl border', overflows && 'overflow-x-auto')} data-testid="library-matrix" role="table">
      <div ref={contentRef} className="min-w-fit">
        <div
          role="row"
          className={cn('grid items-center gap-3 border-b bg-muted px-3 py-2 text-xs font-medium text-muted-foreground', !overflows && 'sticky top-0 z-10 rounded-t-xl')}
          style={{ gridTemplateColumns: template }}
        >
          {selection.selecting && <span />}
          <span>{t('library.columns.title')}</span>
          <span>{t('library.columns.year')}</span>
          {names.map(({ name }) => {
            const count = counts.get(name) ?? 0
            const label = (
              <>
                <span className="font-semibold text-foreground">{name}</span>
                <span className="tabular-nums">{t('library.matrix.count', { count, total: movies.length })}</span>
              </>
            )
            if (!selection.selecting) {
              return (
                <span key={name} className="flex items-baseline gap-1.5" data-testid={`matrix-column-${name}`}>
                  {label}
                </span>
              )
            }

            return (
              <DropdownMenu key={name}>
                <DropdownMenuTrigger className="flex items-baseline gap-1.5 rounded-sm text-left outline-none hover:text-foreground focus-visible:ring-2 focus-visible:ring-ring/50" data-testid={`matrix-column-${name}`}>
                  {label}
                  <ChevronDownIcon className="size-3 self-center" />
                </DropdownMenuTrigger>
                <DropdownMenuContent align="start">
                  <DropdownMenuItem onSelect={() => onSelectWhere((m) => has(m, name))} disabled={count === 0} data-testid={`matrix-select-with-${name}`}>
                    {t('library.matrix.selectWith', { name, count })}
                  </DropdownMenuItem>
                  <DropdownMenuItem onSelect={() => onSelectWhere((m) => !has(m, name))} disabled={count === movies.length} data-testid={`matrix-select-without-${name}`}>
                    {t('library.matrix.selectWithout', { name, count: movies.length - count })}
                  </DropdownMenuItem>
                </DropdownMenuContent>
              </DropdownMenu>
            )
          })}
        </div>
        <div ref={containerRef} className="relative" style={{ height: virtualizer.getTotalSize() }}>
          {virtualizer.getVirtualItems().map((item) => {
            const movie = movies[item.index]
            const isSelected = selection.selected.has(movie.id)
            return (
              <div
                key={movie.id}
                role="row"
                data-testid="movie-row"
                data-movie-id={movie.id}
                aria-selected={selection.selecting ? isSelected : undefined}
                onClick={selection.selecting ? (event) => selection.onToggle(movie.id, event.shiftKey) : undefined}
                className={cn(
                  'absolute top-0 left-0 grid w-full items-center gap-3 border-b px-3 text-sm last:border-b-0 hover:bg-accent/40',
                  selection.selecting && 'cursor-pointer select-none',
                  isSelected && 'bg-primary/10 hover:bg-primary/15',
                )}
                style={{ height, gridTemplateColumns: template, transform: `translateY(${item.start - virtualizer.options.scrollMargin}px)` }}
              >
                {selection.selecting && <SelectionMark checked={isSelected} />}
                <MovieTitleCell movie={movie} density={density} selecting={selection.selecting} />
                <span className="tabular-nums text-muted-foreground">{movie.year ?? t('common.none')}</span>
                {names.map(({ name }) => {
                  const version = movie.versions.find((v) => v.name === name)
                  return (
                    <div key={name} className="min-w-0">
                      {version ? <MatrixCell version={version} /> : <span className="text-muted-foreground/50">{t('common.none')}</span>}
                    </div>
                  )
                })}
              </div>
            )
          })}
        </div>
      </div>
    </div>
  )
}

const has = (movie: MovieSummary, name: string) => movie.versions.some((v) => v.name === name)

/** The quality once there is a file, the status before. */
function MatrixCell({ version }: { version: VersionSummary }) {
  const { t } = useTranslation()
  const label = version.status === 'available' ? (version.quality ?? t('status.available')) : t(`status.${version.status}`)
  return <VersionChip version={version} label={label} className="max-w-full truncate" />
}
