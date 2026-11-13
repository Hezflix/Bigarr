import type { ReactNode } from 'react'
import { Link } from '@tanstack/react-router'
import { CheckSquareIcon, PlusIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Button } from '@/components/ui/button'

type Props = {
  title: string
  /** The formatted count beside the title; null while the library loads. */
  count: string | null
  countTestId: string
  addTo: '/add' | '/series/add'
  addLabel: string
  selecting?: boolean
  onSelect?: () => void
  /** Page-specific actions shown before Select. */
  children?: ReactNode
}

/**
 * Title, count and actions of the film and series libraries. The actions wrap onto more lines on narrow screens,
 * so the row never pushes the page wider than the viewport.
 */
export function LibraryHeader({ title, count, countTestId, addTo, addLabel, selecting = false, onSelect, children }: Props) {
  const { t } = useTranslation()
  return (
    <div className="flex flex-wrap items-center gap-x-3 gap-y-2">
      <h1 className="text-2xl font-semibold tracking-tight">{title}</h1>
      {count !== null && (
        <span className="text-sm text-muted-foreground tabular-nums" data-testid={countTestId}>
          {count}
        </span>
      )}
      <div className="ml-auto flex min-w-0 flex-wrap items-center justify-end gap-2">
        {children}
        {onSelect && (
          <Button type="button" variant={selecting ? 'secondary' : 'outline'} onClick={onSelect} aria-pressed={selecting} data-testid="library-select-toggle">
            <CheckSquareIcon />
            {selecting ? t('bulk.done') : t('library.select')}
          </Button>
        )}
        <Button asChild>
          <Link to={addTo}>
            <PlusIcon />
            {addLabel}
          </Link>
        </Button>
      </div>
    </div>
  )
}
