import { useEffect, useRef } from 'react'
import { Link } from '@tanstack/react-router'
import { useWindowVirtualizer } from '@tanstack/react-virtual'
import { cn } from 'cn'
import { HeartIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import type { MovieSummary } from '@/api/client'
import { Poster } from '@/components/Poster'
import { Card, CardContent } from '@/components/ui/card'
import { formatBytes } from '@/i18n'
import { useDocumentOffset, useElementWidth } from './layout'
import { totalSize, type CardField, type Density, type PosterSize } from './libraryView'
import type { Selection } from './selection'
import { SelectionMark } from './SelectionMark'
import { VersionChip } from './VersionChip'

/** Narrowest a card may get before the grid drops a column. */
const minCardWidth: Record<PosterSize, number> = { small: 112, medium: 152, large: 208 }

type Props = Selection & {
  movies: MovieSummary[]
  posterSize: PosterSize
  density: Density
  fields: CardField[]
}

/** Rows of posters, only the rows near the viewport mounted: 5000 films scroll like 50. */
export function PosterGrid({ movies, posterSize, density, fields, ...selection }: Props) {
  const containerRef = useRef<HTMLDivElement>(null)
  const width = useElementWidth(containerRef)
  const offset = useDocumentOffset(containerRef)
  const gap = density === 'compact' ? 8 : 16
  const columns = Math.max(1, Math.floor((width + gap) / (minCardWidth[posterSize] + gap)))
  const cardWidth = (width - gap * (columns - 1)) / columns
  const rows = Math.ceil(movies.length / columns)

  const virtualizer = useWindowVirtualizer({
    count: width > 0 ? rows : 0,
    // Poster (2:3) plus the text block; rows are measured once rendered, this only places the first frame.
    estimateSize: () => cardWidth * 1.5 + (fields.length === 0 ? 0 : 24 + fields.length * 18) + gap,
    overscan: 3,
    scrollMargin: offset,
  })

  // A new column count reshuffles every row: forget the measured heights.
  useEffect(() => virtualizer.measure(), [virtualizer, columns, fields.length, density])

  return (
    <div ref={containerRef} data-testid="library-grid">
      <div className="relative w-full" style={{ height: virtualizer.getTotalSize() }}>
        {virtualizer.getVirtualItems().map((row) => (
          <div
            key={row.key}
            data-index={row.index}
            ref={virtualizer.measureElement}
            className="absolute top-0 left-0 grid w-full"
            style={{
              transform: `translateY(${row.start - virtualizer.options.scrollMargin}px)`,
              gridTemplateColumns: `repeat(${columns}, minmax(0, 1fr))`,
              columnGap: gap,
              paddingBottom: gap,
            }}
          >
            {movies.slice(row.index * columns, (row.index + 1) * columns).map((movie) => (
              <MovieCard key={movie.id} movie={movie} density={density} posterSize={posterSize} fields={fields} {...selection} />
            ))}
          </div>
        ))}
      </div>
    </div>
  )
}

function MovieCard({ movie, density, posterSize, fields, selecting, selected, onToggle }: Selection & { movie: MovieSummary; density: Density; posterSize: PosterSize; fields: CardField[] }) {
  const isSelected = selected.has(movie.id)
  const card = (
    <Card
      className={cn(
        'relative h-full gap-0 overflow-hidden p-0 transition-colors group-hover:border-ring/40 group-hover:bg-accent/40',
        isSelected && 'border-primary ring-2 ring-primary/60',
      )}
    >
      <Poster url={movie.posterUrl} title={movie.title} size="grid" className="rounded-none" />
      {selecting && (
        <SelectionMark checked={isSelected} className={cn('absolute top-2 left-2 size-5', !isSelected && 'bg-background/90')} />
      )}
      {fields.length > 0 && <CardInfo movie={movie} density={density} posterSize={posterSize} fields={fields} />}
    </Card>
  )

  if (selecting) {
    return (
      <button
        type="button"
        aria-pressed={isSelected}
        data-testid="movie-card"
        data-movie-id={movie.id}
        onClick={(event) => onToggle(movie.id, event.shiftKey)}
        className="group rounded-xl text-left focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
      >
        {card}
      </button>
    )
  }

  return (
    <Link
      to="/movies/$movieId"
      params={{ movieId: String(movie.id) }}
      data-testid="movie-card"
      data-movie-id={movie.id}
      className="group rounded-xl focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
    >
      {card}
    </Link>
  )
}

function CardInfo({ movie, density, posterSize, fields }: { movie: MovieSummary; density: Density; posterSize: PosterSize; fields: CardField[] }) {
  const { t } = useTranslation()
  const show = (field: CardField) => fields.includes(field)
  const size = totalSize(movie)
  const rating = movie.tmdbRating ?? movie.imdbRating
  const qualities = [...new Set(movie.versions.map((v) => v.quality).filter((q): q is string => q !== null))]
  const meta = [
    show('year') ? (movie.year ?? t('common.none')) : null,
    show('size') && size > 0 ? formatBytes(size) : null,
  ].filter((part) => part !== null)

  return (
    <CardContent className={cn('flex flex-col gap-1', density === 'compact' ? 'px-2 py-1.5' : 'px-3 py-2.5')}>
      {show('title') && (
        <span className={cn('leading-snug font-medium', posterSize === 'small' || density === 'compact' ? 'line-clamp-1 text-xs' : 'line-clamp-2 text-sm')} title={movie.title}>
          {movie.title}
        </span>
      )}
      {(meta.length > 0 || (show('rating') && rating !== null)) && (
        <span className="flex items-center gap-1.5 text-xs text-muted-foreground tabular-nums">
          {meta.join(' · ')}
          {show('rating') && rating !== null && (
            <span className="ml-auto inline-flex items-center gap-0.5" title={t('library.columns.rating')}>
              <HeartIcon className="size-3 fill-current" aria-hidden="true" />
              {rating.toFixed(1)}
            </span>
          )}
        </span>
      )}
      {show('quality') && <span className="truncate text-xs text-muted-foreground">{qualities.length > 0 ? qualities.join(' · ') : t('library.noFile')}</span>}
      {show('versions') && movie.versions.length > 0 && (
        <div className="mt-0.5 flex flex-wrap gap-1">
          {movie.versions.map((v) => (
            <VersionChip key={v.id} version={v} />
          ))}
        </div>
      )}
    </CardContent>
  )
}
