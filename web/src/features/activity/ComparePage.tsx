import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { RefreshCwIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { shadow, type CompareOutcome } from '@/api/client'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { compareAgreement } from '@/features/activity/compareModel'
import { CompareByVersion, comparePageSize, ComparePager, ComparePeriodSelect, CompareSummary, CompareVersionSelect, CompareWindowSelect, useComparePage } from '@/features/activity/compareParts'
import { CompareTable, outcomeStyles } from '@/features/activity/CompareTable'
import { formatDateTime } from '@/i18n'

const outcomes: CompareOutcome[] = ['same', 'different', 'bigarrOnly', 'radarrOnly']

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
  const { page, setPage, resetPage } = useComparePage()

  const query = useQuery({
    queryKey: ['compare', days, windowHours, version, outcome, page],
    queryFn: () =>
      shadow.compare({ days, windowHours, page, pageSize: comparePageSize, version: version === 'all' ? undefined : version, outcome: outcome === 'all' ? undefined : outcome }),
    refetchInterval: 60_000,
  })
  const status = useQuery({ queryKey: ['shadow'], queryFn: shadow.status, staleTime: 60_000 })

  const data = query.data
  const pages = data ? Math.max(1, Math.ceil(data.total / comparePageSize)) : 1
  // The agreement is over Radarr's grabs: how many Bigarr would have made too.
  const { grabs, percent } = compareAgreement(data?.summary ?? { same: 0, different: 0 }, data?.summary.radarrOnly ?? 0)

  return (
    <>
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="text-2xl font-semibold tracking-tight">{t('compare.title')}</h1>
        <div className="ml-auto flex flex-wrap items-center gap-2">
          <ComparePeriodSelect value={days} onChange={resetPage(setDays)} testId="compare-period" />
          <CompareVersionSelect value={version} onChange={resetPage(setVersion)} versions={data?.versions} testId="compare-version" />
          <Tooltip>
            <TooltipTrigger asChild>
              <span>
                <CompareWindowSelect value={windowHours} onChange={resetPage(setWindowHours)} />
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
          <CompareSummary<CompareOutcome>
            outcomes={outcomes}
            counts={data.summary}
            active={outcome}
            onPick={resetPage(setOutcome)}
            styles={outcomeStyles}
            label={(o) => t(`compare.outcomes.${o}`)}
            hint={(o) => t(`compare.outcomeHints.${o}`)}
            agreement={percent === null ? t('compare.noGrabs') : t('compare.agreement', { percent, same: data.summary.same, grabs })}
            className="lg:grid-cols-4"
            testIdPrefix="compare"
          />

          {data.byVersion.length > 1 && <CompareByVersion<CompareOutcome> rows={data.byVersion} outcomes={outcomes} label={(o) => t(`compare.outcomes.${o}`)} testId="compare-by-version" />}

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

          <ComparePager page={page} pages={pages} onPage={setPage} />
        </>
      )}
    </>
  )
}
