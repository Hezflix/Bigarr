import { useState } from 'react'
import { cn } from 'cn'
import { ChevronRightIcon, CopyIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import type { IndexerQueryReport, IndexerQueryRequest } from '@/api/client'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Label } from '@/components/ui/label'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { parameterLine, readAdvanced, reportTotals, requestState, writeAdvanced } from '@/features/search/advancedMode'

/** The advanced mode of the interactive search, remembered per browser. */
export function useAdvancedSearch(): [boolean, (on: boolean) => void] {
  const [on, setOn] = useState(readAdvanced)
  return [
    on,
    (next) => {
      setOn(next)
      writeAdvanced(next)
    },
  ]
}

export function AdvancedToggle({ checked, onChange, testId }: { checked: boolean; onChange: (on: boolean) => void; testId: string }) {
  const { t } = useTranslation()
  return (
    <Tooltip>
      <TooltipTrigger asChild>
        <Label className="cursor-pointer text-xs font-normal text-muted-foreground">
          <Checkbox checked={checked} onCheckedChange={(state) => onChange(state === true)} data-testid={testId} />
          {t('searchQueries.toggle')}
        </Label>
      </TooltipTrigger>
      <TooltipContent className="max-w-xs">{t('searchQueries.toggleHint')}</TooltipContent>
    </Tooltip>
  )
}

/**
 * What each indexer was asked, folded by default: per indexer its requests in order (step, function, parameters,
 * redacted URL, status, time, results) and what became of its releases. The URLs come redacted from the server.
 */
export function SearchQueriesPanel({ queries }: { queries: IndexerQueryReport[] | null | undefined }) {
  const { t } = useTranslation()
  if (!queries) {
    return (
      <p className="text-xs text-muted-foreground" data-testid="search-queries-stale">
        {t('searchQueries.searchAgain')}
      </p>
    )
  }

  const requests = queries.reduce((sum, q) => sum + q.requests.length, 0)
  return (
    <details className="group min-w-0 shrink-0 rounded-lg border text-sm" data-testid="search-queries">
      <summary className="flex cursor-pointer list-none flex-wrap items-center gap-x-2 gap-y-1 px-3 py-2 select-none [&::-webkit-details-marker]:hidden">
        <ChevronRightIcon className="size-4 shrink-0 transition-transform group-open:rotate-90" />
        <span className="font-medium">{t('searchQueries.title')}</span>
        <span className="text-xs text-muted-foreground">{t('searchQueries.summary', { indexers: queries.length, requests })}</span>
      </summary>
      <div className="max-h-80 overflow-y-auto border-t">
        {queries.map((report) => (
          <IndexerQueries key={report.indexerId} report={report} />
        ))}
      </div>
    </details>
  )
}

function IndexerQueries({ report }: { report: IndexerQueryReport }) {
  const { t } = useTranslation()
  const totals = reportTotals(report)
  return (
    <section className="flex min-w-0 flex-col gap-2 border-b px-3 py-2 last:border-b-0" data-testid="search-queries-indexer">
      <div className="flex flex-wrap items-baseline gap-x-3 gap-y-0.5">
        <span className="font-medium">{report.indexer}</span>
        <span className="text-xs text-muted-foreground tabular-nums">
          {[
            t('searchQueries.indexerTotals', { sent: totals.sent, results: totals.results }),
            totals.failed > 0 ? t('searchQueries.failed', { count: totals.failed }) : null,
            totals.skipped > 0 ? t('searchQueries.skipped', { count: totals.skipped }) : null,
          ]
            .filter(Boolean)
            .join(' · ')}
        </span>
        <Tooltip>
          <TooltipTrigger asChild>
            <span className="text-xs text-muted-foreground tabular-nums sm:ml-auto">
              {t('searchQueries.outcome', { releases: report.releases, parsed: report.parsed, matched: report.matched, accepted: report.accepted })}
            </span>
          </TooltipTrigger>
          <TooltipContent className="max-w-xs">{t('searchQueries.outcomeHint')}</TooltipContent>
        </Tooltip>
      </div>
      {report.requests.length === 0 ? (
        <p className="text-xs text-muted-foreground">{t('searchQueries.none')}</p>
      ) : (
        <ol className="flex min-w-0 flex-col gap-2">
          {report.requests.map((request, index) => (
            // Requests have no id: their order is the order they were sent in, and the list never reorders.
            // biome-ignore lint/suspicious/noArrayIndexKey: see above
            <RequestLine key={index} request={request} />
          ))}
        </ol>
      )}
    </section>
  )
}

function RequestLine({ request }: { request: IndexerQueryRequest }) {
  const { t } = useTranslation()
  const state = requestState(request)
  const params = parameterLine(request)
  const facts = request.skipped
    ? []
    : [
        request.statusCode != null ? t('searchQueries.status', { status: request.statusCode }) : null,
        t('searchQueries.duration', { ms: request.durationMs }),
        request.results != null ? t('searchQueries.results', { count: request.results }) : null,
        request.waitedMs > 0 ? t('searchQueries.waited', { ms: request.waitedMs }) : null,
      ].filter((f): f is string => !!f)

  const copy = () => {
    if (!request.url) return
    void navigator.clipboard
      ?.writeText(request.url)
      .then(() => toast.success(t('searchQueries.copied')))
      .catch(() => undefined)
  }

  return (
    <li className="flex min-w-0 flex-col gap-1" data-testid="search-queries-request" data-state={state}>
      <div className="flex min-w-0 flex-wrap items-center gap-x-2 gap-y-1 text-xs">
        {(request.step || request.skipped) && (
          <Badge variant="outline" className="h-auto max-w-full rounded-md whitespace-normal text-left">
            {request.skipped ? [request.step, t('searchQueries.skippedRequest')].filter(Boolean).join(' › ') : request.step}
          </Badge>
        )}
        {request.function && <code className="font-mono">t={request.function}</code>}
        {params && <code className="min-w-0 font-mono break-all">{params}</code>}
        {facts.length > 0 && (
          <span className={cn('tabular-nums sm:ml-auto', state === 'error' ? 'text-destructive' : 'text-muted-foreground')}>{facts.join(' · ')}</span>
        )}
      </div>
      {request.error && <p className={cn('text-xs', state === 'skipped' ? 'text-amber-600 dark:text-amber-400' : 'text-destructive')}>{request.error}</p>}
      {request.url && (
        <div className="flex min-w-0 items-start gap-1">
          <code className="min-w-0 flex-1 font-mono text-[11px] leading-5 break-all text-muted-foreground" data-testid="search-queries-url">
            {request.url}
          </code>
          <Button type="button" variant="ghost" size="icon" className="size-6 shrink-0" onClick={copy} aria-label={t('searchQueries.copyUrl')} title={t('searchQueries.copyUrl')}>
            <CopyIcon className="size-3.5" />
          </Button>
        </div>
      )}
    </li>
  )
}
