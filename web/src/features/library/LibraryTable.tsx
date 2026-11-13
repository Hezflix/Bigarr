import { useEffect, useRef, type ReactNode, type Ref } from 'react'
import { useWindowVirtualizer } from '@tanstack/react-virtual'
import { cn } from 'cn'
import { ArrowDownIcon, ArrowUpDownIcon, ArrowUpIcon, ChevronDownIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Poster, type PosterSize as PosterImage } from '@/components/Poster'
import { DropdownMenu, DropdownMenuContent, DropdownMenuLabel, DropdownMenuRadioGroup, DropdownMenuRadioItem, DropdownMenuTrigger } from '@/components/ui/dropdown-menu'
import type { Density, LibraryItem, PosterSize, SortDirection } from './core'
import { useDocumentOffset, useOverflows } from './layout'
import type { Selection } from './selection'
import { SelectionMark } from './SelectionMark'
import { ariaSort, SortButton } from './SortButton'

/** The thumbnail of comfortable rows at each poster size, and its width in px. */
export const thumbnails: Record<PosterSize, { image: PosterImage; width: number }> = {
  small: { image: 'thumb', width: 28 },
  medium: { image: 'thumbMedium', width: 40 },
  large: { image: 'thumbLarge', width: 56 },
}

/** Compact rows hold one line of text, with slimmer chips; comfortable rows fit the 2:3 thumbnail with 5 px above and below. */
export function rowHeight(density: Density, posterSize: PosterSize): number {
  return density === 'compact' ? 28 : thumbnails[posterSize].width * 1.5 + 10
}

/** The link laid over a row or a card: the library page knows where its items open. */
export type ItemLink<T> = (item: T, props: { ref?: Ref<HTMLAnchorElement>; className: string; 'aria-label': string; title?: string }) => ReactNode

export type TableColumnSpec<C extends string> = {
  id: C
  label: string
  width: string
  /** The sort the header sets, when it sorts. */
  sortKey?: string
  /**
   * The header also sorts by one version's value, through a menu, when the library has several versions: `size` sorts
   * by the total, `size:4k` by the 4k file's.
   */
  metric?: string
}

type Props<T extends LibraryItem, C extends string> = Selection & {
  items: T[]
  columns: TableColumnSpec<C>[]
  density: Density
  posterSize: PosterSize
  sort: string
  direction: SortDirection
  onSort: (key: string) => void
  /** The library's version names: with more than one, a metric header can sort by one version's value. */
  versionNames: string[]
  /** The metric a per-version sort is about (`size` for `size:4k`), if it is one. */
  sortedMetric: string | null
  /** "Size · 4k": the label of a per-version sort, shown in its header. */
  sortLabel: (key: string) => string
  /** "Sort by the size of": the label of a metric header's menu. */
  metricMenuLabel: (metric: string) => string
  renderCell: (item: T, column: C) => ReactNode
  link: ItemLink<T>
  /** `movie` → rows `movie-row` with `data-movie-id`. */
  testId: string
}

/**
 * Radarr's table view, for films and series: one row per item, columns picked in the view menu, headers sort, a row
 * opens its item. Only the rows near the viewport are mounted.
 */
export function LibraryTable<T extends LibraryItem, C extends string>({
  items,
  columns,
  density,
  posterSize,
  sort,
  direction,
  onSort,
  versionNames,
  sortedMetric,
  sortLabel,
  metricMenuLabel,
  renderCell,
  link,
  testId,
  ...selection
}: Props<T, C>) {
  const { t } = useTranslation()
  const versions = versionNames.length > 1
  const containerRef = useRef<HTMLTableSectionElement>(null)
  const frameRef = useRef<HTMLDivElement>(null)
  const contentRef = useRef<HTMLTableElement>(null)
  const offset = useDocumentOffset(containerRef)
  const overflows = useOverflows(frameRef, contentRef)
  const height = rowHeight(density, posterSize)
  const virtualizer = useWindowVirtualizer({ count: items.length, estimateSize: () => height, overscan: 10, scrollMargin: offset })
  // The virtualizer keeps the row positions it computed: a new row height has to move every row.
  // biome-ignore lint/correctness/useExhaustiveDependencies: height is the trigger
  useEffect(() => virtualizer.measure(), [virtualizer, height])
  const template = [selection.selecting ? '2rem' : null, 'minmax(14rem, 2.5fr)', ...columns.map((c) => c.width)].filter(Boolean).join(' ')

  // With several versions, the metric headers sort like the others and their chevron picks what they sort by: the
  // item's value or one version's. One button and a menu, however many versions the library has.
  const metricHeader = (column: TableColumnSpec<C>, metric: string) => {
    const current = sortedMetric === metric ? sort : metric
    const active = sort === current
    const Arrow = !active ? ArrowUpDownIcon : direction === 'asc' ? ArrowUpIcon : ArrowDownIcon
    return (
      <th key={column.id} scope="col" aria-sort={!active ? 'none' : direction === 'asc' ? 'ascending' : 'descending'} className="flex min-w-0 items-center gap-0.5 text-left font-medium">
        <button
          type="button"
          className={cn(
            'group/sort -ml-1.5 flex min-w-0 items-center gap-1 rounded-md px-1.5 py-1 text-left outline-none transition-colors hover:bg-accent hover:text-accent-foreground focus-visible:ring-2 focus-visible:ring-ring/50',
            active && 'text-foreground',
          )}
          onClick={() => onSort(current)}
          data-testid={`sort-${metric}`}
        >
          <span className="truncate">{current !== metric ? sortLabel(current) : column.label}</span>
          <Arrow className={cn('size-3 shrink-0', !active && 'opacity-0 group-hover/sort:opacity-50 group-focus-visible/sort:opacity-50')} aria-hidden="true" />
        </button>
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <button
              type="button"
              className="shrink-0 rounded-md p-1 text-muted-foreground/70 outline-none transition-colors hover:bg-accent hover:text-accent-foreground focus-visible:ring-2 focus-visible:ring-ring/50 data-[state=open]:bg-accent data-[state=open]:text-accent-foreground"
              aria-label={metricMenuLabel(metric)}
              data-testid={`sort-${metric}-menu`}
            >
              <ChevronDownIcon className="size-3" aria-hidden="true" />
            </button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="start" className="w-44">
            <DropdownMenuLabel className="text-xs font-normal text-muted-foreground">{metricMenuLabel(metric)}</DropdownMenuLabel>
            <DropdownMenuRadioGroup value={current} onValueChange={(value) => value !== sort && onSort(value)}>
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
      </th>
    )
  }

  const header = (column: TableColumnSpec<C> | { id: 'title'; label: string; sortKey: string; metric?: undefined }) => {
    if (column.metric && versions) return metricHeader(column as TableColumnSpec<C>, column.metric)
    const sortKey = column.sortKey
    if (!sortKey) {
      return (
        <th key={column.id} scope="col" className="truncate text-left font-medium">
          {column.label}
        </th>
      )
    }
    const active = sort === sortKey
    return (
      <th key={column.id} scope="col" aria-sort={ariaSort(active, direction)} className="min-w-0 text-left font-medium">
        <SortButton active={active} direction={direction} onClick={() => onSort(sortKey)} testId={`sort-${sortKey}`}>
          {column.label}
        </SortButton>
      </th>
    )
  }

  // A real table laid out as a grid: the header row and every row share one column template, and the virtualized rows
  // are positioned in the body. Table elements keep their roles with any display.
  return (
    <div ref={frameRef} className={cn('rounded-xl border', overflows && 'overflow-x-auto')} data-testid="library-table">
      <table ref={contentRef} className="block min-w-fit">
        {/* Sticky on the head, not the row: a sticky box cannot leave its parent, and the row's parent is the head. */}
        <thead className={cn('block', !overflows && 'sticky top-0 z-10')}>
          <tr
            className={cn(
              'grid items-center gap-3 border-b bg-muted px-3 text-xs font-medium text-muted-foreground',
              density === 'compact' ? 'py-0.5' : 'py-1',
              !overflows && 'rounded-t-xl',
            )}
            style={{ gridTemplateColumns: template }}
          >
            {selection.selecting && <td />}
            {header({ id: 'title', label: t('library.columns.title'), sortKey: 'title' })}
            {columns.map((c) => header(c))}
          </tr>
        </thead>
        <tbody ref={containerRef} className="relative block" style={{ height: virtualizer.getTotalSize() }}>
          {virtualizer.getVirtualItems().map((row) => {
            const item = items[row.index]
            return (
              <TableRowView
                key={item.id}
                item={item}
                columns={columns}
                density={density}
                posterSize={posterSize}
                renderCell={renderCell}
                link={link}
                testId={testId}
                template={template}
                style={{ height, transform: `translateY(${row.start - virtualizer.options.scrollMargin}px)` }}
                {...selection}
              />
            )
          })}
        </tbody>
      </table>
    </div>
  )
}

function TableRowView<T extends LibraryItem, C extends string>({
  item,
  columns,
  density,
  posterSize,
  renderCell,
  link,
  testId,
  template,
  style,
  selecting,
  selected,
  onToggle,
}: Selection & {
  item: T
  columns: TableColumnSpec<C>[]
  density: Density
  posterSize: PosterSize
  renderCell: (item: T, column: C) => ReactNode
  link: ItemLink<T>
  testId: string
  template: string
  style: React.CSSProperties
}) {
  const isSelected = selected.has(item.id)
  return (
    <tr
      data-testid={`${testId}-row`}
      {...{ [`data-${testId}-id`]: item.id }}
      className={cn(
        'group/row absolute top-0 left-0 grid w-full items-center gap-3 border-b px-3 text-sm last:border-b-0 hover:bg-accent/40',
        // Compact: the version and tag chips lose their vertical padding to fit the shorter row.
        density === 'compact' && '[&_[data-slot=badge]]:py-0',
        selecting && 'cursor-pointer select-none',
        isSelected && 'bg-primary/10 hover:bg-primary/15',
      )}
      style={{ ...style, gridTemplateColumns: template }}
    >
      {selecting && (
        <td>
          <SelectionMark checked={isSelected} />
        </td>
      )}
      <TitleCell
        item={item}
        image={density === 'comfortable' ? thumbnails[posterSize].image : null}
        selecting={selecting}
        selected={isSelected}
        onToggle={(shiftKey) => onToggle(item.id, shiftKey)}
        link={link}
      />
      {columns.map((column) => (
        <td key={column.id} className="min-w-0 truncate">
          {renderCell(item, column.id)}
        </td>
      ))}
    </tr>
  )
}

const overlay = 'absolute inset-0 outline-none focus-visible:ring-2 focus-visible:ring-ring/50 focus-visible:ring-inset'

/**
 * Poster thumbnail (comfortable density, sized by the poster size) and title. An empty control is laid over the whole
 * row (the row is its positioned ancestor). Outside selection mode it is a link: a click anywhere opens the item, and
 * ctrl/cmd/middle click or "open in new tab" work like on any link, with no link or button nested in another. In
 * selection mode it is a toggle button, so the row selects from the keyboard too. The control sits next to the content
 * rather than around it, so the thumbnail stays mounted, and painted, when selection mode toggles.
 */
function TitleCell<T extends LibraryItem>({
  item,
  image,
  selecting,
  selected,
  onToggle,
  link,
}: {
  item: T
  image: PosterImage | null
  selecting: boolean
  selected: boolean
  onToggle: (shiftKey: boolean) => void
  link: ItemLink<T>
}) {
  return (
    <td className="flex min-w-0 items-center gap-2.5">
      {image && <Poster url={item.posterUrl} title={item.title} size={image} className="rounded-sm" />}
      <span className={cn('truncate font-medium', !selecting && 'underline-offset-4 group-hover/row:underline')}>{item.title}</span>
      {selecting ? (
        <button type="button" aria-label={item.title} aria-pressed={selected} className={cn(overlay, 'cursor-pointer')} onClick={(event) => onToggle(event.shiftKey)} />
      ) : (
        link(item, { 'aria-label': item.title, className: overlay })
      )}
    </td>
  )
}
