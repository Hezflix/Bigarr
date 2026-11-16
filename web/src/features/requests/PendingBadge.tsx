import { useQuery } from '@tanstack/react-query'
import { cn } from 'cn'
import { useTranslation } from 'react-i18next'
import { requests } from '@/api/client'

/** How many requests wait for a decision, next to the Requests entry of an admin or a request manager. */
export function PendingBadge({ className }: { className?: string }) {
  const { t } = useTranslation()
  const query = useQuery({ queryKey: ['requests', 'count'], queryFn: requests.count, refetchInterval: 60_000 })
  const pending = query.data?.pending ?? 0
  if (pending === 0) return null
  return (
    <span
      className={cn('ml-auto inline-flex h-5 min-w-5 items-center justify-center rounded-full bg-amber-500/15 px-1.5 text-xs font-medium text-amber-700 tabular-nums dark:text-amber-300', className)}
      data-testid="requests-badge"
    >
      <span aria-hidden="true">{pending}</span>
      <span className="sr-only">{t('requests.pendingBadge', { count: pending })}</span>
    </span>
  )
}
