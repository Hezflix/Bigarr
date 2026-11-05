import { cn } from 'cn'
import { ArrowUpIcon, StarIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import type { VersionSummary } from '@/api/client'
import { cutoffUnmetStyle, statusStyles } from '@/components/StatusBadge'
import { Badge } from '@/components/ui/badge'

/** Status colour of a version; an unmonitored one is dashed and faded so it reads as "left alone". */
export function versionChipClass(version: VersionSummary) {
  return cn(
    version.status === 'available' && version.cutoffMet === false ? cutoffUnmetStyle : statusStyles[version.status],
    !version.monitored && 'border-dashed opacity-60',
  )
}

/** "4k · Bluray-2160p · unmonitored": what the chip's colour means, spelled out on hover. */
export function useVersionTitle() {
  const { t } = useTranslation()
  return (version: VersionSummary) =>
    [
      version.name,
      version.quality ?? t(`status.${version.status}`),
      version.cutoffMet === false ? t('library.cutoffUnmet') : null,
      version.monitored ? null : t('library.unmonitored'),
    ]
      .filter(Boolean)
      .join(' · ')
}

/** A version as a small chip; `label` replaces the name (the matrix shows the quality or the status instead). */
export function VersionChip({ version, label, className }: { version: VersionSummary; label?: string; className?: string }) {
  const title = useVersionTitle()
  return (
    <Badge variant="outline" className={cn('gap-0.5', versionChipClass(version), className)} title={title(version)}>
      {version.isDefault && label === undefined && <StarIcon className="size-2.5 fill-current" aria-hidden="true" />}
      {version.cutoffMet === false && <ArrowUpIcon className="size-2.5" aria-hidden="true" />}
      {label ?? version.name}
    </Badge>
  )
}
