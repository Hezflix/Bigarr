import { Fragment, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { cn } from 'cn'
import { ChevronDownIcon, ChevronRightIcon, RefreshCwIcon } from 'lucide-react'
import type { TFunction } from 'i18next'
import { useTranslation } from 'react-i18next'
import { shadow, type CompareCounts, type CompareGrab, type CompareOutcome, type ComparePick, type CompareRow } from '@/api/client'
import { Poster } from '@/components/Poster'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { formatAge, formatBytes, formatDateTime } from '@/i18n'

const pageSize = 50
const outcomes: CompareOutcome[] = ['same', 'different', 'bigarrOnly', 'radarrOnly']
const periods = [1, 7, 30, 90, 0] as const
const windows = [6, 24, 72] as const

export const outcomeStyles: Record<CompareOutcome, string> = {
  same: 'border-emerald-600/30 bg-emerald-500/15 text-emerald-700 dark:border-emerald-400/30 dark:text-emerald-300',
  different: 'border-amber-600/30 bg-amber-500/15 text-amber-700 dark:border-amber-400/30 dark:text-amber-300',
  bigarrOnly: 'border-violet-600/30 bg-violet-500/15 text-violet-700 dark:border-violet-400/30 dark:text-violet-300',
  radarrOnly: 'border-sky-600/30 bg-sky-500/15 text-sky-700 dark:border-sky-400/30 dark:text-sky-300',
}
const formatStyle = 'border-violet-600/30 bg-violet-500/10 text-violet-700 dark:border-violet-400/30 dark:text-violet-300'
const okStyle = 'border-emerald-600/30 bg-emerald-500/15 text-emerald-700 dark:border-emerald-400/30 dark:text-emerald-300'
const rejectStyle = 'border-red-600/30 bg-red-500/10 text-red-700 dark:border-red-400/30 dark:text-red-300'

/** "10 min", "2 h", "3 d": how far apart the two sides chose. */
function formatGap(seconds: number): string {
  const s = Math.abs(seconds)
  if (s < 90) return `${Math.round(s)} s`
  if (s < 3600) return `${Math.round(s / 60)} min`
  if (s < 48 * 3600) return `${Math.round(s / 3600)} h`
  return `${Math.round(s / 86400)} d`
}

function leadText(t: TFunction, row: CompareRow): string | null {
  if (row.bigarrLeadSeconds === null) return null
  if (Math.abs(row.bigarrLeadSeconds) < 60) return t('compare.firstTogether')
  return row.bigarrLeadSeconds > 0
    ? t('compare.firstBigarr', { gap: formatGap(row.bigarrLeadSeconds) })
    : t('compare.firstRadarr', { gap: formatGap(row.bigarrLeadSeconds) })
}

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
  const [open, setOpen] = useState<Set<string>>(new Set())

  const query = useQuery({
    queryKey: ['compare', days, windowHours, version, outcome, page],
    queryFn: () =>
      shadow.compare({ days, windowHours, page, pageSize, version: version === 'all' ? undefined : version, outcome: outcome === 'all' ? undefined : outcome }),
    refetchInterval: 60_000,
  })
  const status = useQuery({ queryKey: ['shadow'], queryFn: shadow.status, staleTime: 60_000 })

  const data = query.data
  const pages = data ? Math.max(1, Math.ceil(data.total / pageSize)) : 1
  const toggle = (key: string) =>
    setOpen((current) => {
      const next = new Set(current)
      if (next.has(key)) next.delete(key)
      else next.add(key)
      return next
    })
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
            <div className="overflow-x-auto rounded-lg border">
              <Table data-testid="compare-table">
                <TableHeader>
                  <TableRow>
                    <TableHead className="w-8" />
                    <TableHead>{t('compare.movie')}</TableHead>
                    <TableHead>{t('compare.outcome')}</TableHead>
                    <TableHead>{t('compare.radarrGrabbed')}</TableHead>
                    <TableHead>{t('compare.bigarrPicked')}</TableHead>
                    <TableHead>{t('compare.when')}</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {data.rows.map((row) => {
                    const expanded = open.has(row.key)
                    return (
                      <Fragment key={row.key}>
                        <TableRow className="cursor-pointer" onClick={() => toggle(row.key)} data-outcome={row.outcome} data-expanded={expanded} data-testid="compare-row">
                          <TableCell className="align-top">
                            <Button
                              type="button"
                              variant="ghost"
                              size="icon"
                              className="size-7"
                              aria-expanded={expanded}
                              aria-label={expanded ? t('compare.collapse') : t('compare.expand')}
                              onClick={(e) => {
                                e.stopPropagation()
                                toggle(row.key)
                              }}
                            >
                              {expanded ? <ChevronDownIcon /> : <ChevronRightIcon />}
                            </Button>
                          </TableCell>
                          <TableCell className="min-w-[14rem] align-top">
                            <div className="flex items-start gap-3">
                              <Poster url={row.posterUrl} title={row.movieTitle} size="thumb" />
                              <div className="min-w-0">
                                {row.movieId ? (
                                  <Link
                                    to="/movies/$movieId"
                                    params={{ movieId: String(row.movieId) }}
                                    className="font-medium underline-offset-4 hover:underline"
                                    onClick={(e) => e.stopPropagation()}
                                  >
                                    {row.movieTitle}
                                  </Link>
                                ) : (
                                  <span className="font-medium">{row.movieTitle}</span>
                                )}
                                {row.year ? <span className="text-muted-foreground"> ({row.year})</span> : null}
                                <div className="mt-1">
                                  <Badge variant="secondary">{row.versionName}</Badge>
                                </div>
                              </div>
                            </div>
                          </TableCell>
                          <TableCell className="align-top">
                            <Badge variant="outline" className={outcomeStyles[row.outcome]}>
                              {t(`compare.outcomes.${row.outcome}`)}
                            </Badge>
                            {row.alsoPicked.length > 0 && <span className="mt-1 block text-xs text-muted-foreground">{t('compare.alsoPickedShort', { count: row.alsoPicked.length })}</span>}
                          </TableCell>
                          <TableCell className="min-w-[16rem] max-w-[24rem] align-top whitespace-normal">
                            {row.radarr ? <ReleaseCell title={row.radarr.title} quality={row.radarr.quality} score={row.radarr.customFormatScore} /> : <span className="text-xs text-muted-foreground">{t('compare.nothing')}</span>}
                          </TableCell>
                          <TableCell className="min-w-[16rem] max-w-[24rem] align-top whitespace-normal">
                            {row.bigarr ? <ReleaseCell title={row.bigarr.title} quality={row.bigarr.quality} score={row.bigarr.customFormatScore} /> : <span className="text-xs text-muted-foreground">{t('compare.nothing')}</span>}
                          </TableCell>
                          <TableCell className="align-top whitespace-nowrap text-xs">
                            <Tooltip>
                              <TooltipTrigger asChild>
                                <span>{formatAge(row.at)}</span>
                              </TooltipTrigger>
                              <TooltipContent>{formatDateTime(row.at)}</TooltipContent>
                            </Tooltip>
                            {leadText(t, row) && <span className="block text-muted-foreground">{leadText(t, row)}</span>}
                          </TableCell>
                        </TableRow>
                        {expanded && (
                          <TableRow className="hover:bg-transparent" data-testid="compare-details">
                            <TableCell />
                            <TableCell colSpan={5} className="whitespace-normal pb-4">
                              <Details row={row} />
                            </TableCell>
                          </TableRow>
                        )}
                      </Fragment>
                    )
                  })}
                </TableBody>
              </Table>
            </div>
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

function ReleaseCell({ title, quality, score }: { title: string; quality: string | null; score: number | null }) {
  const { t } = useTranslation()
  return (
    <>
      <span className="block break-all text-xs">{title}</span>
      <span className="mt-1 flex flex-wrap items-center gap-1">
        {quality && <Badge variant="secondary">{quality}</Badge>}
        {score !== null && <span className="text-xs text-muted-foreground">{t('search.score', { score })}</span>}
      </span>
    </>
  )
}

function Details({ row }: { row: CompareRow }) {
  const { t } = useTranslation()
  return (
    <div className="flex flex-col gap-3">
      <div className="grid gap-3 lg:grid-cols-2">
        {/* Radarr's side on the left, Bigarr's on the right; a one-sided row says why the other side chose nothing. */}
        {row.radarr ? <GrabCard grab={row.radarr} versionName={row.versionName} showVerdict={row.bigarr !== null} /> : <BigarrOnlyWhy row={row} />}
        {row.bigarr ? <PickCard pick={row.bigarr} tmdbId={row.tmdbId} showView={row.radarr !== null} /> : <RadarrOnlyWhy row={row} />}
      </div>
      {row.radarr && row.bigarr && row.outcome === 'different' && <p className="text-xs text-muted-foreground">{t('compare.differentHint')}</p>}
      {row.alsoPicked.length > 0 && (
        <div className="rounded-lg border p-3">
          <h4 className="text-sm font-medium">{t('compare.alsoPicked', { count: row.alsoPicked.length })}</h4>
          <ul className="mt-2 flex flex-col gap-1">
            {row.alsoPicked.map((pick) => (
              <li key={pick.id} className="flex flex-wrap items-center gap-2 text-xs">
                <span className="text-muted-foreground">{formatDateTime(pick.pickedAt)}</span>
                <span className="break-all">{pick.title}</span>
                {pick.quality && <Badge variant="secondary">{pick.quality}</Badge>}
                <span className="text-muted-foreground">{t('search.score', { score: pick.customFormatScore })}</span>
              </li>
            ))}
          </ul>
        </div>
      )}
    </div>
  )
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="grid grid-cols-[7rem_1fr] gap-2 text-xs">
      <dt className="text-muted-foreground">{label}</dt>
      <dd className="min-w-0 break-words">{children}</dd>
    </div>
  )
}

function Formats({ names, score }: { names: string[]; score: number | null }) {
  const { t } = useTranslation()
  return (
    <span className="flex flex-wrap items-center gap-1">
      {names.length === 0 ? <span className="text-muted-foreground">{t('compare.noFormats')}</span> : names.map((name) => <Badge key={name} variant="outline" className={formatStyle}>{name}</Badge>)}
      {score !== null && <span className="font-medium tabular-nums">{t('search.score', { score })}</span>}
    </span>
  )
}

function Card({ title, tone, children }: { title: string; tone?: 'radarr' | 'bigarr'; children: React.ReactNode }) {
  return (
    <section className={cn('flex flex-col gap-2 rounded-lg border p-3', tone === 'radarr' && 'border-sky-600/30', tone === 'bigarr' && 'border-violet-600/30')}>
      <h4 className="text-sm font-medium">{title}</h4>
      <dl className="flex flex-col gap-1.5">{children}</dl>
    </section>
  )
}

function GrabCard({ grab, versionName, showVerdict }: { grab: CompareGrab; versionName: string; showVerdict: boolean }) {
  const { t } = useTranslation()
  return (
    <Card title={t('compare.radarrCard', { name: grab.radarrName })} tone="radarr">
      <Field label={t('compare.release')}>
        <span className="break-all">{grab.title}</span>
      </Field>
      <Field label={t('compare.quality')}>{grab.quality ?? '—'}</Field>
      <Field label={t('compare.customFormats')}>
        <Formats names={grab.customFormats} score={grab.customFormatScore} />
      </Field>
      <Field label={t('compare.sizeGroup')}>
        {formatBytes(grab.size)} · {grab.releaseGroup ?? '—'}
      </Field>
      <Field label={t('compare.indexer')}>{grab.indexer ?? '—'}</Field>
      {grab.languages.length > 0 && <Field label={t('compare.languages')}>{grab.languages.join(', ')}</Field>}
      <Field label={t('compare.at')}>
        {formatDateTime(grab.grabbedAt)}
        {grab.publishedAt && <span className="text-muted-foreground"> · {t('compare.publishedAge', { age: formatAge(grab.publishedAt, new Date(grab.grabbedAt).getTime()) })}</span>}
      </Field>
      <Field label={t('compare.source')}>
        {grab.releaseSource ? t(`compare.sources.${grab.releaseSource}`, { defaultValue: grab.releaseSource }) : '—'} · {t(`compare.states.${grab.state}`)}
      </Field>
      {showVerdict && (
        <div className="mt-1 border-t pt-2">
          <Field label={t('compare.bigarrSays')}>
            <VerdictText grab={grab} versionName={versionName} />
          </Field>
        </div>
      )}
    </Card>
  )
}

/** Bigarr's engine on Radarr's release, when the grab was read: rejections or approval, score, the file the version had. */
function VerdictText({ grab, versionName }: { grab: CompareGrab; versionName: string }) {
  const { t } = useTranslation()
  const verdict = grab.bigarr
  if (!verdict) return <>—</>
  if (verdict.unmatched) return <span className="text-red-700 dark:text-red-300">{verdict.unmatched}</span>
  return (
    <span className="flex flex-col gap-1">
      <span className="flex flex-wrap items-center gap-1">
        {verdict.approved ? (
          <Badge variant="outline" className={okStyle}>
            {verdict.attributedVersion && verdict.attributedVersion !== versionName ? t('compare.approvedElsewhere', { version: verdict.attributedVersion }) : t('compare.approved')}
          </Badge>
        ) : (
          verdict.rejections.map((reason) => (
            <Badge key={reason} variant="outline" className={cn('whitespace-normal text-left', rejectStyle)}>
              {reason}
            </Badge>
          ))
        )}
        <span className="tabular-nums text-muted-foreground">{t('search.score', { score: verdict.customFormatScore })}</span>
      </span>
      {verdict.customFormats.length > 0 && <Formats names={verdict.customFormats} score={null} />}
      <span className="text-muted-foreground">
        {verdict.currentQuality ? t('compare.fileThen', { quality: verdict.currentQuality, score: verdict.currentScore ?? 0 }) : t('compare.noFileThen')}
      </span>
    </span>
  )
}

function PickCard({ pick, tmdbId, showView }: { pick: ComparePick; tmdbId: number; showView: boolean }) {
  const { t } = useTranslation()
  return (
    <Card title={t('compare.bigarrCard')} tone="bigarr">
      <Field label={t('compare.release')}>
        <span className="break-all">{pick.title}</span>
      </Field>
      <Field label={t('compare.quality')}>{pick.quality ?? '—'}</Field>
      <Field label={t('compare.customFormats')}>
        <Formats names={pick.customFormats} score={pick.customFormatScore} />
      </Field>
      <Field label={t('compare.sizeGroup')}>
        {formatBytes(pick.size)} · {pick.releaseGroup ?? '—'}
      </Field>
      <Field label={t('compare.indexer')}>{pick.indexer ?? '—'}</Field>
      {pick.languages.length > 0 && <Field label={t('compare.languages')}>{pick.languages.join(', ')}</Field>}
      <Field label={t('compare.at')}>
        {formatDateTime(pick.pickedAt)}
        {pick.publishDate && <span className="text-muted-foreground"> · {t('compare.publishedAge', { age: formatAge(pick.publishDate, new Date(pick.pickedAt).getTime()) })}</span>}
      </Field>
      <Field label={t('compare.source')}>
        {t(`compare.triggers.${pick.trigger.replace(':', '_')}`, { defaultValue: pick.trigger })}
        {pick.picks > 1 && <span className="text-muted-foreground"> · {t('compare.pickedTimes', { count: pick.picks })}</span>}
      </Field>
      {showView && (
        <div className="mt-1 border-t pt-2">
          <Field label={t('compare.radarrSays')}>
            <RadarrViewText pick={pick} tmdbId={tmdbId} />
          </Field>
        </div>
      )}
    </Card>
  )
}

/** Radarr's parse of Bigarr's pick, in words. */
function RadarrViewText({ pick, tmdbId }: { pick: ComparePick; tmdbId: number }) {
  const { t } = useTranslation()
  const view = pick.radarr
  if (!view) return <span className="text-muted-foreground">{t('compare.notAskedYet')}</span>
  if (view.error) return <span className="text-muted-foreground">{t('compare.radarrError', { error: view.error })}</span>
  const lines: string[] = []
  if (view.tmdbId === null) lines.push(t('compare.radarrNoFilm'))
  else if (view.tmdbId !== tmdbId) lines.push(t('compare.radarrOtherFilm'))
  if (view.monitored === false) lines.push(t('compare.radarrUnmonitored'))
  return (
    <span className="flex flex-col gap-1">
      {lines.map((line) => (
        <span key={line} className="text-red-700 dark:text-red-300">
          {line}
        </span>
      ))}
      <span className="flex flex-wrap items-center gap-1">
        {view.quality && <Badge variant="secondary">{view.quality}</Badge>}
        <Formats names={view.customFormats} score={view.customFormatScore} />
      </span>
      <span className="text-muted-foreground">
        {view.fileQuality ? t('compare.radarrFile', { quality: view.fileQuality, score: view.fileScore ?? 0 }) : t('compare.radarrNoFile')}
      </span>
    </span>
  )
}

/** Radarr grabbed, Bigarr did not: whether its feeds ever carried the release, and what it thought of it. */
function RadarrOnlyWhy({ row }: { row: CompareRow }) {
  const { t } = useTranslation()
  const grab = row.radarr!
  const sighting = row.sighting
  const verdict = sighting?.verdict
  return (
    <Card title={t('compare.whyNotBigarr')} tone="bigarr">
      <Field label={t('compare.feeds')}>
        {!sighting ? (
          <span>
            {t('compare.neverSeen')}
            {grab.releaseSource && grab.releaseSource !== 'Rss' && (
              <span className="block text-muted-foreground">{t('compare.foundBySearch', { source: t(`compare.sources.${grab.releaseSource}`, { defaultValue: grab.releaseSource }) })}</span>
            )}
          </span>
        ) : (
          <span className="flex flex-col gap-1">
            <span>{t('compare.seen', { date: formatDateTime(sighting.seenAt), indexer: sighting.indexer })}</span>
            {!sighting.matched ? (
              <span className="text-red-700 dark:text-red-300">{t('compare.seenUnmatched', { reason: sighting.unmatchedReason ?? '?' })}</span>
            ) : verdict && !verdict.approved ? (
              <span className="flex flex-wrap gap-1">
                {verdict.rejections.map((reason) => (
                  <Badge key={reason} variant="outline" className={cn('whitespace-normal text-left', rejectStyle)}>
                    {reason}
                  </Badge>
                ))}
              </span>
            ) : sighting.attributedVersion && sighting.attributedVersion !== row.versionName ? (
              <span>{t('compare.seenElsewhere', { version: sighting.attributedVersion })}</span>
            ) : (
              <span>{t('compare.seenBeaten')}</span>
            )}
          </span>
        )}
      </Field>
      <Field label={t('compare.engineNow')}>
        <span className="flex flex-col gap-1">
          <VerdictText grab={grab} versionName={row.versionName} />
          {grab.bigarr?.approved && !sighting && <span className="text-muted-foreground">{t('compare.engineWouldTake')}</span>}
        </span>
      </Field>
    </Card>
  )
}

/** Bigarr picked, Radarr did not: what Radarr makes of the release and where its film stands. */
function BigarrOnlyWhy({ row }: { row: CompareRow }) {
  const { t } = useTranslation()
  return (
    <Card title={t('compare.whyNotRadarr')} tone="radarr">
      <Field label={t('compare.radarrSays')}>
        <RadarrViewText pick={row.bigarr!} tmdbId={row.tmdbId} />
      </Field>
      <p className="text-xs text-muted-foreground">{t('compare.bigarrOnlyHint')}</p>
    </Card>
  )
}
