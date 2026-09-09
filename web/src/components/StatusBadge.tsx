import { cn } from 'cn'
import { useTranslation } from 'react-i18next'
import type { VersionStatus } from '@/api/client'
import { Badge } from '@/components/ui/badge'

/** One colour per version status, reused by the library grid badges. */
export const statusStyles: Record<VersionStatus, string> = {
  available: 'border-emerald-600/30 bg-emerald-500/15 text-emerald-700 dark:border-emerald-400/30 dark:text-emerald-300',
  missing: 'border-amber-600/30 bg-amber-500/15 text-amber-700 dark:border-amber-400/30 dark:text-amber-300',
  downloading: 'border-blue-600/30 bg-blue-500/15 text-blue-700 dark:border-blue-400/30 dark:text-blue-300',
  pending: 'border-border bg-muted text-muted-foreground',
}

export function StatusBadge({ status, className }: { status: VersionStatus; className?: string }) {
  const { t } = useTranslation()
  return (
    <Badge variant="outline" className={cn(statusStyles[status], className)}>
      {t(`status.${status}`)}
    </Badge>
  )
}
