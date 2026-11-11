import { Fragment, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { cn } from 'cn'
import { ChevronDownIcon, ChevronRightIcon, RefreshCwIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { shadow, type SeriesCompareCounts, type SeriesCompareGrab, type SeriesCompareGroup, type SeriesCompareOutcome, type SeriesComparePick, type SeriesCompareRow } from '@/api/client'
import { Poster } from '@/components/Poster'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { formatBytes, formatDateTime } from '@/i18n'

const pageSize = 50
const outcomes: SeriesCompareOutcome[] = ['same', 'different', 'bigarrOnly', 'sonarrOnly']
const periods = [1, 7, 30, 90, 0] as const
const windows = [6, 24, 72] as const

export const seriesOutcomeStyles: Record<SeriesCompareOutcome, string> = {
  same: 'border-emerald-600/30 bg-emerald-500/15 text-emerald-700 dark:border-emerald-400/30 dark:text-emerald-300',
  different: 'border-amber-600/30 bg-amber-500/15 text-amber-700 dark:border-amber-400/30 dark:text-amber-300',
  bigarrOnly: 'border-violet-600/30 bg-violet-500/15 text-violet-700 dark:border-violet-400/30 dark:text-violet-300',
  sonarrOnly: 'border-sky-600/30 bg-sky-500/15 text-sky-700 dark:border-sky-400/30 dark:text-sky-300',
}
const okStyle = 'border-emerald-600/30 bg-emerald-500/15 text-emerald-700 dark:border-emerald-400/30 dark:text-emerald-300'
const rejectStyle = 'border-red-600/30 bg-red-500/10 text-red-700 dark:border-red-400/30 dark:text-red-300'

function formatGap(seconds: number): string {
  const s = Math.abs(seconds)
  if (s < 90) return `${Math.round(s)} s`
  if (s < 3600) return `${Math.round(s / 60)} min`
  if (s < 48 * 3600) return `${Math.round(s / 3600)} h`
  return `${Math.round(s / 86400)} d`
}

/**
 * Activity → Compare, series: per show, version and episode set, what the followed Sonarr grabbed next to what Bigarr
 * would have grabbed. A season pack on one side and single episodes on the other share one row.
 */
export function SonarrComparePage() {
  const { t } = useTranslation()
  const [days, setDays] = useState<number>(30)
  const [windowHours, setWindowHours] = useState<number>(24)
  const [version, setVersion] = useState<string>('all')
  const [outcome, setOutcome] = useState<SeriesCompareOutcome | 'all'>('all')
  const [page, setPage] = useState(1)

  const query = useQuery({
    queryKey: ['compareSeries', days, windowHours, version, outcome, page],
    queryFn: () =>
      shadow.compareSeries({ days, windowHours, page, pageSize, version: version === 'all' ? undefined : version, outcome: outcome === 'all' ? undefined : outcome }),
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
      <h1 className="text-2xl font-semibold tracking-tight">{t('compare.seriesTitle')}</h1>
      <div className="flex flex-wrap items-center gap-2">
        <Select value={String(days)} onValueChange={(value) => resetPage(setDays)(Number(value))}>
          <SelectTrigger size="sm" aria-label={t('compare.period')}>
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
          <SelectTrigger size="sm" aria-label={t('compare.version')}>
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
          <Summary counts={data.summary} active={outcome} onPick={resetPage(setOutcome)} />
          {data.byVersion.length > 1 && (
            <div className="overflow-x-auto rounded-lg border sm:max-w-2xl" data-testid="series-compare-by-version">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>{t('compare.version')}</TableHead>
                    {outcomes.map((o) => (
                      <TableHead key={o} className="text-right">
                        {t(`compare.seriesOutcomes.${o}`)}
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
          <span className="text-xs text-muted-foreground">
            {data.from ? t('compare.since', { date: formatDateTime(data.from) }) : t('compare.periodAll')} · {t('compare.windowHours', { hours: data.windowHours })}
          </span>
          {data.groups.length === 0 ? (
            <p className="text-sm text-muted-foreground">{data.summary.total === 0 ? t('compare.seriesEmpty') : t('compare.emptyFiltered')}</p>
          ) : (
            <SeriesGroups groups={data.groups} />
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

function Summary({ counts, active, onPick }: { counts: SeriesCompareCounts; active: SeriesCompareOutcome | 'all'; onPick: (value: SeriesCompareOutcome | 'all') => void }) {
  const { t } = useTranslation()
  const grabs = counts.same + counts.different + counts.sonarrOnly
  const agreement = grabs > 0 ? Math.round((counts.same / grabs) * 100) : null
  return (
    <div className="flex flex-col gap-2" data-testid="series-compare-summary">
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
              <span className={cn('inline-block size-2 rounded-full border', seriesOutcomeStyles[o])} />
              {t(`compare.seriesOutcomes.${o}`)}
            </dt>
            <dd className="text-2xl font-semibold tabular-nums">{counts[o]}</dd>
            <dd className="text-xs text-muted-foreground">{t(`compare.seriesOutcomeHints.${o}`)}</dd>
          </button>
        ))}
      </dl>
      <p className="text-sm text-muted-foreground">
        {agreement === null ? t('compare.seriesNoGrabs') : t('compare.seriesAgreement', { percent: agreement, same: counts.same, grabs })}
      </p>
    </div>
  )
}

function Counters({ counts }: { counts: SeriesCompareCounts }) {
  const { t } = useTranslation()
  return (
    <span className="flex flex-wrap gap-1">
      {outcomes
        .filter((o) => counts[o] > 0)
        .map((o) => (
          <Badge key={o} variant="outline" className={seriesOutcomeStyles[o]}>
            {t(`compare.seriesOutcomes.${o}`)} · {counts[o]}
          </Badge>
        ))}
    </span>
  )
}

const countRows = (rows: SeriesCompareRow[]): SeriesCompareCounts => {
  const counts = { same: 0, different: 0, bigarrOnly: 0, sonarrOnly: 0, total: rows.length }
  for (const row of rows) counts[row.outcome]++
  return counts
}

/** One line per show, collapsed: open it for its seasons, open a season for its episode-set rows. */
function SeriesGroups({ groups }: { groups: SeriesCompareGroup[] }) {
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
    <div className="flex flex-col rounded-lg border" data-testid="series-compare-groups">
      {groups.map((group) => {
        const key = `s${group.tvdbId}`
        const expanded = open.has(key)
        const seasons = [...new Set(group.rows.map((r) => r.seasonNumber))].sort((a, b) => b - a)
        return (
          <div key={key} className="border-b last:border-b-0" data-testid="series-compare-group">
            <button type="button" className="flex w-full items-center gap-3 p-3 text-left hover:bg-muted/50" onClick={() => toggle(key)} aria-expanded={expanded}>
              {expanded ? <ChevronDownIcon className="size-4 shrink-0" /> : <ChevronRightIcon className="size-4 shrink-0" />}
              <Poster url={group.posterUrl} title={group.seriesTitle} size="thumb" />
              <span className="min-w-0 flex-1">
                <span className="block font-medium">
                  {group.seriesTitle}
                  {group.year && <span className="ml-1 text-muted-foreground">({group.year})</span>}
                </span>
                <span className="block text-xs text-muted-foreground">
                  {t('compare.rowsCount', { count: group.rows.length })} · {t('compare.latest', { date: formatDateTime(group.latestAt) })}
                </span>
              </span>
              <Counters counts={group.counts} />
            </button>
            {expanded && (
              <div className="flex flex-col gap-2 px-3 pb-3 pl-10">
                {group.seriesId !== null && (
                  <Link to="/series/$seriesId" params={{ seriesId: String(group.seriesId) }} className="text-sm underline-offset-4 hover:underline">
                    {t('nav.series')} →
                  </Link>
                )}
                {seasons.map((season) => {
                  const seasonKey = `${key}-${season}`
                  const rows = group.rows.filter((r) => r.seasonNumber === season)
                  const seasonOpen = open.has(seasonKey)
                  return (
                    <div key={seasonKey} className="rounded-md border" data-testid="series-compare-season">
                      <button type="button" className="flex w-full items-center gap-2 p-2 text-left text-sm hover:bg-muted/50" onClick={() => toggle(seasonKey)} aria-expanded={seasonOpen}>
                        {seasonOpen ? <ChevronDownIcon className="size-4" /> : <ChevronRightIcon className="size-4" />}
                        <span className="font-medium">{season === 0 ? t('compare.specials') : t('compare.seasonLabel', { season })}</span>
                        <span className="text-xs text-muted-foreground">{t('compare.rowsCount', { count: rows.length })}</span>
                        <span className="ml-auto">
                          <Counters counts={countRows(rows)} />
                        </span>
                      </button>
                      {seasonOpen && <SeriesCompareTable rows={rows} showSeries={false} />}
                    </div>
                  )
                })}
              </div>
            )}
          </div>
        )
      })}
    </div>
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
                  <TableCell className="align-top">
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
                  <TableCell className="align-top text-sm whitespace-nowrap">{row.episodes}</TableCell>
                  <TableCell className="align-top">
                    <Badge variant="outline" className={seriesOutcomeStyles[row.outcome]}>
                      {t(`compare.seriesOutcomes.${row.outcome}`)}
                    </Badge>
                  </TableCell>
                  <TableCell className="max-w-sm align-top whitespace-normal">
                    {row.sonarr.length === 0 ? <span className="text-muted-foreground">{t('compare.nothing')}</span> : row.sonarr.map((g) => <ReleaseLine key={g.id} title={g.title} pack={g.fullSeason} quality={g.quality} />)}
                  </TableCell>
                  <TableCell className="max-w-sm align-top whitespace-normal">
                    {row.bigarr.length === 0 ? <span className="text-muted-foreground">{t('compare.nothing')}</span> : row.bigarr.map((p) => <ReleaseLine key={p.id} title={p.title} pack={p.fullSeason} quality={p.quality} />)}
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
                          {row.sighting === null && row.outcome === 'sonarrOnly' && <p className="text-xs text-muted-foreground">{t('compare.neverSeen')}</p>}
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
              <span className="flex flex-wrap gap-1">
                {verdict.approved ? (
                  <Badge variant="outline" className={okStyle}>
                    {t('compare.approved')}
                  </Badge>
                ) : (
                  verdict.rejections.map((r) => (
                    <Badge key={r} variant="outline" className={cn('whitespace-normal', rejectStyle)}>
                      {r}
                    </Badge>
                  ))
                )}
              </span>
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

/** The series page's own comparison, shown only when the show has rows. */
export function SeriesCompareSection({ tvdbId }: { tvdbId: number | null }) {
  const { t } = useTranslation()
  const query = useQuery({
    queryKey: ['compareSeries', 'series', tvdbId],
    queryFn: () => shadow.compareSeries({ days: 0, windowHours: 24, page: 1, pageSize: 50, tvdbId: tvdbId ?? undefined }),
    enabled: tvdbId !== null,
  })
  const rows = query.data?.groups.flatMap((g) => g.rows) ?? []
  if (rows.length === 0) return null
  return (
    <section className="flex flex-col gap-3" data-testid="series-compare-section">
      <h2 className="text-xl font-semibold tracking-tight">{t('compare.seriesTitle')}</h2>
      <p className="max-w-3xl text-sm text-muted-foreground">{t('compare.seriesHint')}</p>
      <SeriesCompareTable rows={rows} showSeries={false} />
    </section>
  )
}
