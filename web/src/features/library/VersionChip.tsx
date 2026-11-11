import { cn } from 'cn'
import { ArrowUpIcon, StarIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import type { VersionStatus } from '@/api/client'
import { cutoffUnmetStyle, statusStyles } from '@/components/StatusBadge'
import { Badge } from '@/components/ui/badge'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import type { LibraryVersion, StatusFilter, MonitoredFilter } from './core'

/** What a chip's colour says: the version's status, a file below the cutoff told apart from one that meets it. */
export type ChipTone = VersionStatus | 'cutoffUnmet'

/** Everything a legend explains: the colours, plus the dashed border of an unmonitored version. */
export type LegendTone = ChipTone | 'unmonitored'

/** What a chip shows of a film's or a series' version. */
export type ChipVersion = Pick<LibraryVersion, 'name' | 'status' | 'cutoffMet' | 'monitored' | 'isDefault' | 'quality'>

/** Whose words explain the colours: a film's status is about its release, a series' about its aired episodes. */
export type LegendKind = 'library' | 'seriesLibrary'

export const chipTone = (version: ChipVersion): ChipTone => (version.status === 'available' && version.cutoffMet === false ? 'cutoffUnmet' : version.status)

const chipStyles: Record<ChipTone, string> = { ...statusStyles, cutoffUnmet: cutoffUnmetStyle }

/** Each tone as a solid dot: readable on the tooltip's inverted background as on the page. */
const toneDots: Record<LegendTone, string> = {
  available: 'bg-emerald-500',
  cutoffUnmet: 'bg-violet-500',
  missing: 'bg-amber-500',
  downloading: 'bg-blue-500',
  pending: 'bg-zinc-400 dark:bg-zinc-500',
  unmonitored: 'border border-dashed border-current',
}

/** Status colour of a version; an unmonitored one is dashed and faded so it reads as "left alone". */
export function versionChipClass(version: ChipVersion) {
  return cn(chipStyles[chipTone(version)], !version.monitored && 'border-dashed opacity-60')
}

export function ToneDot({ tone, className }: { tone: LegendTone; className?: string }) {
  return <span className={cn('inline-block size-2 shrink-0 rounded-full', toneDots[tone], className)} aria-hidden="true" />
}

/** The chip colour a view lists, if it lists one: "Missing" is amber, "Cutoff unmet" violet, "Unmonitored" dashed. */
export function filterTone(filter: { status: StatusFilter; monitored: MonitoredFilter }): LegendTone | null {
  switch (filter.status) {
    case 'missing':
    case 'downloading':
    case 'pending':
    case 'cutoffUnmet':
      return filter.status
    case 'any':
      return filter.monitored === 'unmonitored' ? 'unmonitored' : null
    default:
      // "Available" spans the green and the violet chips, "no file" three colours: no single colour to show.
      return null
  }
}

/** Before a view's tab label, the dot of the chips that view lists; nothing for a view that lists several colours. */
export function ViewToneDot({ filter }: { filter: { status: StatusFilter; monitored: MonitoredFilter } }) {
  const tone = filterTone(filter)
  return tone && <ToneDot tone={tone} />
}

/** "● Missing: out per its minimum availability, no file yet", one line of a chip's tooltip. */
function ToneHint({ tone, legend }: { tone: LegendTone; legend: LegendKind }) {
  const { t } = useTranslation()
  return (
    <span className="flex items-baseline gap-1.5">
      <ToneDot tone={tone} className="translate-y-px" />
      <span>
        <span className="font-medium">{t(`library.legend.${tone}.label`)}</span>: {t(`${legend}.legend.${tone}.hint` as `library.legend.${LegendTone}.hint`)}
      </span>
    </span>
  )
}

/**
 * A version as a small chip; what its colour, border and star mean is spelled out on hover. `detail` adds a line under
 * the name (a series' episode count).
 */
export function VersionChip({ version, className, legend = 'library', detail }: { version: ChipVersion; className?: string; legend?: LegendKind; detail?: string }) {
  const { t } = useTranslation()
  return (
    <Tooltip delayDuration={200}>
      <TooltipTrigger asChild>
        <Badge variant="outline" className={cn('gap-0.5', versionChipClass(version), className)} data-testid="version-chip">
          {version.isDefault && <StarIcon className="size-2.5 fill-current" aria-hidden="true" />}
          {version.cutoffMet === false && <ArrowUpIcon className="size-2.5" aria-hidden="true" />}
          {version.name}
        </Badge>
      </TooltipTrigger>
      <TooltipContent className="flex max-w-72 flex-col items-start gap-1 text-left">
        <span className="font-semibold">{[version.name, version.quality].filter(Boolean).join(' · ')}</span>
        {detail && <span className="tabular-nums">{detail}</span>}
        <ToneHint tone={chipTone(version)} legend={legend} />
        {!version.monitored && <ToneHint tone="unmonitored" legend={legend} />}
        {version.isDefault && (
          <span className="flex items-center gap-1.5">
            <StarIcon className="size-2 fill-current" aria-hidden="true" />
            {t('library.legend.default')}
          </span>
        )}
      </TooltipContent>
    </Tooltip>
  )
}
