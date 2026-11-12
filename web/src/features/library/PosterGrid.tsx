import { useEffect, useRef, type ReactNode } from 'react'
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
import type { LibraryItem } from './core'
import type { ItemLink } from './LibraryTable'
import { totalSize, type CardField, type Density, type PosterSize } from './libraryView'
import type { Selection } from './selection'
import { SelectionMark } from './SelectionMark'
import { SizeSplit } from './SizeSplit'
import { VersionChip } from './VersionChip'

/** Narrowest a card may get before the grid drops a column. */
const minCardWidth: Record<PosterSize, number> = { small: 112, medium: 152, large: 208 }

type GridProps<T extends LibraryItem> = Selection & {
  items: T[]
  posterSize: PosterSize
  density: Density
  /** Lines of text under the poster, for the first estimate of a row's height (rows are measured once rendered). */
  lines: number
  /** The text under the poster; nothing renders under it when null. */
  renderInfo: (item: T) => ReactNode
  link: ItemLink<T>
  /** `movie` → cards `movie-card` with `data-movie-id`. */
  testId: string
}

/** Rows of posters, only the rows near the viewport mounted: 5000 films scroll like 50. Films and series alike. */
export function LibraryPosterGrid<T extends LibraryItem>({ items, posterSize, density, lines, renderInfo, link, testId, ...selection }: GridProps<T>) {
  const containerRef = useRef<HTMLDivElement>(null)
  const width = useElementWidth(containerRef)
  const offset = useDocumentOffset(containerRef)
  const gap = density === 'compact' ? 8 : 16
  const columns = Math.max(1, Math.floor((width + gap) / (minCardWidth[posterSize] + gap)))
  const cardWidth = (width - gap * (columns - 1)) / columns
  const rows = Math.ceil(items.length / columns)

  const virtualizer = useWindowVirtualizer({
    count: width > 0 ? rows : 0,
    // Poster (2:3) plus the text block; rows are measured once rendered, this only places the first frame.
    estimateSize: () => cardWidth * 1.5 + (lines === 0 ? 0 : 24 + lines * 18) + gap,
    overscan: 3,
    scrollMargin: offset,
  })

  // A new column count reshuffles every row: forget the measured heights.
  // biome-ignore lint/correctness/useExhaustiveDependencies: the layout inputs are the trigger
  useEffect(() => virtualizer.measure(), [virtualizer, columns, lines, density])

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
            {items.slice(row.index * columns, (row.index + 1) * columns).map((item) => (
              <LibraryCard key={item.id} item={item} info={renderInfo(item)} link={link} testId={testId} {...selection} />
            ))}
          </div>
        ))}
      </div>
    </div>
  )
}

type Props = Selection & {
  movies: MovieSummary[]
  posterSize: PosterSize
  density: Density
  fields: CardField[]
  /** Show the size per version under the size. */
  sizeSplit: boolean
  /** The version the list is sorted by: it stands out in the split. */
  emphasis: string | null
}

/** The film library's posters. */
export function PosterGrid({ movies, posterSize, density, fields, sizeSplit, emphasis, ...selection }: Props) {
  return (
    <LibraryPosterGrid
      items={movies}
      posterSize={posterSize}
      density={density}
      lines={fields.length}
      renderInfo={(movie) => (fields.length > 0 ? <CardInfo movie={movie} density={density} posterSize={posterSize} fields={fields} sizeSplit={sizeSplit} emphasis={emphasis} /> : null)}
      link={(movie, props) => <Link to="/movies/$movieId" params={{ movieId: String(movie.id) }} {...props} />}
      testId="movie"
      {...selection}
    />
  )
}

/** Laid over the whole card: a link to the item, or the selection toggle in selection mode. */
const cardControl = 'absolute inset-0 rounded-xl focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring'

function LibraryCard<T extends LibraryItem>({ item, info, link: renderLink, testId, selecting, selected, onToggle }: Selection & { item: T; info: ReactNode; link: ItemLink<T>; testId: string }) {
  const isSelected = selected.has(item.id)
  const link = useRef<HTMLAnchorElement>(null)
  // Only the control over the card changes with selection mode. Wrapping the card in a <button> instead of an <a>
  // would remount it, and every poster <img> with it: each one repainted, visibly, at every toggle.
  return (
    <div className="group relative" data-testid={`${testId}-card`} {...{ [`data-${testId}-id`]: item.id }}>
      {/* Only the version chips rise above the control, for their tooltips: a click on one acts as a click on the card. */}
      <Card
        className={cn(
          'relative h-full gap-0 overflow-hidden p-0 transition-colors group-hover:border-ring/40 group-hover:bg-accent/40',
          isSelected && 'border-primary ring-2 ring-primary/60',
        )}
        onClick={(event) => (selecting ? onToggle(item.id, event.shiftKey) : link.current?.click())}
      >
        <Poster url={item.posterUrl} title={item.title} size="grid" className="rounded-none" />
        {selecting && (
          <SelectionMark checked={isSelected} className={cn('absolute top-2 left-2 size-5', !isSelected && 'bg-background/90')} />
        )}
        {info}
      </Card>
      {selecting ? (
        <button
          type="button"
          aria-pressed={isSelected}
          aria-label={item.title}
          title={item.title}
          onClick={(event) => onToggle(item.id, event.shiftKey)}
          className={cardControl}
        />
      ) : (
        renderLink(item, { ref: link, 'aria-label': item.title, title: item.title, className: cardControl })
      )}
    </div>
  )
}

type CardProps = { movie: MovieSummary; density: Density; posterSize: PosterSize; fields: CardField[]; sizeSplit: boolean; emphasis: string | null }

function CardInfo({ movie, density, posterSize, fields, sizeSplit, emphasis }: CardProps) {
  const { t } = useTranslation()
  const show = (field: CardField) => fields.includes(field)
  const size = totalSize(movie)
  const rating = movie.tmdbRating ?? movie.imdbRating
  const qualities = [...new Set(movie.versions.map((v) => v.quality).filter((q): q is string => q !== null))]
  const meta = [
    show('year') ? (movie.year ?? t('common.none')) : null,
    show('size') && size > 0 ? formatBytes(size) : null,
    show('plays') ? t('library.playsCount', { count: movie.plays }) : null,
  ].filter((part) => part !== null)

  return (
    <CardContent className={cn('flex flex-col gap-1', density === 'compact' ? 'px-2 py-1.5' : 'px-3 py-2.5')}>
      {show('title') && (
        <span className={cn('leading-snug font-medium', posterSize === 'small' || density === 'compact' ? 'line-clamp-1 text-xs' : 'line-clamp-2 text-sm')}>
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
      {sizeSplit && show('size') && <SizeSplit item={movie} emphasis={emphasis} />}
      {show('quality') && <span className="truncate text-xs text-muted-foreground">{qualities.length > 0 ? qualities.join(' · ') : t('library.noFile')}</span>}
      {show('versions') && movie.versions.length > 0 && (
        <div className="pointer-events-none relative z-10 mt-0.5 flex flex-wrap gap-1">
          {movie.versions.map((v) => (
            <VersionChip key={v.id} version={v} className="pointer-events-auto" />
          ))}
        </div>
      )}
    </CardContent>
  )
}
