import { useMemo, useRef } from 'react'
import { Link } from '@tanstack/react-router'
import { useVirtualizer } from '@tanstack/react-virtual'
import { cn } from 'cn'
import type { TFunction } from 'i18next'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import type { BulkMovieResult, BulkOutcome, BulkSeriesResult } from '@/api/client'
import { statusStyles } from '@/components/StatusBadge'
import { Badge } from '@/components/ui/badge'
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog'

/** A film or a series line of a bulk report. */
export type BulkItemResult = BulkMovieResult | BulkSeriesResult

export type BulkReport = { action: string; results: BulkItemResult[] }

/** Problems first, so a long report opens on what needs a look. */
const outcomeOrder: BulkOutcome[] = ['failed', 'skipped', 'updated', 'queued', 'versionsDeleted', 'movieDeleted', 'seriesDeleted', 'alreadyQueued', 'unchanged']

const outcomeVariant: Record<BulkOutcome, 'secondary' | 'destructive' | 'outline'> = {
  updated: 'secondary',
  queued: 'secondary',
  versionsDeleted: 'secondary',
  movieDeleted: 'secondary',
  seriesDeleted: 'secondary',
  unchanged: 'outline',
  alreadyQueued: 'outline',
  skipped: 'outline',
  failed: 'destructive',
}

export function countOutcomes(results: BulkItemResult[]) {
  const counts = new Map<BulkOutcome, number>()
  for (const r of results) counts.set(r.outcome, (counts.get(r.outcome) ?? 0) + 1)
  return outcomeOrder.filter((o) => counts.has(o)).map((outcome) => ({ outcome, count: counts.get(outcome) ?? 0 }))
}

/** "Updated 98 · Unchanged 12 · Skipped 2" */
export function outcomeSummary(t: TFunction, results: BulkItemResult[]) {
  return countOutcomes(results)
    .map(({ outcome, count }) => t('bulk.count', { label: t(`bulk.outcomes.${outcome}`), count }))
    .join(' · ')
}

/** One toast per bulk action, with the per-film report one click away. */
export function toastBulk(t: TFunction, report: BulkReport, onDetails: (report: BulkReport) => void) {
  const failed = report.results.some((r) => r.outcome === 'failed')
  const skipped = report.results.some((r) => r.outcome === 'skipped')
  const show = failed ? toast.error : skipped ? toast.warning : toast.success
  show(report.action, {
    description: outcomeSummary(t, report.results),
    action: { label: t('bulk.details'), onClick: () => onDetails(report) },
  })
}

export function BulkResultsDialog({ report, onClose }: { report: BulkReport | null; onClose: () => void }) {
  const { t } = useTranslation()
  return (
    <Dialog open={report !== null} onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="sm:max-w-2xl" data-testid="bulk-results">
        <DialogHeader>
          <DialogTitle>{report?.action}</DialogTitle>
          <DialogDescription>{report ? outcomeSummary(t, report.results) : null}</DialogDescription>
        </DialogHeader>
        {report && <ResultList results={report.results} />}
      </DialogContent>
    </Dialog>
  )
}

/** Virtualized: a "select all" on 5000 films reports 5000 lines. */
export function ResultList({ results, className }: { results: BulkItemResult[]; className?: string }) {
  const { t } = useTranslation()
  const scrollRef = useRef<HTMLDivElement>(null)
  const sorted = useMemo(
    () => [...results].sort((a, b) => outcomeOrder.indexOf(a.outcome) - outcomeOrder.indexOf(b.outcome) || a.title.localeCompare(b.title)),
    [results],
  )
  const virtualizer = useVirtualizer({ count: sorted.length, getScrollElement: () => scrollRef.current, estimateSize: () => 44, overscan: 8 })

  return (
    <div ref={scrollRef} className={className ?? 'max-h-[60vh] overflow-y-auto rounded-md border'}>
      <div className="relative" style={{ height: virtualizer.getTotalSize() }}>
        {virtualizer.getVirtualItems().map((item) => {
          const r = sorted[item.index]
          const detail = [
            r.versions.length > 0 ? r.versions.join(', ') : null,
            r.promoted ? t('bulk.promoted', { name: r.promoted }) : null,
            r.files > 0 ? t('bulk.files', { count: r.files }) : null,
            r.detail,
          ]
            .filter(Boolean)
            .join(' · ')
          return (
            <div
              key={item.key}
              data-index={item.index}
              ref={virtualizer.measureElement}
              className="absolute top-0 left-0 flex w-full items-center gap-3 border-b px-3 py-2 text-sm last:border-b-0"
              style={{ transform: `translateY(${item.start}px)` }}
            >
              <div className="min-w-0 flex-1">
                {r.outcome === 'movieDeleted' || r.outcome === 'seriesDeleted' ? (
                  <span className="block truncate font-medium">{r.title}</span>
                ) : 'seriesId' in r ? (
                  <Link to="/series/$seriesId" params={{ seriesId: String(r.seriesId) }} className="block truncate font-medium hover:underline underline-offset-4">
                    {r.title}
                  </Link>
                ) : (
                  <Link to="/movies/$movieId" params={{ movieId: String(r.movieId) }} className="block truncate font-medium hover:underline underline-offset-4">
                    {r.title}
                  </Link>
                )}
                {detail && <span className="block truncate text-xs text-muted-foreground">{detail}</span>}
              </div>
              <Badge variant={outcomeVariant[r.outcome]} className={cn('shrink-0', r.outcome === 'skipped' && statusStyles.missing)}>
                {t(`bulk.outcomes.${r.outcome}`)}
              </Badge>
            </div>
          )
        })}
      </div>
    </div>
  )
}
