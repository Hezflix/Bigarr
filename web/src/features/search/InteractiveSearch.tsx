import { useState, type FormEvent, type ReactNode } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { cn } from 'cn'
import { ArrowDownIcon, ArrowUpIcon, CheckIcon, ChevronDownIcon, ClockIcon, CopyPlusIcon, DownloadIcon, ExternalLinkIcon, SearchIcon, XIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ApiError, queue, search, type Movie, type Release, type Version, type VersionDecision } from '@/api/client'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { Input } from '@/components/ui/input'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { OneShotGrabDialog } from '@/features/search/OneShotGrabDialog'
import { formatAge, formatBytes, formatDateTime } from '@/i18n'

const approvedStyle = 'border-emerald-600/30 bg-emerald-500/15 text-emerald-700 dark:border-emerald-400/30 dark:text-emerald-300'
const temporaryStyle = 'border-amber-600/30 bg-amber-500/15 text-amber-700 dark:border-amber-400/30 dark:text-amber-300'
const permanentStyle = 'border-red-600/30 bg-red-500/10 text-red-700 dark:border-red-400/30 dark:text-red-300'

/** "fit" is the server's ranking (best first, fixed direction, like Radarr's release weight); the others toggle. */
type SortKey = 'fit' | 'age' | 'title' | 'indexer' | 'size' | 'peers'
type Sort = { key: SortKey; direction: 'asc' | 'desc' }

const titleCollator = new Intl.Collator(undefined, { sensitivity: 'base', numeric: true })

/** Ascending comparators; ties keep the server's ranking (the sort is stable). */
const comparators: Record<Exclude<SortKey, 'fit'>, (a: Release, b: Release) => number> = {
  age: (a, b) => Date.parse(b.publishDate) - Date.parse(a.publishDate),
  title: (a, b) => titleCollator.compare(a.title, b.title),
  indexer: (a, b) => titleCollator.compare(a.indexer, b.indexer),
  size: (a, b) => a.size - b.size,
  peers: (a, b) => (a.seeders ?? -1) - (b.seeders ?? -1),
}

function sorted(releases: Release[], sort: Sort): Release[] {
  if (sort.key === 'fit') return releases
  const compare = comparators[sort.key]
  const sign = sort.direction === 'asc' ? 1 : -1
  return [...releases].sort((a, b) => sign * compare(a, b))
}

/** The film is acceptable for at least one version, and the release is the film. */
function fits(release: Release) {
  return release.matchesMovie && release.versions.some((v) => v.approved)
}

/** The last search shown for this film, so a remount (navigating away and back) shows it again with its text. */
function cachedSearchText(queryClient: ReturnType<typeof useQueryClient>, movieId: number): string | null {
  const latest = queryClient
    .getQueryCache()
    .findAll({ queryKey: ['search', movieId] })
    .filter((q) => q.state.data !== undefined)
    .sort((a, b) => b.state.dataUpdatedAt - a.state.dataUpdatedAt)[0]
  return latest ? String(latest.queryKey[2] ?? '') : null
}

/**
 * Interactive search for one film: every indexer once, every release judged by every version (blueprint §5). The
 * version is chosen when grabbing; each release suggests the one it fits. A typed text replaces the film's ids and
 * titles for films listed under another name; the releases are still judged against this film's versions.
 */
export function InteractiveSearch({ movie }: { movie: Movie }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  // null: nothing searched yet. '' is the film's own search (ids and titles), anything else the user's text.
  const [submitted, setSubmitted] = useState<string | null>(() => cachedSearchText(queryClient, movie.id))
  const [text, setText] = useState(submitted ?? '')
  const [sort, setSort] = useState<Sort>({ key: 'fit', direction: 'asc' })
  const [oneShot, setOneShot] = useState<{ release: Release; profileId: number } | null>(null)
  const query = useQuery({
    queryKey: ['search', movie.id, submitted ?? ''],
    queryFn: () => search.movie(movie.id, { query: submitted || undefined }),
    enabled: submitted !== null,
    staleTime: Number.POSITIVE_INFINITY,
    retry: false,
  })

  // Every click searches again: grabs and imports since the last results change the verdicts.
  const run = (next: string) => {
    if (next === submitted) {
      void query.refetch()
      return
    }
    queryClient.removeQueries({ queryKey: ['search', movie.id, next], exact: true })
    setSubmitted(next)
  }
  const submit = (e: FormEvent) => {
    e.preventDefault()
    run(text.trim())
  }

  const versionById = new Map(movie.versions.map((v) => [v.id, v]))
  const defaultVersion = movie.versions.find((v) => v.isDefault) ?? movie.versions[0]

  // Grab: the release goes back as the search returned it (the search keeps no state), for the version picked.
  const grab = useMutation({
    mutationFn: ({ release, versionId }: { release: Release; versionId: number }) =>
      queue.grab({
        versionId,
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
        publishDate: release.publishDate,
      }),
    onSuccess: async (result, { release, versionId }) => {
      const version = versionById.get(versionId)?.name ?? ''
      // Shadow mode: the release is recorded as Bigarr's pick, nothing reaches a client.
      if (result.shadow) toast.success(t('search.picked', { title: release.title, version }))
      else toast.success(t('search.grabbed', { title: release.title, version, client: result.downloadClient ?? '' }))
      await queryClient.invalidateQueries({ queryKey: ['movie', movie.id] })
      await queryClient.invalidateQueries({ queryKey: ['movies'] })
      await queryClient.invalidateQueries({ queryKey: ['queue'] })
      await query.refetch()
    },
    onError: (e) => toast.error(e instanceof ApiError ? e.message : t('search.grabFailed')),
  })

  const onSort = (key: SortKey) =>
    setSort((current) => (key === 'fit' ? { key, direction: 'asc' } : current.key === key ? { key, direction: current.direction === 'asc' ? 'desc' : 'asc' } : { key, direction: 'asc' }))

  const data = query.data
  const releases = data ? sorted(data.releases, sort) : []
  const acceptable = movie.versions.map((v) => ({
    version: v,
    count: data?.releases.filter((r) => r.matchesMovie && r.versions.some((d) => d.versionId === v.id && d.approved)).length ?? 0,
  }))

  return (
    <section className="flex flex-col gap-3" data-testid="interactive-search">
      <div className="flex flex-wrap items-center gap-3">
        <h2 className="text-xl font-semibold tracking-tight">{t('search.title')}</h2>
        <form onSubmit={submit} className="ml-auto flex flex-wrap items-center gap-2" role="search">
          <Input
            value={text}
            onChange={(e) => setText(e.target.value)}
            placeholder={t('search.queryPlaceholder')}
            aria-label={t('search.queryLabel')}
            maxLength={200}
            className="w-72"
            data-testid="search-query"
          />
          <Button type="submit" disabled={query.isFetching || movie.versions.length === 0} data-testid="search-run">
            <SearchIcon className={query.isFetching ? 'animate-pulse' : undefined} />
            {query.isFetching ? t('search.searching') : submitted !== null && submitted === text.trim() ? t('search.searchAgain') : t('search.search')}
          </Button>
        </form>
      </div>

      {submitted === null && <p className="text-sm text-muted-foreground">{t('search.hint')}</p>}

      {query.isFetching && !data && (
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

      {data && (
        <>
          {data.query && (
            <p className="text-sm" data-testid="search-text-note">
              {t('search.textResults', { query: data.query })}{' '}
              <Button
                type="button"
                variant="link"
                className="h-auto p-0"
                onClick={() => {
                  setText('')
                  run('')
                }}
              >
                {t('search.searchFilm')}
              </Button>
            </p>
          )}

          <p className="text-xs text-muted-foreground">
            {t('search.summary', { releases: data.releases.length, indexers: data.indexersQueried })}{' '}
            {acceptable.map(({ version, count }) => t('search.acceptableFor', { count, version: version.name })).join(' · ')}
          </p>

          {data.indexerErrors.length > 0 && (
            <Alert>
              <AlertDescription>
                <ul className="flex flex-col gap-1">
                  {data.indexerErrors.map((e) => (
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
            <div className="max-h-[32rem] overflow-y-auto overflow-x-auto rounded-lg border">
              <Table data-testid="search-results">
                <TableHeader className="sticky top-0 z-10 bg-background">
                  <TableRow>
                    <SortableHead column="age" sort={sort} onSort={onSort}>
                      {t('search.age')}
                    </SortableHead>
                    <SortableHead column="title" sort={sort} onSort={onSort}>
                      {t('search.release')}
                    </SortableHead>
                    <SortableHead column="indexer" sort={sort} onSort={onSort}>
                      {t('search.indexer')}
                    </SortableHead>
                    <SortableHead column="size" sort={sort} onSort={onSort}>
                      {t('search.size')}
                    </SortableHead>
                    <SortableHead column="peers" sort={sort} onSort={onSort}>
                      {t('search.peers')}
                    </SortableHead>
                    <SortableHead column="fit" sort={sort} onSort={onSort} title={t('search.fitSort')}>
                      {t('search.fit')}
                    </SortableHead>
                    <TableHead className="text-right">{t('search.grab')}</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {releases.map((release) => {
                    const target = versionById.get(release.suggestedVersionId ?? -1) ?? defaultVersion
                    return (
                      <ReleaseRow
                        key={`${release.indexerId}:${release.guid}`}
                        release={release}
                        versions={movie.versions}
                        target={target}
                        grabbing={grab.isPending && grab.variables?.release.guid === release.guid && grab.variables.release.indexerId === release.indexerId}
                        onGrab={(versionId) => grab.mutate({ release, versionId })}
                        onGrabOneShot={() => setOneShot({ release, profileId: target?.qualityProfileId ?? defaultVersion?.qualityProfileId ?? 0 })}
                      />
                    )
                  })}
                </TableBody>
              </Table>
            </div>
          )}

          <p className="text-xs text-muted-foreground">{t('search.grabHint')}</p>
        </>
      )}

      {oneShot && <OneShotGrabDialog movie={movie} release={oneShot.release} defaultProfileId={oneShot.profileId} onClose={() => setOneShot(null)} />}
    </section>
  )
}

function SortableHead({ column, sort, onSort, title, children }: { column: SortKey; sort: Sort; onSort: (key: SortKey) => void; title?: string; children: ReactNode }) {
  const active = sort.key === column
  return (
    <TableHead aria-sort={active ? (sort.direction === 'asc' ? 'ascending' : 'descending') : 'none'}>
      <button type="button" className="flex items-center gap-1 text-left hover:text-foreground" onClick={() => onSort(column)} title={title} data-testid={`sort-${column}`}>
        {children}
        {active && (sort.direction === 'asc' ? <ArrowUpIcon className="size-3" /> : <ArrowDownIcon className="size-3" />)}
      </button>
    </TableHead>
  )
}

/** ✓ accepted, clock when only temporary reasons stand in the way, ✗ otherwise. */
function VerdictIcon({ decision }: { decision: VersionDecision | undefined }) {
  if (!decision) return <span className="size-3.5" />
  if (decision.approved) return <CheckIcon className="size-3.5 text-emerald-600 dark:text-emerald-400" aria-hidden="true" />
  if (decision.rejections.every((r) => r.temporary)) return <ClockIcon className="size-3.5 text-amber-600 dark:text-amber-400" aria-hidden="true" />
  return <XIcon className="size-3.5 text-red-600 dark:text-red-400" aria-hidden="true" />
}

function ReleaseRow({
  release,
  versions,
  target,
  grabbing,
  onGrab,
  onGrabOneShot,
}: {
  release: Release
  versions: Version[]
  /** Where the grab button sends the release: the version that accepts it, or the closest. */
  target: Version | undefined
  grabbing: boolean
  onGrab: (versionId: number) => void
  /** Creates a one-shot version for this release and grabs it there (blueprint §3.3). */
  onGrabOneShot: () => void
}) {
  const { t } = useTranslation()
  const good = fits(release)
  const grabbable = release.protocol === 'torrent' && (!!release.downloadUrl || !!release.magnetUrl || !!release.infoHash)
  const flags = release.flags === 'none' ? [] : release.flags.split(', ')
  const details = [
    release.edition,
    release.releaseGroup,
    release.languages.length > 0 ? release.languages.join(', ') : null,
    release.isRepack ? t('search.repack') : null,
  ].filter((d): d is string => !!d)
  const decisionFor = (versionId: number) => release.versions.find((v) => v.versionId === versionId)
  const targetDecision = target ? decisionFor(target.id) : undefined
  const targetBusy = target?.status === 'downloading'
  const accepted = release.matchesMovie && !!targetDecision?.approved

  const grabTip = !grabbable
    ? t('search.grabNoLink')
    : targetBusy
      ? t('search.grabBlockedFor', { version: target?.name ?? '' })
      : !release.matchesMovie
        ? t('search.grabMismatch', { version: target?.name ?? '' })
        : accepted
          ? t('search.grabFor', { version: target?.name ?? '' })
          : t('search.grabAnyway', { version: target?.name ?? '' })

  return (
    <TableRow className={cn(!good && 'text-muted-foreground')} data-fits={good}>
      <TableCell className="align-top whitespace-nowrap">
        <Tooltip>
          <TooltipTrigger asChild>
            <span>{formatAge(release.publishDate)}</span>
          </TooltipTrigger>
          <TooltipContent>{formatDateTime(release.publishDate)}</TooltipContent>
        </Tooltip>
      </TableCell>
      <TableCell className="min-w-[24rem] align-top whitespace-normal">
        <span className={cn('flex items-start gap-1 break-all', good && 'font-medium text-foreground')}>
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
      <TableCell className="min-w-[18rem] align-top whitespace-normal" data-testid="release-fit">
        {!release.matchesMovie && (
          <span className="mb-1 flex flex-wrap gap-1">
            {release.rejections.map((r) => (
              <Badge key={r.reason} variant="outline" className={cn('whitespace-normal text-left', permanentStyle)}>
                {t('search.notThisFilm', { reason: r.reason })}
              </Badge>
            ))}
          </span>
        )}
        <ul className="flex flex-col gap-1">
          {release.versions.map((v) => (
            <li key={v.versionId} className="flex items-start gap-1">
              <span className="flex h-5 shrink-0 items-center gap-1">
                <VerdictIcon decision={v} />
                <span className={cn('text-xs font-medium whitespace-nowrap', v.approved && release.matchesMovie && 'text-foreground')}>
                  {v.versionName}
                  {v.customFormatScore !== 0 && <span className="font-normal text-muted-foreground"> · {t('search.score', { score: v.customFormatScore })}</span>}
                </span>
              </span>
              <span className="flex flex-wrap gap-1">
                {v.approved ? (
                  <Badge variant="outline" className={approvedStyle}>
                    {t('search.approved')}
                  </Badge>
                ) : (
                  v.rejections.map((r) => (
                    <Badge key={r.reason} variant="outline" className={cn('whitespace-normal text-left', r.temporary ? temporaryStyle : permanentStyle)}>
                      {r.reason}
                    </Badge>
                  ))
                )}
              </span>
            </li>
          ))}
        </ul>
      </TableCell>
      <TableCell className="align-top">
        <div className="flex justify-end">
          <Tooltip>
            <TooltipTrigger asChild>
              <span>
                <Button
                  type="button"
                  variant={accepted ? 'default' : 'outline'}
                  size="sm"
                  className="max-w-40 rounded-r-none"
                  onClick={() => target && onGrab(target.id)}
                  disabled={grabbing || targetBusy || !grabbable || !target}
                  aria-label={grabTip}
                  data-testid="grab"
                >
                  <DownloadIcon className={grabbing ? 'animate-pulse' : undefined} />
                  <span className="truncate">{target?.name}</span>
                </Button>
              </span>
            </TooltipTrigger>
            <TooltipContent>{grabTip}</TooltipContent>
          </Tooltip>
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button
                type="button"
                variant={accepted ? 'default' : 'outline'}
                size="sm"
                className="rounded-l-none border-l-0 px-1.5"
                disabled={grabbing || !grabbable}
                aria-label={t('search.grabTo')}
                data-testid="grab-menu"
              >
                <ChevronDownIcon />
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end" className="min-w-64">
              <DropdownMenuLabel>{t('search.grabTo')}</DropdownMenuLabel>
              {versions.map((v) => {
                const decision = decisionFor(v.id)
                const busy = v.status === 'downloading'
                return (
                  <DropdownMenuItem key={v.id} disabled={busy} onSelect={() => onGrab(v.id)} data-testid="grab-version">
                    <VerdictIcon decision={decision} />
                    <span className={cn(v.id === target?.id && 'font-medium')}>{v.name}</span>
                    <span className="ml-auto pl-4 text-xs text-muted-foreground">
                      {busy
                        ? t('search.activeDownload')
                        : [v.isDefault ? t('search.defaultVersion') : null, v.mode === 'oneShot' ? t('versions.oneShot').toLowerCase() : null, v.id === target?.id ? t('search.suggested') : null]
                            .filter(Boolean)
                            .join(' · ')}
                    </span>
                  </DropdownMenuItem>
                )
              })}
              <DropdownMenuSeparator />
              <DropdownMenuItem onSelect={onGrabOneShot} data-testid="grab-oneshot">
                <CopyPlusIcon />
                {t('search.grabOneShot')}
              </DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>
        </div>
      </TableCell>
    </TableRow>
  )
}
