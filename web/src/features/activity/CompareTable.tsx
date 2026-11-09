import { Fragment, useState } from 'react'
import { Link } from '@tanstack/react-router'
import { cn } from 'cn'
import { ChevronDownIcon, ChevronRightIcon } from 'lucide-react'
import type { TFunction } from 'i18next'
import { useTranslation } from 'react-i18next'
import type { CompareGrab, CompareOutcome, ComparePick, CompareRow } from '@/api/client'
import { Poster } from '@/components/Poster'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { formatAge, formatBytes, formatDateTime } from '@/i18n'

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
 * Radarr's grabs next to Bigarr's picks, one row per pair, each opening on both sides with their reasons. Activity →
 * Compare lists every film; the film page shows its own rows without the film column.
 */
export function CompareTable({ rows, showMovie = true }: { rows: CompareRow[]; showMovie?: boolean }) {
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
      <Table data-testid="compare-table">
        <TableHeader>
          <TableRow>
            <TableHead className="w-8" />
            <TableHead>{showMovie ? t('compare.movie') : t('compare.version')}</TableHead>
            <TableHead>{t('compare.outcome')}</TableHead>
            <TableHead>{t('compare.radarrGrabbed')}</TableHead>
            <TableHead>{t('compare.bigarrPicked')}</TableHead>
            <TableHead>{t('compare.when')}</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {rows.map((row) => {
            const expanded = open.has(row.key)
            // Who chose first, when both did and more than a minute apart: positive means Bigarr.
            const lead = row.bigarrLeadSeconds !== null && Math.abs(row.bigarrLeadSeconds) >= 60 ? row.bigarrLeadSeconds : null
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
                  {!showMovie ? (
                    <TableCell className="align-top">
                      <Badge variant="secondary">{row.versionName}</Badge>
                    </TableCell>
                  ) : (
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
                  )}
                  <TableCell className="align-top">
                    <Badge variant="outline" className={outcomeStyles[row.outcome]}>
                      {t(`compare.outcomes.${row.outcome}`)}
                    </Badge>
                    {row.alsoPicked.length > 0 && <span className="mt-1 block text-xs text-muted-foreground">{t('compare.alsoPickedShort', { count: row.alsoPicked.length })}</span>}
                  </TableCell>
                  <TableCell className="min-w-[16rem] max-w-[24rem] align-top whitespace-normal">
                    {row.radarr ? (
                      <ReleaseCell title={row.radarr.title} quality={row.radarr.quality} score={row.radarr.customFormatScore} at={row.radarr.grabbedAt} first={lead !== null && lead < 0} />
                    ) : (
                      <span className="text-xs text-muted-foreground">{t('compare.nothing')}</span>
                    )}
                  </TableCell>
                  <TableCell className="min-w-[16rem] max-w-[24rem] align-top whitespace-normal">
                    {row.bigarr ? (
                      <ReleaseCell title={row.bigarr.title} quality={row.bigarr.quality} score={row.bigarr.customFormatScore} at={row.bigarr.pickedAt} first={lead !== null && lead > 0} />
                    ) : (
                      <span className="text-xs text-muted-foreground">{t('compare.nothing')}</span>
                    )}
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
  )
}

/** "06/11, 19:00": when each side chose, in the cell, so the faster one shows without reading the When column. */
function shortDateTime(iso: string): string {
  return new Intl.DateTimeFormat('en-GB', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit', hourCycle: 'h23' }).format(new Date(iso))
}

function ReleaseCell({ title, quality, score, at, first }: { title: string; quality: string | null; score: number | null; at: string; first: boolean }) {
  const { t } = useTranslation()
  return (
    <>
      <span className="mb-0.5 flex items-center gap-1.5 text-xs tabular-nums text-muted-foreground" data-testid="compare-cell-date">
        <Tooltip>
          <TooltipTrigger asChild>
            <span>{shortDateTime(at)}</span>
          </TooltipTrigger>
          <TooltipContent>{formatDateTime(at)}</TooltipContent>
        </Tooltip>
        {first && (
          <Badge variant="outline" className="h-4 px-1 text-[10px] leading-none border-emerald-600/30 bg-emerald-500/15 text-emerald-700 dark:border-emerald-400/30 dark:text-emerald-300">
            {t('compare.first')}
          </Badge>
        )}
      </span>
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
        {verdict.currentQuality
          ? t(verdict.currentIsPick ? 'compare.pickThen' : 'compare.fileThen', { quality: verdict.currentQuality, score: verdict.currentScore ?? 0 })
          : t('compare.noFileThen')}
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
