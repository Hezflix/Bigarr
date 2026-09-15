import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { cn } from 'cn'
import { CopyPlusIcon, DownloadIcon, ExternalLinkIcon, SearchIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ApiError, queue, search, type Movie, type Rejection, type Release } from '@/api/client'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { OneShotGrabDialog } from '@/features/search/OneShotGrabDialog'
import { formatAge, formatBytes } from '@/i18n'

const approvedStyle = 'border-emerald-600/30 bg-emerald-500/15 text-emerald-700 dark:border-emerald-400/30 dark:text-emerald-300'
const temporaryStyle = 'border-amber-600/30 bg-amber-500/15 text-amber-700 dark:border-amber-400/30 dark:text-amber-300'
const permanentStyle = 'border-red-600/30 bg-red-500/10 text-red-700 dark:border-red-400/30 dark:text-red-300'

/** The verdict of one version on one release, or why the release is not the film at all. */
function verdictOf(release: Release, versionId: number): { approved: boolean; rejections: Rejection[]; score: number | null } {
  if (!release.matchesMovie) return { approved: false, rejections: release.rejections, score: null }
  const decision = release.versions.find((v) => v.versionId === versionId)
  return { approved: decision?.approved ?? false, rejections: decision?.rejections ?? [], score: decision?.customFormatScore ?? null }
}

/** Interactive search for one film: every indexer once, every release judged by the selected version (blueprint §5). */
export function InteractiveSearch({ movie }: { movie: Movie }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [versionId, setVersionId] = useState<number>(movie.versions.find((v) => v.isDefault)?.id ?? movie.versions[0]?.id ?? 0)
  // A remount (e.g. navigating away and back) resets this component's state, but the query cache below is
  // shared and outlives it: without this check "armed" would start false while stale results are still shown,
  // and the first click after remount would only flip it instead of refetching (grabs/imports since then are missed).
  const [armed, setArmed] = useState(() => queryClient.getQueryData(['search', movie.id]) !== undefined)
  const [oneShotFor, setOneShotFor] = useState<Release | null>(null)
  const query = useQuery({
    queryKey: ['search', movie.id],
    queryFn: () => search.movie(movie.id),
    enabled: armed,
    staleTime: Number.POSITIVE_INFINITY,
    retry: false,
  })

  const run = () => {
    if (armed) void query.refetch()
    else setArmed(true)
  }

  const selectedId = movie.versions.some((v) => v.id === versionId) ? versionId : (movie.versions[0]?.id ?? 0)
  const selected = movie.versions.find((v) => v.id === selectedId)

  // Grab: the release goes back as the search returned it (the search keeps no state), for the selected version.
  const grab = useMutation({
    mutationFn: (release: Release) =>
      queue.grab({
        versionId: selectedId,
        indexerId: release.indexerId,
        guid: release.guid,
        title: release.title,
        downloadUrl: release.downloadUrl,
        magnetUrl: release.magnetUrl,
        infoHash: release.infoHash,
        size: release.size,
        seeders: release.seeders,
        flags: release.flags,
        protocol: release.protocol,
        indexer: release.indexer,
      }),
    onSuccess: async (result, release) => {
      toast.success(t('search.grabbed', { title: release.title, client: result.downloadClient }))
      await queryClient.invalidateQueries({ queryKey: ['movie', movie.id] })
      await queryClient.invalidateQueries({ queryKey: ['movies'] })
      await queryClient.invalidateQueries({ queryKey: ['queue'] })
      await query.refetch()
    },
    onError: (e) => toast.error(e instanceof ApiError ? e.message : t('search.grabFailed')),
  })
  const releases = query.data
    ? [...query.data.releases].sort((a, b) => Number(verdictOf(b, selectedId).approved) - Number(verdictOf(a, selectedId).approved))
    : []
  const approvedCount = releases.filter((r) => verdictOf(r, selectedId).approved).length

  return (
    <section className="flex flex-col gap-3" data-testid="interactive-search">
      <div className="flex flex-wrap items-center gap-3">
        <h2 className="text-xl font-semibold tracking-tight">{t('search.title')}</h2>
        <div className="ml-auto flex flex-wrap items-center gap-2">
          <Label htmlFor="search-version" className="text-muted-foreground">
            {t('search.version')}
          </Label>
          <Select value={String(selectedId)} onValueChange={(value) => setVersionId(Number(value))}>
            <SelectTrigger id="search-version" className="min-w-48">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {movie.versions.map((v) => (
                <SelectItem key={v.id} value={String(v.id)}>
                  {v.name}
                  {v.isDefault ? ` · ${t('versions.default').toLowerCase()}` : ''}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
          <Button type="button" onClick={run} disabled={query.isFetching || movie.versions.length === 0} data-testid="search-run">
            <SearchIcon className={query.isFetching ? 'animate-pulse' : undefined} />
            {query.isFetching ? t('search.searching') : armed ? t('search.searchAgain') : t('search.search')}
          </Button>
        </div>
      </div>

      {!armed && <p className="text-sm text-muted-foreground">{t('search.hint')}</p>}

      {query.isFetching && !query.data && (
        <div className="flex flex-col gap-2">
          <Skeleton className="h-8 w-full" />
          <Skeleton className="h-8 w-full" />
          <Skeleton className="h-8 w-2/3" />
        </div>
      )}

      {query.error && (
        <Alert variant="destructive">
          <AlertDescription>{query.error instanceof ApiError ? query.error.message : t('search.failed')}</AlertDescription>
        </Alert>
      )}

      {query.data && (
        <>
          <p className="text-xs text-muted-foreground">
            {t('search.summary', { releases: query.data.releases.length, indexers: query.data.indexersQueried, approved: approvedCount })}
          </p>

          {query.data.indexerErrors.length > 0 && (
            <Alert>
              <AlertDescription>
                <ul className="flex flex-col gap-1">
                  {query.data.indexerErrors.map((e) => (
                    <li key={e.indexerId}>
                      <span className="font-medium">{e.indexer}</span>: {e.message}
                    </li>
                  ))}
                </ul>
              </AlertDescription>
            </Alert>
          )}

          {releases.length === 0 ? (
            <p className="text-sm text-muted-foreground">{t('search.noResults')}</p>
          ) : (
            <div className="max-h-[28rem] overflow-y-auto overflow-x-auto rounded-lg border">
              <Table data-testid="search-results">
                <TableHeader className="sticky top-0 z-10 bg-background">
                  <TableRow>
                    <TableHead>{t('search.age')}</TableHead>
                    <TableHead>{t('search.release')}</TableHead>
                    <TableHead>{t('search.indexer')}</TableHead>
                    <TableHead>{t('search.size')}</TableHead>
                    <TableHead>{t('search.peers')}</TableHead>
                    <TableHead>{t('search.verdict')}</TableHead>
                    <TableHead className="text-right">{t('versions.actions')}</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {releases.map((release) => (
                    <ReleaseRow
                      key={`${release.indexerId}:${release.guid}`}
                      release={release}
                      versionId={selectedId}
                      downloading={selected?.status === 'downloading'}
                      grabbing={grab.isPending && grab.variables?.guid === release.guid}
                      onGrab={() => grab.mutate(release)}
                      onGrabOneShot={() => setOneShotFor(release)}
                    />
                  ))}
                </TableBody>
              </Table>
            </div>
          )}

          <p className="text-xs text-muted-foreground">{t('search.grabHint')}</p>
        </>
      )}

      {oneShotFor && selected && <OneShotGrabDialog movie={movie} release={oneShotFor} defaultProfileId={selected.qualityProfileId} onClose={() => setOneShotFor(null)} />}
    </section>
  )
}

function ReleaseRow({
  release,
  versionId,
  downloading,
  grabbing,
  onGrab,
  onGrabOneShot,
}: {
  release: Release
  versionId: number
  /** The selected version already has an active download: a second grab would be refused. */
  downloading: boolean
  grabbing: boolean
  onGrab: () => void
  /** Creates a one-shot version for this release and grabs it there (blueprint §3.3). */
  onGrabOneShot: () => void
}) {
  const { t } = useTranslation()
  const { approved, rejections, score } = verdictOf(release, versionId)
  const grabbable = release.protocol === 'torrent' && (!!release.downloadUrl || !!release.magnetUrl || !!release.infoHash)
  const flags = release.flags === 'none' ? [] : release.flags.split(', ')
  const details = [
    release.edition,
    release.releaseGroup,
    release.languages.length > 0 ? release.languages.join(', ') : null,
    release.isRepack ? t('search.repack') : null,
  ].filter((d): d is string => !!d)

  return (
    <TableRow className={cn(!approved && 'text-muted-foreground')} data-approved={approved}>
      <TableCell className="align-top whitespace-nowrap">
        <Tooltip>
          <TooltipTrigger asChild>
            <span>{formatAge(release.publishDate)}</span>
          </TooltipTrigger>
          <TooltipContent>{new Date(release.publishDate).toLocaleString()}</TooltipContent>
        </Tooltip>
      </TableCell>
      <TableCell className="min-w-[24rem] align-top whitespace-normal">
        <span className={cn('flex items-start gap-1 break-all', approved && 'font-medium text-foreground')}>
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
      <TableCell className="min-w-[16rem] align-top whitespace-normal">
        {approved ? (
          <Badge variant="outline" className={approvedStyle}>
            {t('search.approved')}
          </Badge>
        ) : (
          <span className="flex flex-wrap gap-1">
            {rejections.map((r) => (
              <Badge key={r.reason} variant="outline" className={cn('whitespace-normal text-left', r.temporary ? temporaryStyle : permanentStyle)}>
                {r.reason}
              </Badge>
            ))}
          </span>
        )}
        {score !== null && <span className="mt-1 block text-xs text-muted-foreground">{t('search.score', { score })}</span>}
      </TableCell>
      <TableCell className="align-top">
        <div className="flex justify-end gap-1">
          <Tooltip>
            <TooltipTrigger asChild>
              <span>
                <Button type="button" variant="ghost" size="icon" onClick={onGrabOneShot} disabled={!grabbable} aria-label={t('search.grabOneShot')} data-testid="grab-oneshot">
                  <CopyPlusIcon />
                </Button>
              </span>
            </TooltipTrigger>
            <TooltipContent>{t('search.grabOneShot')}</TooltipContent>
          </Tooltip>
          <Tooltip>
            <TooltipTrigger asChild>
              <span>
                <Button
                  type="button"
                  variant={approved ? 'default' : 'outline'}
                  size="icon"
                  onClick={onGrab}
                  disabled={grabbing || downloading || !grabbable}
                  aria-label={t('search.grab')}
                  data-testid="grab"
                >
                  <DownloadIcon className={grabbing ? 'animate-pulse' : undefined} />
                </Button>
              </span>
            </TooltipTrigger>
            <TooltipContent>{downloading ? t('search.grabBlocked') : !grabbable ? t('search.grabNoLink') : approved ? t('search.grab') : t('search.grabAnyway')}</TooltipContent>
          </Tooltip>
        </div>
      </TableCell>
    </TableRow>
  )
}
