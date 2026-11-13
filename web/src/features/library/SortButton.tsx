import type { ReactNode } from 'react'
import { cn } from 'cn'
import { ArrowDownIcon, ArrowUpDownIcon, ArrowUpIcon } from 'lucide-react'
import type { SortDirection } from './core'

/**
 * A column header's sort button: the sorted column shows its direction, the others a faint hint on hover that they
 * sort too. The caller's `th` carries `aria-sort` (see {@link ariaSort}).
 */
export function SortButton({ active, direction, onClick, testId, title, children }: { active: boolean; direction: SortDirection; onClick: () => void; testId: string; title?: string; children: ReactNode }) {
  const Arrow = !active ? ArrowUpDownIcon : direction === 'asc' ? ArrowUpIcon : ArrowDownIcon
  return (
    <button
      type="button"
      className={cn(
        'group/sort -mx-1.5 flex max-w-[calc(100%+0.75rem)] items-center gap-1 rounded-md px-1.5 py-1 text-left outline-none transition-colors hover:bg-accent hover:text-accent-foreground focus-visible:ring-2 focus-visible:ring-ring/50',
        active && 'text-foreground',
      )}
      onClick={onClick}
      title={title}
      data-testid={testId}
    >
      <span className="truncate">{children}</span>
      <Arrow className={cn('size-3 shrink-0', !active && 'opacity-0 group-hover/sort:opacity-50 group-focus-visible/sort:opacity-50')} aria-hidden="true" />
    </button>
  )
}

export const ariaSort = (active: boolean, direction: SortDirection) => (active ? (direction === 'asc' ? 'ascending' : 'descending') : 'none')
