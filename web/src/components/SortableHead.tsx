import type { ReactNode } from 'react'
import { cn } from 'cn'
import { ArrowDownIcon, ArrowUpIcon } from 'lucide-react'
import { TableHead } from '@/components/ui/table'

/**
 * A column header that sorts its table: a click hands the column to `onSort`, the sorted column shows its direction.
 * A right-aligned column (`className` with `text-right`) keeps its label against the edge, the arrow before it. The
 * button's test id is `<testIdPrefix>-<column>`.
 */
export function SortableHead<K extends string>({
  column,
  sort,
  onSort,
  className,
  title,
  testIdPrefix,
  children,
}: {
  column: K
  sort: { key: K; direction: 'asc' | 'desc' }
  onSort: (key: K) => void
  className?: string
  title?: string
  testIdPrefix: string
  children: ReactNode
}) {
  const active = sort.key === column
  return (
    <TableHead className={className} aria-sort={active ? (sort.direction === 'asc' ? 'ascending' : 'descending') : 'none'}>
      <button
        type="button"
        className={cn('flex items-center gap-1 text-left hover:text-foreground', className?.includes('text-right') && 'flex-row-reverse')}
        onClick={() => onSort(column)}
        title={title}
        data-testid={`${testIdPrefix}-${column}`}
      >
        {children}
        {active && (sort.direction === 'asc' ? <ArrowUpIcon className="size-3" /> : <ArrowDownIcon className="size-3" />)}
      </button>
    </TableHead>
  )
}
