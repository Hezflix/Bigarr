import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { cn } from 'cn'
import { RefreshCwIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { shadow, type CompareCounts, type CompareOutcome } from '@/api/client'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { CompareTable, outcomeStyles } from '@/features/activity/CompareTable'
import { formatDateTime } from '@/i18n'

const pageSize = 50
const outcomes: CompareOutcome[] = ['same', 'different', 'bigarrOnly', 'radarrOnly']
const periods = [1, 7, 30, 90, 0] as const
const windows = [6, 24, 72] as const

/**
 * Activity → Compare (shadow mode): per film and version, what the followed Radarr grabbed next to what Bigarr would
 * have grabbed, and why each side did what it did. The point is to judge Bigarr's parser, profiles, custom formats and
 * version attribution against Radarr's before replacing it.
 */
export function ComparePage() {
  const { t } = useTranslation()
  const [days, setDays] = useState<number>(30)
  const [windowHours, setWindowHours] = useState<number>(24)
  const [version, setVersion] = useState<string>('all')
  const [outcome, setOutcome] = useState<CompareOutcome | 'all'>('all')
  const [page, setPage] = useState(1)

  const query = useQuery({
    queryKey: ['compare', days, windowHours, version, outcome, page],
    queryFn: () =>
      shadow.compare({ days, windowHours, page, pageSize, version: version === 'all' ? undefined : version, outcome: outcome === 'all' ? undefined : outcome }),
    refetchInterval: 60_000,
  })
  const status = useQuery({ queryKey: ['shadow'], queryFn: shadow.status, staleTime: 60_000 })

  const data = query.data
  const pages = data ? Math.max(1, Math.ceil(data.total / pageSize)) : 1
  const resetPage = <T,>(set: (value: T) => void) => (value: T) => {
    set(value)
    setPage(1)
  }

  return (
    <>
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="text-2xl font-semibold tracking-tight">{t('compare.title')}</h1>
        <div className="ml-auto flex flex-wrap items-center gap-2">
          <Select value={String(days)} onValueChange={(value) => resetPage(setDays)(Number(value))}>
            <SelectTrigger size="sm" aria-label={t('compare.period')} data-testid="compare-period">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {periods.map((value) => (
                <SelectItem key={value} value={String(value)}>
                  {value === 0 ? t('compare.periodAll') : t('compare.periodDays', { count: value })}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
          <Select value={version} onValueChange={resetPage(setVersion)}>
            <SelectTrigger size="sm" aria-label={t('compare.version')} data-testid="compare-version">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">{t('compare.allVersions')}</SelectItem>
              {data?.versions.map((name) => (
                <SelectItem key={name} value={name}>
                  {name}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
          <Tooltip>
            <TooltipTrigger asChild>
              <span>
                <Select value={String(windowHours)} onValueChange={(value) => resetPage(setWindowHours)(Number(value))}>
                  <SelectTrigger size="sm" aria-label={t('compare.window')}>
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {windows.map((value) => (
                      <SelectItem key={value} value={String(value)}>
                        {t('compare.windowHours', { hours: value })}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </span>
            </TooltipTrigger>
            <TooltipContent className="max-w-xs">{t('compare.windowHint')}</TooltipContent>
          </Tooltip>
          <Button type="button" variant="outline" size="sm" onClick={() => void query.refetch()} disabled={query.isFetching} aria-label={t('compare.refresh')}>
            <RefreshCwIcon className={query.isFetching ? 'animate-spin' : undefined} />
          </Button>
        </div>
      </div>
      <p className="max-w-3xl text-sm text-muted-foreground">{t('compare.intro')}</p>

      {data && !data.shadowEnabled && (
        <Alert className="max-w-3xl">
          <AlertTitle>{t('radarrFollow.shadowOff')}</AlertTitle>
          <AlertDescription>{t('compare.shadowOffHint')}</AlertDescription>
        </Alert>
      )}
      {status.data && status.data.radarrs.length === 0 && (
        <Alert className="max-w-3xl">
          <AlertTitle>{t('compare.noRadarr')}</AlertTitle>
          <AlertDescription>
            <span>
              {t('compare.noRadarrHint')}{' '}
              <Link to="/settings/followed-radarrs" className="underline underline-offset-4">
                {t('nav.settingsFollowedRadarrs')}
              </Link>
            </span>
          </AlertDescription>
        </Alert>
      )}

      {query.isPending && (
        <div className="flex flex-col gap-2">
          <Skeleton className="h-20 w-full" />
          <Skeleton className="h-8 w-full" />
          <Skeleton className="h-8 w-full" />
        </div>
      )}

      {data && (
        <>
          <Summary counts={data.summary} active={outcome} onPick={resetPage(setOutcome)} />

          {data.byVersion.length > 1 && (
            <div className="overflow-x-auto rounded-lg border sm:max-w-2xl" data-testid="compare-by-version">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>{t('compare.version')}</TableHead>
                    {outcomes.map((o) => (
                      <TableHead key={o} className="text-right">
                        {t(`compare.outcomes.${o}`)}
                      </TableHead>
                    ))}
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {data.byVersion.map((v) => (
                    <TableRow key={v.versionName}>
                      <TableCell>
                        <Badge variant="secondary">{v.versionName}</Badge>
                      </TableCell>
                      {outcomes.map((o) => (
                        <TableCell key={o} className="text-right tabular-nums">
                          {v.counts[o]}
                        </TableCell>
                      ))}
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </div>
          )}

          <div className="flex flex-wrap items-center gap-2">
            <div className="flex flex-wrap rounded-md border p-0.5" role="tablist" aria-label={t('compare.outcome')}>
              {(['all', ...outcomes] as const).map((value) => (
                <Button
                  key={value}
                  type="button"
                  role="tab"
                  aria-selected={outcome === value}
                  variant={outcome === value ? 'secondary' : 'ghost'}
                  size="sm"
                  onClick={() => resetPage(setOutcome)(value)}
                  data-testid={`compare-filter-${value}`}
                >
                  {value === 'all' ? t('compare.all') : t(`compare.outcomes.${value}`)}
                  <span className="text-xs text-muted-foreground tabular-nums">{value === 'all' ? data.summary.total : data.summary[value]}</span>
                </Button>
              ))}
            </div>
            <span className="text-xs text-muted-foreground">
              {data.from ? t('compare.since', { date: formatDateTime(data.from) }) : t('compare.periodAll')} · {t('compare.windowHours', { hours: data.windowHours })}
            </span>
          </div>

          {data.rows.length === 0 ? (
            <p className="text-sm text-muted-foreground">{data.summary.total === 0 ? t('compare.empty') : t('compare.emptyFiltered')}</p>
          ) : (
            <CompareTable rows={data.rows} />
          )}

          {pages > 1 && (
            <div className="flex items-center gap-2">
              <Button type="button" variant="outline" size="sm" onClick={() => setPage((p) => Math.max(1, p - 1))} disabled={page <= 1}>
                {t('compare.previous')}
              </Button>
              <span className="text-sm text-muted-foreground tabular-nums">{t('compare.page', { page, pages })}</span>
              <Button type="button" variant="outline" size="sm" onClick={() => setPage((p) => Math.min(pages, p + 1))} disabled={page >= pages}>
                {t('compare.next')}
              </Button>
            </div>
          )}
        </>
      )}
    </>
  )
}

/** Four tiles, one per outcome; the agreement is over Radarr's grabs: how many Bigarr would have made too. */
function Summary({ counts, active, onPick }: { counts: CompareCounts; active: CompareOutcome | 'all'; onPick: (value: CompareOutcome | 'all') => void }) {
  const { t } = useTranslation()
  const grabs = counts.same + counts.different + counts.radarrOnly
  const agreement = grabs > 0 ? Math.round((counts.same / grabs) * 100) : null
  return (
    <div className="flex flex-col gap-2" data-testid="compare-summary">
      <dl className="grid grid-cols-2 gap-3 lg:grid-cols-4">
        {outcomes.map((o) => (
          <button
            key={o}
            type="button"
            onClick={() => onPick(active === o ? 'all' : o)}
            className={cn('rounded-lg border p-3 text-left transition-colors hover:bg-muted/50', active === o && 'ring-2 ring-ring')}
            aria-pressed={active === o}
          >
            <dt className="flex items-center gap-2 text-xs text-muted-foreground">
              <span className={cn('inline-block size-2 rounded-full border', outcomeStyles[o])} />
              {t(`compare.outcomes.${o}`)}
            </dt>
            <dd className="text-2xl font-semibold tabular-nums">{counts[o]}</dd>
            <dd className="text-xs text-muted-foreground">{t(`compare.outcomeHints.${o}`)}</dd>
          </button>
        ))}
      </dl>
      <p className="text-sm text-muted-foreground" data-testid="compare-agreement">
        {agreement === null ? t('compare.noGrabs') : t('compare.agreement', { percent: agreement, same: counts.same, grabs })}
      </p>
    </div>
  )
}
