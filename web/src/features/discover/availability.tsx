import { cn } from 'cn'
import { CheckIcon, ClockIcon, PieChartIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import type { Availability, AvailabilityState } from '@/api/client'

/** One colour per state, close to the library's: green available, amber partial, blue on its way. */
export const availabilityStyles: Record<AvailabilityState, string> = {
  available: 'border-emerald-600/30 bg-emerald-500/15 text-emerald-700 dark:border-emerald-400/30 dark:text-emerald-300',
  partiallyAvailable: 'border-amber-600/30 bg-amber-500/15 text-amber-700 dark:border-amber-400/30 dark:text-amber-300',
  processing: 'border-blue-600/30 bg-blue-500/15 text-blue-700 dark:border-blue-400/30 dark:text-blue-300',
  notInLibrary: 'border-border bg-muted text-muted-foreground',
}

/** On a poster the badges sit on the image: solid backgrounds so they read on any picture. */
const solidStyles: Record<AvailabilityState, string> = {
  available: 'bg-emerald-600 text-white',
  partiallyAvailable: 'bg-amber-500 text-black',
  processing: 'bg-blue-600 text-white',
  notInLibrary: 'bg-black/60 text-white',
}

const icons: Record<AvailabilityState, React.ComponentType<{ className?: string }> | null> = {
  available: CheckIcon,
  partiallyAvailable: PieChartIcon,
  processing: ClockIcon,
  notInLibrary: null,
}

/**
 * The corner badges of a poster: one per version that is in the library ("HD" for the default version, "4K"),
 * nothing for a title the library does not have. The label says the state for screen readers and on hover.
 */
export function PosterBadges({ availability, className }: { availability: Availability | null; className?: string }) {
  const { t } = useTranslation()
  if (!availability) return null
  const slots = [
    { key: 'standard', label: t('discover.version.standardShort'), state: availability.standard },
    ...(availability.fourK ? [{ key: 'fourK', label: t('discover.version.fourK'), state: availability.fourK }] : []),
  ].filter((s) => s.state !== 'notInLibrary')
  if (slots.length === 0) return null

  return (
    <div className={cn('pointer-events-none absolute top-1.5 left-1.5 flex flex-col items-start gap-1', className)}>
      {slots.map((slot) => {
        const Icon = icons[slot.state]
        const text = `${slot.label}: ${t(`discover.state.${slot.state}`)}`
        return (
          <span
            key={slot.key}
            title={text}
            className={cn('inline-flex h-5 items-center gap-0.5 rounded-full px-1.5 text-[11px] font-semibold shadow-sm', solidStyles[slot.state])}
            data-testid={`badge-${slot.key}`}
          >
            {Icon && <Icon className="size-3" aria-hidden="true" />}
            <span aria-hidden="true">{slot.label}</span>
            <span className="sr-only">{text}</span>
          </span>
        )
      })}
    </div>
  )
}

/** "Standard · Available" and "4K · On its way" side by side, every state spelled out (title pages, seasons). */
export function AvailabilityChips({ availability, className }: { availability: Availability; className?: string }) {
  const { t } = useTranslation()
  const slots = [
    { key: 'standard', label: t('discover.version.standard'), state: availability.standard },
    ...(availability.fourK ? [{ key: 'fourK', label: t('discover.version.fourK'), state: availability.fourK }] : []),
  ]
  return (
    <div className={cn('flex flex-wrap items-center gap-1.5', className)}>
      {slots.map((slot) => {
        const Icon = icons[slot.state]
        return (
          <span
            key={slot.key}
            className={cn('inline-flex items-center gap-1 rounded-md border px-2 py-0.5 text-xs font-medium whitespace-nowrap', availabilityStyles[slot.state])}
            data-testid={`availability-${slot.key}`}
          >
            {Icon && <Icon className="size-3.5" aria-hidden="true" />}
            <span>{slot.label}</span>
            <span aria-hidden="true">·</span>
            {t(`discover.state.${slot.state}`)}
          </span>
        )
      })}
    </div>
  )
}
