import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { cn } from 'cn'
import { ExternalLinkIcon, RefreshCwIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ApiError, rss, type RssRelease } from '@/api/client'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Label } from '@/components/ui/label'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { approvedStyle as grabStyle, permanentStyle, temporaryStyle } from '@/features/search/releaseTable'
import { formatAge, formatBytes, formatDateTime } from '@/i18n'

const pickStyle = 'border-violet-600/30 bg-violet-500/15 text-violet-700 dark:border-violet-400/30 dark:text-violet-300'
const attributedStyle = 'border-blue-600/30 bg-blue-500/15 text-blue-700 dark:border-blue-400/30 dark:text-blue-300'

/** What the RSS loop saw: every release of every feed, judged against the library. */
export function ReleasesPage() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [matchedOnly, setMatchedOnly] = useState(true)
  const status = useQuery({ queryKey: ['rss', 'status'], queryFn: rss.status, refetchInterval: 30_000 })
  const releases = useQuery({
    queryKey: ['rss', 'releases', matchedOnly],
    queryFn: () => rss.releases(matchedOnly ? true : undefined),
    refetchInterval: 60_000,
  })
  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['rss'] })
  const sync = useMutation({
    mutationFn: rss.sync,
    onSuccess: async (result) => {
      toast.success(
        result.picked > 0
          ? t('rss.syncedShadow', { fetched: result.fetched, new: result.new, matched: result.matched, picked: result.picked })
          : t('rss.synced', { fetched: result.fetched, new: result.new, matched: result.matched, grabbed: result.grabbed }),
      )
      await invalidate()
    },
    onError: async (e) => {
      toast.error(e instanceof ApiError ? e.message : t('rss.syncFailed'))
      await invalidate()
    },
  })

  const s = status.data
  const failing = s?.indexers.filter((i) => i.lastError) ?? []
  /** False when RSS or the scheduler is off: then a pass only runs on Sync now. */
  const onItsOwn = !s || (s.enabled && s.schedulerEnabled)

  return (
    <>
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="text-2xl font-semibold tracking-tight">{t('rss.title')}</h1>
        <div className="ml-auto flex flex-wrap items-center gap-3">
          <div className="flex items-center gap-2">
            <Checkbox id="rss-matched" checked={matchedOnly} onCheckedChange={(v) => setMatchedOnly(v === true)} />
            <Label htmlFor="rss-matched" className="font-normal">
              {t('rss.matchedOnly')}
            </Label>
          </div>
          <Button type="button" size="sm" onClick={() => sync.mutate()} disabled={sync.isPending || s?.running} data-testid="rss-sync">
            <RefreshCwIcon className={sync.isPending || s?.running ? 'animate-spin' : undefined} />
            {sync.isPending || s?.running ? t('rss.syncing') : t('rss.sync')}
          </Button>
        </div>
      </div>

      {s && (
        <p className="text-sm text-muted-foreground" data-testid="rss-status">
          {!s.enabled ? t('rss.disabled') : s.schedulerEnabled ? t('rss.schedule', { minutes: s.intervalMinutes }) : t('rss.schedulerOff')}
          {s.lastRunAt ? ` · ${t('rss.lastRun', { date: formatDateTime(s.lastRunAt) })}` : ` · ${t('rss.neverRan')}`}
          {s.nextRunAt ? ` · ${t('rss.nextRun', { date: formatDateTime(s.nextRunAt) })}` : ''}
          {` · ${t('rss.stored', { stored: s.stored, matched: s.matched, attributed: s.wouldGrab, grabbed: s.grabbed })}`}
          {s.picked > 0 ? ` · ${t('rss.storedPicked', { picked: s.picked })}` : ''}
        </p>
      )}

      {failing.length > 0 && (
        <Alert>
          <AlertDescription>
            <ul className="flex flex-col gap-1">
              {failing.map((i) => (
                <li key={i.indexerId}>
                  <span className="font-medium">{i.indexer}</span>: {i.lastError}
                </li>
              ))}
            </ul>
          </AlertDescription>
        </Alert>
      )}

      {releases.isPending && (
        <div className="flex flex-col gap-2">
          <Skeleton className="h-8 w-full" />
          <Skeleton className="h-8 w-full" />
        </div>
      )}

      {releases.data?.length === 0 && (
        <p className="text-sm text-muted-foreground">{s?.lastRunAt ? t(matchedOnly ? 'rss.emptyMatched' : 'rss.empty') : t(onItsOwn ? 'rss.hint' : 'rss.hintManual')}</p>
      )}

      {releases.data && releases.data.length > 0 && (
        <div className="overflow-x-auto rounded-lg border">
          <Table data-testid="rss-table">
            <TableHeader>
              <TableRow>
                <TableHead>{t('search.age')}</TableHead>
                <TableHead>{t('search.release')}</TableHead>
                <TableHead>{t('search.indexer')}</TableHead>
                <TableHead>{t('search.size')}</TableHead>
                <TableHead>{t('search.peers')}</TableHead>
                <TableHead>{t('rss.movie')}</TableHead>
                <TableHead>{t('search.verdict')}</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {releases.data.map((r) => (
                <ReleaseRow key={`${r.indexerId}:${r.guid}`} release={r} />
              ))}
            </TableBody>
          </Table>
        </div>
      )}

      <p className="text-xs text-muted-foreground">{t('rss.grabHint')}</p>
    </>
  )
}

function ReleaseRow({ release }: { release: RssRelease }) {
  const { t } = useTranslation()
  const wouldGrab = release.attributedVersionId !== null
  const grabbed = release.grabId !== null
  const picked = release.shadowPickId !== null
  const flags = release.flags === 'none' ? [] : release.flags.split(', ')
  const details = [release.edition, release.releaseGroup, release.languages.length > 0 ? release.languages.join(', ') : null, release.isRepack ? t('search.repack') : null].filter(
    (d): d is string => !!d,
  )
  const dimmed = !wouldGrab

  return (
    <TableRow className={cn(dimmed && 'text-muted-foreground')} data-would-grab={wouldGrab} data-grabbed={grabbed} data-picked={picked}>
      <TableCell className="align-top whitespace-nowrap">
        <Tooltip>
          <TooltipTrigger asChild>
            <span>{formatAge(release.publishDate)}</span>
          </TooltipTrigger>
          <TooltipContent>{formatDateTime(release.publishDate)}</TooltipContent>
        </Tooltip>
      </TableCell>
      <TableCell className="min-w-[22rem] align-top whitespace-normal">
        <span className={cn('flex items-start gap-1 break-all', wouldGrab && 'font-medium text-foreground')}>
          {release.infoUrl ? (
            <a href={release.infoUrl} target="_blank" rel="noreferrer" className="inline-flex items-center gap-1 underline-offset-4 hover:underline">
              {release.title}
              <ExternalLinkIcon className="size-3 shrink-0" />
            </a>
          ) : (
            release.title
          )}
        </span>
        <span className="mt-1 flex flex-wrap items-center gap-1">
          <Badge variant="secondary">{release.quality}</Badge>
          {flags.map((flag) => (
            <Badge key={flag} variant="outline">
              {t(`search.flags.${flag}`, { defaultValue: flag })}
            </Badge>
          ))}
          {release.customFormats.map((format) => (
            <Badge key={format} variant="outline" className="border-violet-600/30 bg-violet-500/10 text-violet-700 dark:border-violet-400/30 dark:text-violet-300">
              {format}
            </Badge>
          ))}
          {details.length > 0 && <span className="text-xs text-muted-foreground">{details.join(' · ')}</span>}
        </span>
      </TableCell>
      <TableCell className="align-top whitespace-nowrap">{release.indexer}</TableCell>
      <TableCell className="align-top whitespace-nowrap">{formatBytes(release.size)}</TableCell>
      <TableCell className="align-top whitespace-nowrap">
        {release.seeders ?? '—'} <span className="text-muted-foreground">/ {release.leechers ?? '—'}</span>
      </TableCell>
      <TableCell className="min-w-[12rem] align-top whitespace-normal">
        {release.movie ? (
          <Link to="/movies/$movieId" params={{ movieId: String(release.movie.id) }} className="font-medium text-foreground underline-offset-4 hover:underline">
            {release.movie.title}
            {release.movie.year ? <span className="font-normal text-muted-foreground"> ({release.movie.year})</span> : null}
          </Link>
        ) : release.series ? (
          <span className="flex flex-col items-start gap-0.5" data-testid="rss-series">
            <Link to="/series/$seriesId" params={{ seriesId: String(release.series.id) }} className="font-medium text-foreground underline-offset-4 hover:underline">
              {release.series.title}
              {release.series.year ? <span className="font-normal text-muted-foreground"> ({release.series.year})</span> : null}
            </Link>
            {release.episodes ? <span className="text-xs tabular-nums">{release.episodes}</span> : <span className="text-xs">{release.unmatchedReason}</span>}
          </span>
        ) : (
          <span className="text-xs">{release.unmatchedReason}</span>
        )}
      </TableCell>
      <TableCell className="min-w-[16rem] align-top whitespace-normal">
        {grabbed ? (
          <Badge variant="outline" className={grabStyle}>
            {t('rss.grabbed', { version: release.attributedVersionName ?? '' })}
          </Badge>
        ) : picked ? (
          <Tooltip>
            <TooltipTrigger asChild>
              <Badge variant="outline" className={pickStyle}>
                {t('rss.picked', { version: release.attributedVersionName ?? '' })}
              </Badge>
            </TooltipTrigger>
            <TooltipContent className="max-w-xs">{t('rss.pickedHint')}</TooltipContent>
          </Tooltip>
        ) : wouldGrab ? (
          <span className="flex flex-col items-start gap-1">
            <Badge variant="outline" className={attributedStyle}>
              {t('rss.attributed', { version: release.attributedVersionName ?? '' })}
            </Badge>
            {release.grabError && <span className="text-xs text-muted-foreground">{release.grabError}</span>}
          </span>
        ) : release.movie || (release.series && release.episodes) ? (
          <ul className="flex flex-col gap-1">
            {release.versions.map((v) => (
              <li key={v.versionId} className="flex flex-wrap items-center gap-1">
                <span className="text-xs font-medium">
                  {v.versionName} <span className="font-normal text-muted-foreground">· {t('search.score', { score: v.customFormatScore })}</span>
                </span>
                {v.rejections.map((rej) => (
                  <Badge key={rej.reason} variant="outline" className={cn('whitespace-normal text-left', rej.temporary ? temporaryStyle : permanentStyle)}>
                    {rej.reason}
                  </Badge>
                ))}
              </li>
            ))}
          </ul>
        ) : (
          <span className="text-xs">{t('rss.noMatch')}</span>
        )}
      </TableCell>
    </TableRow>
  )
}
