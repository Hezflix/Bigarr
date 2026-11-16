import { cn } from 'cn'
import { BanIcon, CheckIcon, CircleAlertIcon, ClockIcon, HourglassIcon, PieChartIcon, ThumbsUpIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import type { Me, RequestSlots, RequestState } from '@/api/client'

/** A colour per state, close to Discover's availability: amber waiting, blue on its way, green there, red failed. */
export const requestStateStyles: Record<RequestState, string> = {
  pending: 'border-amber-600/30 bg-amber-500/15 text-amber-700 dark:border-amber-400/30 dark:text-amber-300',
  approved: 'border-sky-600/30 bg-sky-500/15 text-sky-700 dark:border-sky-400/30 dark:text-sky-300',
  processing: 'border-blue-600/30 bg-blue-500/15 text-blue-700 dark:border-blue-400/30 dark:text-blue-300',
  partiallyAvailable: 'border-amber-600/30 bg-amber-500/15 text-amber-700 dark:border-amber-400/30 dark:text-amber-300',
  available: 'border-emerald-600/30 bg-emerald-500/15 text-emerald-700 dark:border-emerald-400/30 dark:text-emerald-300',
  declined: 'border-border bg-muted text-muted-foreground',
  failed: 'border-red-600/30 bg-red-500/15 text-red-700 dark:border-red-400/30 dark:text-red-300',
}

export const requestStateIcons: Record<RequestState, React.ComponentType<{ className?: string }>> = {
  pending: HourglassIcon,
  approved: ThumbsUpIcon,
  processing: ClockIcon,
  partiallyAvailable: PieChartIcon,
  available: CheckIcon,
  declined: BanIcon,
  failed: CircleAlertIcon,
}

export function RequestStateBadge({ state, className }: { state: RequestState; className?: string }) {
  const { t } = useTranslation()
  const Icon = requestStateIcons[state]
  return (
    <span
      className={cn('inline-flex items-center gap-1 rounded-md border px-2 py-0.5 text-xs font-medium whitespace-nowrap', requestStateStyles[state], className)}
      data-testid={`request-state-${state}`}
    >
      <Icon className="size-3.5" aria-hidden="true" />
      {t(`requests.state.${state}`)}
    </span>
  )
}

/** "Standard · Requested" next to the availability chips of a title page, one per slot someone asked for. */
export function RequestChips({ requests, className }: { requests: RequestSlots | null | undefined; className?: string }) {
  const { t } = useTranslation()
  if (!requests) return null
  const slots = [
    { key: 'standard', label: t('discover.version.standard'), state: requests.standard },
    { key: 'fourK', label: t('discover.version.fourK'), state: requests.fourK },
  ].filter((s): s is { key: string; label: string; state: RequestState } => !!s.state)
  if (slots.length === 0) return null
  return (
    <div className={cn('flex flex-wrap items-center gap-1.5', className)}>
      {slots.map((slot) => {
        const Icon = requestStateIcons[slot.state]
        return (
          <span key={slot.key} className={cn('inline-flex items-center gap-1 rounded-md border px-2 py-0.5 text-xs font-medium whitespace-nowrap', requestStateStyles[slot.state])} data-testid={`requested-${slot.key}`}>
            <Icon className="size-3.5" aria-hidden="true" />
            <span>{slot.label}</span>
            <span aria-hidden="true">·</span>
            {t(`requests.badge.${slot.state}`)}
          </span>
        )
      })}
    </div>
  )
}

/** What the signed-in user may ask for, from their session's permissions (an admin carries them all). */
export function requestRights(me: Me | undefined) {
  const has = (p: string) => me?.permissions.includes(p) ?? false
  return { films: has('RequestFilms'), series: has('RequestSeries'), fourK: has('Request4K') }
}
