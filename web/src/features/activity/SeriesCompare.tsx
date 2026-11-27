import { Fragment, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { cn } from 'cn'
import { ChevronDownIcon, ChevronRightIcon, RefreshCwIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { shadow, type SeriesCompareGrab, type SeriesCompareOutcome, type SeriesComparePick, type SeriesCompareRow } from '@/api/client'
import { Poster } from '@/components/Poster'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { compareAgreement } from '@/features/activity/compareModel'
import { CompareByVersion, comparePageSize, ComparePager, ComparePeriodSelect, CompareSummary, CompareVersionSelect, CompareWindowSelect, useComparePage } from '@/features/activity/compareParts'
import { formatGap, TimingNote } from '@/features/activity/TimingNote'
import { ReasonList } from '@/features/search/ReasonsDisplay'
import { approvedStyle } from '@/features/search/releaseTable'
import { useShadowMode } from '@/hooks/useShadowMode'
import { formatBytes, formatDateTime } from '@/i18n'

const outcomes: SeriesCompareOutcome[] = ['same', 'different', 'intended', 'bigarrOnly', 'sonarrOnly']

export const seriesOutcomeStyles: Record<SeriesCompareOutcome, string> = {
  same: 'border-emerald-600/30 bg-emerald-500/15 text-emerald-700 dark:border-emerald-400/30 dark:text-emerald-300',
  different: 'border-amber-600/30 bg-amber-500/15 text-amber-700 dark:border-amber-400/30 dark:text-amber-300',
  bigarrOnly: 'border-violet-600/30 bg-violet-500/15 text-violet-700 dark:border-violet-400/30 dark:text-violet-300',
  sonarrOnly: 'border-sky-600/30 bg-sky-500/15 text-sky-700 dark:border-sky-400/30 dark:text-sky-300',
  // A difference Bigarr makes on purpose (one release group per season): neutral, not a warning.
  intended: 'border-slate-500/30 bg-slate-500/15 text-slate-700 dark:border-slate-400/30 dark:text-slate-300',
}

/**
 * Activity → Compare, series: one flat table like the Radarr page, newest first, one row per show, version and episode
 * set: what the followed Sonarr grabbed next to what Bigarr would have grabbed. A season pack on one side and single
 * episodes on the other share one row.
 */
export function SonarrComparePage() {
  const { t } = useTranslation()
  const [days, setDays] = useState<number>(30)
  const [windowHours, setWindowHours] = useState<number>(24)
  const [version, setVersion] = useState<string>('all')
  const [outcome, setOutcome] = useState<SeriesCompareOutcome | 'all'>('all')
  const { page, setPage, resetPage } = useComparePage()

  const query = useQuery({
    queryKey: ['compareSeries', days, windowHours, version, outcome, page],
    queryFn: () =>
      shadow.compareSeries({ days, windowHours, page, pageSize: comparePageSize, version: version === 'all' ? undefined : version, outcome: outcome === 'all' ? undefined : outcome }),
    refetchInterval: 60_000,
  })
  const status = useQuery({ queryKey: ['shadow'], queryFn: shadow.status, staleTime: 60_000 })
  const data = query.data
  const pages = data ? Math.max(1, Math.ceil(data.total / comparePageSize)) : 1
  // The agreement is over Sonarr's grabs: how many Bigarr would have made too.
  const { grabs, percent } = compareAgreement(data?.summary ?? { same: 0, different: 0 }, data?.summary.sonarrOnly ?? 0)

  return (
    <>
      <h1 className="text-2xl font-semibold tracking-tight">{t('compare.seriesTitle')}</h1>
      <div className="flex flex-wrap items-center gap-2">
        <ComparePeriodSelect value={days} onChange={resetPage(setDays)} testId="series-compare-period" />
        <CompareVersionSelect value={version} onChange={resetPage(setVersion)} versions={data?.versions} testId="series-compare-version" />
        <CompareWindowSelect value={windowHours} onChange={resetPage(setWindowHours)} />
        <Button type="button" variant="outline" size="sm" onClick={() => void query.refetch()} disabled={query.isFetching} aria-label={t('compare.refresh')}>
          <RefreshCwIcon className={query.isFetching ? 'animate-spin' : undefined} />
        </Button>
      </div>
      <p className="max-w-3xl text-sm text-muted-foreground">{t('compare.seriesIntro')}</p>

      {data && !data.shadowEnabled && (
        <Alert className="max-w-3xl">
          <AlertTitle>{t('sonarrFollow.shadowOff')}</AlertTitle>
          <AlertDescription>{t('compare.shadowOffHint')}</AlertDescription>
        </Alert>
      )}
      {status.data && (status.data.sonarrs ?? []).length === 0 && (
        <Alert className="max-w-3xl">
          <AlertTitle>{t('compare.noSonarr')}</AlertTitle>
          <AlertDescription>
            <span>
              {t('compare.noSonarrHint')}{' '}
              <Link to="/settings/followed-sonarrs" className="underline underline-offset-4">
                {t('nav.settingsFollowedSonarrs')}
              </Link>
            </span>
          </AlertDescription>
        </Alert>
      )}

      {query.isPending && <Skeleton className="h-20 w-full" />}

      {data && (
        <>
          <CompareSummary<SeriesCompareOutcome>
            outcomes={outcomes}
            counts={data.summary}
            active={outcome}
            onPick={resetPage(setOutcome)}
            styles={seriesOutcomeStyles}
            label={(o) => t(`compare.seriesOutcomes.${o}`)}
            hint={(o) => t(`compare.seriesOutcomeHints.${o}`)}
            agreement={percent === null ? t('compare.seriesNoGrabs') : t('compare.seriesAgreement', { percent, same: data.summary.same, grabs })}
            className="lg:grid-cols-5"
            testIdPrefix="series-compare"
          />
          {data.byVersion.length > 1 && <CompareByVersion<SeriesCompareOutcome> rows={data.byVersion} outcomes={outcomes} label={(o) => t(`compare.seriesOutcomes.${o}`)} testId="series-compare-by-version" />}
          <span className="text-xs text-muted-foreground">
            {data.from ? t('compare.since', { date: formatDateTime(data.from) }) : t('compare.periodAll')} · {t('compare.windowHours', { hours: data.windowHours })}
          </span>
          {data.rows.length === 0 ? (
            <p className="text-sm text-muted-foreground">{data.summary.total === 0 ? t('compare.seriesEmpty') : t('compare.emptyFiltered')}</p>
          ) : (
            <SeriesCompareTable rows={data.rows} />
          )}
          <ComparePager page={page} pages={pages} onPage={setPage} />
        </>
      )}
    </>
  )
}

/** One row per episode set, each opening on both sides with their reasons. The series page hides the series column. */
export function SeriesCompareTable({ rows, showSeries = true }: { rows: SeriesCompareRow[]; showSeries?: boolean }) {
  const { t } = useTranslation()
  const [open, setOpen] = useState<Set<string>>(new Set())
  const toggle = (key: string) =>
    setOpen((current) => {
      const next = new Set(current)
      if (next.has(key)) next.delete(key)
      else next.add(key)
      return next
    })

  return (
    <div className="overflow-x-auto rounded-lg border">
      <Table data-testid="series-compare-table">
        <TableHeader>
          <TableRow>
            <TableHead className="w-8" />
            <TableHead>{showSeries ? t('compare.show') : t('compare.version')}</TableHead>
            <TableHead>{t('compare.episodes')}</TableHead>
            <TableHead>{t('compare.outcome')}</TableHead>
            <TableHead>{t('compare.sonarrGrabbed')}</TableHead>
            <TableHead>{t('compare.bigarrPicked')}</TableHead>
            <TableHead>{t('compare.when')}</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {rows.map((row) => {
            const expanded = open.has(row.key)
            const lead = row.bigarrLeadSeconds
            return (
              <Fragment key={row.key}>
                <TableRow className="cursor-pointer" onClick={() => toggle(row.key)} data-outcome={row.outcome} data-testid="series-compare-row">
                  <TableCell className="align-top">
                    <Button type="button" variant="ghost" size="icon" className="size-6" aria-label={expanded ? t('compare.collapse') : t('compare.expand')}>
                      {expanded ? <ChevronDownIcon /> : <ChevronRightIcon />}
                    </Button>
                  </TableCell>
                  <TableCell className={cn('align-top', showSeries && 'min-w-[11rem] whitespace-normal')}>
                    {showSeries ? (
                      <span className="flex items-start gap-2">
                        <Poster url={row.posterUrl} title={row.seriesTitle} size="thumb" />
                        <span>
                          {row.seriesId !== null ? (
                            <Link to="/series/$seriesId" params={{ seriesId: String(row.seriesId) }} className="font-medium underline-offset-4 hover:underline" onClick={(e) => e.stopPropagation()}>
                              {row.seriesTitle}
                            </Link>
                          ) : (
                            <span className="font-medium">{row.seriesTitle}</span>
                          )}
                          <Badge variant="secondary" className="ml-2">
                            {row.versionName}
                          </Badge>
                        </span>
                      </span>
                    ) : (
                      <Badge variant="secondary">{row.versionName}</Badge>
                    )}
                  </TableCell>
                  <TableCell className="align-top text-sm whitespace-nowrap">
                    <RowEpisodes row={row} />
                  </TableCell>
                  <TableCell className="align-top">
                    <Badge variant="outline" className={seriesOutcomeStyles[row.outcome]}>
                      {t(`compare.seriesOutcomes.${row.outcome}`)}
                    </Badge>
                    {row.reason && (
                      <span className="mt-1 block max-w-48 text-xs whitespace-normal text-muted-foreground" title={row.reasonDetail ?? undefined} data-testid="series-compare-reason">
                        {t(`compare.seriesReasons.${row.reason}`, { defaultValue: row.reason })}
                      </span>
                    )}
                  </TableCell>
                  <TableCell className="min-w-[13rem] max-w-[24rem] align-top whitespace-normal">
                    {row.sonarr.length === 0 ? <span className="text-muted-foreground">{t('compare.nothing')}</span> : row.sonarr.map((g) => <ReleaseLine key={g.id} title={g.title} pack={g.fullSeason} quality={g.quality} />)}
                  </TableCell>
                  <TableCell className="min-w-[13rem] max-w-[24rem] align-top whitespace-normal">
                    {row.bigarr.length === 0 ? <span className="text-muted-foreground">{t('compare.nothing')}</span> : row.bigarr.map((p) => <ReleaseLine key={p.id} title={p.title} pack={p.fullSeason} quality={p.quality} />)}
                    <TimingNote note={row.timing} app="Sonarr" />
                    {row.alsoPicked.length > 0 && <span className="block text-xs text-muted-foreground">{t('compare.alsoPickedSeries', { count: row.alsoPicked.length })}</span>}
                  </TableCell>
                  <TableCell className="align-top text-sm whitespace-nowrap">
                    {formatDateTime(row.at)}
                    {lead !== null && (
                      <span className="block text-xs text-muted-foreground">
                        {Math.abs(lead) < 60 ? t('compare.firstTogether') : lead > 0 ? t('compare.firstBigarr', { gap: formatGap(lead) }) : t('compare.firstSonarr', { gap: formatGap(lead) })}
                      </span>
                    )}
                  </TableCell>
                </TableRow>
                {expanded && (
                  <TableRow className="hover:bg-transparent">
                    <TableCell />
                    <TableCell colSpan={6} className="whitespace-normal">
                      <div className="grid gap-4 py-2 lg:grid-cols-2">
                        <div className="flex flex-col gap-3">
                          {row.sonarr.map((g) => (
                            <GrabCard key={g.id} grab={g} />
                          ))}
                          {row.sonarr.length === 0 && <p className="text-sm text-muted-foreground">{t('compare.whyNotSonarr')}: {t('compare.nothing')}</p>}
                          {row.timing && row.timing.kind !== 'neverSeen' && <TimingNote note={row.timing} app="Sonarr" className="text-sm" />}
                          {row.sighting === null && row.outcome === 'sonarrOnly' && <p className="text-xs text-muted-foreground">{t('compare.neverSeen')}</p>}
                          {row.reasonDetail && <p className="text-sm">{row.reasonDetail}.</p>}
                          {row.sighting && (
                            <p className="text-xs text-muted-foreground">
                              {t('compare.seen', { date: formatDateTime(row.sighting.seenAt), indexer: row.sighting.indexer })}
                              {row.sighting.unmatchedReason && ` ${t('compare.seenUnmatched', { reason: row.sighting.unmatchedReason })}`}
                            </p>
                          )}
                        </div>
                        <div className="flex flex-col gap-3">
                          {[...row.bigarr, ...row.alsoPicked].map((p) => (
                            <PickCard key={p.id} pick={p} />
                          ))}
                        </div>
                      </div>
                    </TableCell>
                  </TableRow>
                )}
              </Fragment>
            )
          })}
        </TableBody>
      </Table>
    </div>
  )
}

/** `S04E05`, a range, or `S03 pack (52 episodes)` when a release on the row is a whole-season pack. */
function RowEpisodes({ row }: { row: SeriesCompareRow }) {
  const { t } = useTranslation()
  if (row.packSeason === null) return row.episodes
  return <span title={row.episodes}>{t('compare.seasonPack', { season: String(row.packSeason).padStart(2, '0'), count: row.episodeCount })}</span>
}

function ReleaseLine({ title, pack, quality }: { title: string; pack: boolean; quality: string | null }) {
  const { t } = useTranslation()
  return (
    <span className="block text-sm break-all">
      {title}
      <span className="ml-1 inline-flex gap-1 align-middle">
        {quality && (
          <Badge variant="outline" className="text-[10px]">
            {quality}
          </Badge>
        )}
        {pack && (
          <Badge variant="outline" className="text-[10px]">
            {t('compare.pack')}
          </Badge>
        )}
      </span>
    </span>
  )
}

function GrabCard({ grab }: { grab: SeriesCompareGrab }) {
  const { t } = useTranslation()
  const verdict = grab.bigarr
  return (
    <div className="rounded-lg border p-3 text-sm" data-testid="series-compare-grab">
      <p className="font-medium">{t('compare.sonarrCard', { name: grab.sonarrName })}</p>
      <p className="break-all">{grab.title}</p>
      <p className="text-xs text-muted-foreground">
        {grab.episodes} · {grab.quality ?? '?'} · {formatBytes(grab.size)} · {grab.indexer ?? '?'} · {t(`compare.states.${grab.state}`)}
        {grab.customFormatScore !== null && ` · score ${grab.customFormatScore}`}
      </p>
      {verdict && (
        <div className="mt-2 flex flex-col gap-1">
          <span className="text-xs font-medium">{t('compare.bigarrSays')}</span>
          {verdict.unmatched ? (
            <span className="text-xs text-muted-foreground">{verdict.unmatched}</span>
          ) : (
            <>
              {verdict.approved ? (
                <span>
                  <Badge variant="outline" className={approvedStyle}>
                    {t('compare.approved')}
                  </Badge>
                </span>
              ) : (
                <ReasonList reasons={verdict.rejections} />
              )}
              {verdict.episodes && <span className="text-xs text-muted-foreground">{t('compare.bigarrEpisodes', { episodes: verdict.episodes })}</span>}
              {verdict.currentQuality ? (
                <span className="text-xs text-muted-foreground">
                  {verdict.currentIsPick ? t('compare.seriesPickThen', { quality: verdict.currentQuality, score: verdict.currentScore ?? 0 }) : t('compare.seriesFileThen', { quality: verdict.currentQuality, score: verdict.currentScore ?? 0 })}
                </span>
              ) : (
                <span className="text-xs text-muted-foreground">{t('compare.seriesNoFileThen')}</span>
              )}
            </>
          )}
        </div>
      )}
    </div>
  )
}

function PickCard({ pick }: { pick: SeriesComparePick }) {
  const { t } = useTranslation()
  const view = pick.sonarr
  return (
    <div className="rounded-lg border p-3 text-sm" data-testid="series-compare-pick">
      <p className="font-medium">{t('compare.bigarrCard')}</p>
      {pick.film && (
        <p className="text-xs text-muted-foreground" data-testid="series-compare-film-pick">
          {t('seriesFilms.filmPick', { film: pick.film })}
        </p>
      )}
      <p className="break-all">{pick.title}</p>
      <p className="text-xs text-muted-foreground">
        {pick.episodes} · {pick.quality ?? '?'} · {formatBytes(pick.size)} · {pick.indexer ?? '?'} · score {pick.customFormatScore} ·{' '}
        {t(`compare.triggers.${pick.trigger.replace(':', '_')}`, { defaultValue: pick.trigger })}
      </p>
      {!pick.film && <div className="mt-2 flex flex-col gap-1">
        <span className="text-xs font-medium">{t('compare.sonarrSays')}</span>
        {view === null ? (
          <span className="text-xs text-muted-foreground">{t('compare.sonarrNotAskedYet')}</span>
        ) : view.error ? (
          <span className="text-xs text-muted-foreground">{t('compare.sonarrError', { error: view.error })}</span>
        ) : view.tvdbId === null ? (
          <span className="text-xs text-muted-foreground">{t('compare.sonarrNoSeries')}</span>
        ) : (
          <>
            {view.episodes && <span className="text-xs text-muted-foreground">{t('compare.sonarrEpisodes', { episodes: view.episodes, withFile: view.episodesWithFile ?? 0 })}</span>}
            {view.monitored === false && <span className="text-xs text-muted-foreground">{t('compare.sonarrUnmonitored')}</span>}
            <span className="text-xs text-muted-foreground">
              {view.quality ?? '?'} · score {view.customFormatScore}
              {view.customFormats.length > 0 && ` · ${view.customFormats.join(', ')}`}
            </span>
          </>
        )}
      </div>}
    </div>
  )
}

/** The series page's own comparison, shown only while series are in shadow mode and the show has rows. */
export function SeriesCompareSection({ tvdbId }: { tvdbId: number | null }) {
  const { t } = useTranslation()
  const { series } = useShadowMode()
  const query = useQuery({
    queryKey: ['compareSeries', 'series', tvdbId],
    queryFn: () => shadow.compareSeries({ days: 0, windowHours: 24, page: 1, pageSize: 50, tvdbId: tvdbId ?? undefined }),
    enabled: series && tvdbId !== null,
  })
  const rows = query.data?.rows ?? []
  if (!series || rows.length === 0) return null
  return (
    <section className="flex flex-col gap-3" data-testid="series-compare-section">
      <h2 className="text-xl font-semibold tracking-tight">{t('compare.seriesTitle')}</h2>
      <p className="max-w-3xl text-sm text-muted-foreground">{t('compare.seriesHint')}</p>
      <SeriesCompareTable rows={rows} showSeries={false} />
    </section>
  )
}
