import { useQuery } from '@tanstack/react-query'
import { cn } from 'cn'
import { CaptionsIcon, CircleCheckIcon, CircleDotIcon, CircleHelpIcon, MonitorIcon, Volume2Icon } from 'lucide-react'
import type { TFunction } from 'i18next'
import { useTranslation } from 'react-i18next'
import { type Issue, type IssueStatus, type IssueType, issues } from '@/api/client'

export const ISSUE_TYPES: IssueType[] = ['video', 'audio', 'subtitles', 'other']

const typeIcons: Record<IssueType, React.ComponentType<{ className?: string }>> = {
  video: MonitorIcon,
  audio: Volume2Icon,
  subtitles: CaptionsIcon,
  other: CircleHelpIcon,
}

export function IssueTypeIcon({ type, className }: { type: IssueType; className?: string }) {
  const Icon = typeIcons[type]
  return <Icon className={cn('size-4', className)} aria-hidden="true" />
}

/** The type as a small chip: icon and word. */
export function IssueTypeChip({ type }: { type: IssueType }) {
  const { t } = useTranslation()
  return (
    <span className="inline-flex items-center gap-1 rounded-md border px-1.5 py-0.5 text-xs" data-testid="issue-type">
      <IssueTypeIcon type={type} className="size-3.5" />
      {t(`issues.type.${type}`)}
    </span>
  )
}

export function IssueStatusBadge({ status }: { status: IssueStatus }) {
  const { t } = useTranslation()
  const open = status === 'open'
  const Icon = open ? CircleDotIcon : CircleCheckIcon
  return (
    <span
      className={cn(
        'inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-medium',
        open ? 'bg-amber-500/15 text-amber-700 dark:text-amber-300' : 'bg-emerald-500/15 text-emerald-700 dark:text-emerald-300',
      )}
      data-testid="issue-status"
    >
      <Icon className="size-3.5" aria-hidden="true" />
      {t(`issues.status.${status}`)}
    </span>
  )
}

/** "Film", "Whole series", "Season 2", "S02E05 · Title". */
export function scopeText(t: TFunction, issue: Pick<Issue, 'scope' | 'seasonNumber' | 'episodeLabel'>): string {
  switch (issue.scope) {
    case 'season':
      return t('issues.scope.season', { number: issue.seasonNumber })
    case 'episode':
      return issue.episodeLabel ?? t('issues.scope.episode')
    default:
      return t(`issues.scope.${issue.scope}`)
  }
}

/** How many issues are open, next to the Issues entry of an admin or a request manager. */
export function OpenIssuesBadge({ className }: { className?: string }) {
  const { t } = useTranslation()
  const query = useQuery({ queryKey: ['issues', 'count'], queryFn: issues.count, refetchInterval: 60_000 })
  const open = query.data?.open ?? 0
  if (open === 0) return null
  return (
    <span
      className={cn('ml-auto inline-flex h-5 min-w-5 items-center justify-center rounded-full bg-amber-500/15 px-1.5 text-xs font-medium text-amber-700 tabular-nums dark:text-amber-300', className)}
      data-testid="issues-badge"
    >
      <span aria-hidden="true">{open}</span>
      <span className="sr-only">{t('issues.openBadge', { count: open })}</span>
    </span>
  )
}
