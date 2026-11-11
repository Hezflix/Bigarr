import { useState, type FormEvent, type ReactNode } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { cn } from 'cn'
import { ArrowDownIcon, ArrowUpIcon, CheckIcon, ChevronDownIcon, ClockIcon, DownloadIcon, ExternalLinkIcon, SearchIcon, XIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ApiError, queue, search, type Series, type SeriesRelease, type SeriesVersion, type SeriesVersionDecision } from '@/api/client'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuLabel, DropdownMenuTrigger } from '@/components/ui/dropdown-menu'
import { Input } from '@/components/ui/input'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { formatAge, formatBytes } from '@/i18n'

const approvedStyle = 'border-emerald-600/30 bg-emerald-500/15 text-emerald-700 dark:border-emerald-400/30 dark:text-emerald-300'
const temporaryStyle = 'border-amber-600/30 bg-amber-500/15 text-amber-700 dark:border-amber-400/30 dark:text-amber-300'
const permanentStyle = 'border-red-600/30 bg-red-500/10 text-red-700 dark:border-red-400/30 dark:text-red-300'

/** What is searched: an episode, a season, or the whole series. */
export type SeriesSearchScope = { kind: 'episode'; episodeId: number; label: string } | { kind: 'season'; season: number; label: string; orderSeason?: boolean } | { kind: 'series'; label: string }

/** "fit" is the server's ranking (best first); the others toggle, like the film search. */
type SortKey = 'fit' | 'age' | 'title' | 'episodes' | 'indexer' | 'size' | 'peers'
type Sort = { key: SortKey; direction: 'asc' | 'desc' }

const collator = new Intl.Collator(undefined, { sensitivity: 'base', numeric: true })

const comparators: Record<Exclude<SortKey, 'fit'>, (a: SeriesRelease, b: SeriesRelease) => number> = {
  age: (a, b) => Date.parse(b.publishDate) - Date.parse(a.publishDate),
  title: (a, b) => collator.compare(a.title, b.title),
  episodes: (a, b) => a.episodes.length - b.episodes.length,
  indexer: (a, b) => collator.compare(a.indexer, b.indexer),
  size: (a, b) => a.size - b.size,
  peers: (a, b) => (a.seeders ?? -1) - (b.seeders ?? -1),
}

function sorted(releases: SeriesRelease[], sort: Sort) {
  if (sort.key === 'fit') return releases
  const compare = comparators[sort.key]
  const sign = sort.direction === 'asc' ? 1 : -1
  return [...releases].sort((a, b) => sign * compare(a, b))
}

const fits = (release: SeriesRelease) => release.matchesSeries && release.episodes.length > 0 && release.versions.some((v) => v.approved)

/**
 * Interactive search for a series (Sonarr's manual search): an episode, a season (packs and single episodes together)
 * or the whole series, every release mapped to episodes and judged by every version, rejections spelled out. The grab
 * sends the release with the episodes it covers; in shadow mode it is recorded as a pick.
 */
export function SeriesSearchDialog({ series, scope, onClose }: { series: Series; scope: SeriesSearchScope; onClose: () => void }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [submitted, setSubmitted] = useState('')
  const [text, setText] = useState('')
  const [sort, setSort] = useState<Sort>({ key: 'fit', direction: 'asc' })
  const scopeKey = scope.kind === 'episode' ? `e${scope.episodeId}` : scope.kind === 'season' ? `s${scope.season}${scope.orderSeason ? 'o' : ''}` : 'all'
  const query = useQuery({
    queryKey: ['seriesSearch', series.id, scopeKey, submitted],
    queryFn: () =>
      search.series(series.id, scope.kind === 'episode' ? { episodeId: scope.episodeId } : scope.kind === 'season' ? { season: scope.season, orderSeason: scope.orderSeason } : {}, { query: submitted || undefined }),
    staleTime: Number.POSITIVE_INFINITY,
    retry: false,
  })

  const versionById = new Map(series.versions.map((v) => [v.id, v]))
  const defaultVersion = series.versions.find((v) => v.isDefault) ?? series.versions[0]

  const grab = useMutation({
    mutationFn: ({ release, versionId }: { release: SeriesRelease; versionId: number }) =>
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
        episodeIds: release.episodes.map((e) => e.id),
        numbering: release.numbering,
      }),
    onSuccess: async (result, { release, versionId }) => {
      const version = versionById.get(versionId)?.name ?? ''
      if (result.shadow) toast.success(t('search.picked', { title: release.title, version }))
      else toast.success(t('search.grabbed', { title: release.title, version, client: result.downloadClient ?? '' }))
      await query.refetch()
    },
    onError: (e) => toast.error(e instanceof ApiError ? e.message : t('search.grabFailed')),
  })

  const run = (next: string) => {
    if (next === submitted) {
      void query.refetch()
      return
    }

    queryClient.removeQueries({ queryKey: ['seriesSearch', series.id, scopeKey, next], exact: true })
    setSubmitted(next)
  }
  const submit = (e: FormEvent) => {
    e.preventDefault()
    run(text.trim())
  }
  const onSort = (key: SortKey) =>
    setSort((current) => (key === 'fit' ? { key, direction: 'asc' } : current.key === key ? { key, direction: current.direction === 'asc' ? 'desc' : 'asc' } : { key, direction: 'asc' }))

  const data = query.data
  const releases = data ? sorted(data.releases, sort) : []
  const acceptable = series.versions.map((v) => ({
    version: v,
    count: data?.releases.filter((r) => r.matchesSeries && r.episodes.length > 0 && r.versions.some((d) => d.versionId === v.id && d.approved)).length ?? 0,
  }))

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="flex max-h-[90vh] flex-col sm:max-w-[min(96vw,90rem)]" data-testid="series-search">
        <DialogHeader>
          <DialogTitle>{t('seriesSearch.title', { what: scope.label, series: series.title })}</DialogTitle>
          <DialogDescription>{t(`seriesSearch.hint.${scope.kind}`)}</DialogDescription>
        </DialogHeader>

        <form onSubmit={submit} className="flex flex-wrap items-center gap-2" role="search">
          <Input
            value={text}
            onChange={(e) => setText(e.target.value)}
            placeholder={t('seriesSearch.queryPlaceholder')}
            aria-label={t('search.queryLabel')}
            maxLength={200}
            className="w-72"
            data-testid="series-search-query"
          />
          <Button type="submit" disabled={query.isFetching} data-testid="series-search-run">
            <SearchIcon className={query.isFetching ? 'animate-pulse' : undefined} />
            {query.isFetching ? t('search.searching') : t('search.searchAgain')}
          </Button>
          {data && (
            <p className="text-xs text-muted-foreground">
              {t('search.summary', { releases: data.releases.length, indexers: data.indexersQueried })}{' '}
              {acceptable.map(({ version, count }) => t('search.acceptableFor', { count, version: version.name })).join(' · ')}
            </p>
          )}
        </form>

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

        {data && data.indexerErrors.length > 0 && (
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

        {data && data.query && <p className="text-sm">{t('seriesSearch.textResults', { query: data.query })}</p>}
        {data?.orderFallback && (
          <p className="text-sm text-muted-foreground" data-testid="series-search-order-fallback">
            {t('seriesSearch.orderFallback')}
          </p>
        )}

        {data &&
          (releases.length === 0 ? (
            <p className="text-sm text-muted-foreground">{t('search.noResults')}</p>
          ) : (
            <div className="min-h-0 flex-1 overflow-auto rounded-lg border">
              <Table data-testid="series-search-results">
                <TableHeader className="sticky top-0 z-10 bg-background">
                  <TableRow>
                    <SortableHead column="age" sort={sort} onSort={onSort}>
                      {t('search.age')}
                    </SortableHead>
                    <SortableHead column="title" sort={sort} onSort={onSort}>
                      {t('search.release')}
                    </SortableHead>
                    <SortableHead column="episodes" sort={sort} onSort={onSort}>
                      {t('seriesSearch.episodes')}
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
                  {releases.map((release) => (
                    <ReleaseRow
                      key={`${release.indexerId}:${release.guid}`}
                      release={release}
                      versions={series.versions}
                      target={versionById.get(release.suggestedVersionId ?? -1) ?? defaultVersion}
                      grabbing={grab.isPending && grab.variables?.release.guid === release.guid && grab.variables.release.indexerId === release.indexerId}
                      onGrab={(versionId) => grab.mutate({ release, versionId })}
                    />
                  ))}
                </TableBody>
              </Table>
            </div>
          ))}
      </DialogContent>
    </Dialog>
  )
}

function SortableHead({ column, sort, onSort, title, children }: { column: SortKey; sort: Sort; onSort: (key: SortKey) => void; title?: string; children: ReactNode }) {
  const active = sort.key === column
  return (
    <TableHead aria-sort={active ? (sort.direction === 'asc' ? 'ascending' : 'descending') : 'none'}>
      <button type="button" className="flex items-center gap-1 text-left hover:text-foreground" onClick={() => onSort(column)} title={title} data-testid={`series-sort-${column}`}>
        {children}
        {active && (sort.direction === 'asc' ? <ArrowUpIcon className="size-3" /> : <ArrowDownIcon className="size-3" />)}
      </button>
    </TableHead>
  )
}

function VerdictIcon({ decision }: { decision: SeriesVersionDecision | undefined }) {
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
}: {
  release: SeriesRelease
  versions: SeriesVersion[]
  target: SeriesVersion | undefined
  grabbing: boolean
  onGrab: (versionId: number) => void
}) {
  const { t } = useTranslation()
  const good = fits(release)
  const mapped = release.episodes.length > 0
  const grabbable = mapped && release.protocol === 'torrent' && (!!release.downloadUrl || !!release.magnetUrl || !!release.infoHash)
  const flags = release.flags === 'none' ? [] : release.flags.split(', ')
  const details = [release.releaseGroup, release.languages.length > 0 ? release.languages.join(', ') : null, release.isRepack ? t('search.repack') : null].filter(
    (d): d is string => !!d,
  )
  const decisionFor = (versionId: number) => release.versions.find((v) => v.versionId === versionId)
  const accepted = release.matchesSeries && !!(target && decisionFor(target.id)?.approved)
  const grabTip = !mapped
    ? t('seriesSearch.grabUnmapped')
    : !grabbable
      ? t('search.grabNoLink')
      : accepted
        ? t('seriesSearch.grabFor', { version: target?.name ?? '', episodes: release.episodesLabel ?? '' })
        : t('search.grabAnyway', { version: target?.name ?? '' })

  return (
    <TableRow className={cn(!good && 'text-muted-foreground')} data-fits={good} data-testid="series-release">
      <TableCell className="align-top whitespace-nowrap">
        <Tooltip>
          <TooltipTrigger asChild>
            <span>{formatAge(release.publishDate)}</span>
          </TooltipTrigger>
          <TooltipContent>{new Date(release.publishDate).toLocaleString()}</TooltipContent>
        </Tooltip>
      </TableCell>
      <TableCell className="min-w-[22rem] align-top whitespace-normal">
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
      <TableCell className="align-top whitespace-nowrap" data-testid="release-episodes">
        {mapped ? (
          <span className="flex flex-col items-start gap-1">
            {release.fullSeason && <Badge variant="outline">{t('seriesSearch.pack')}</Badge>}
            <span className="text-xs tabular-nums">{release.episodesLabel}</span>
            {release.numbering && release.numbering !== 'tvdb' && (
              <Badge variant="outline" title={t(`seriesSearch.numberingHint.${release.numbering}`)} data-testid="release-numbering">
                {t(`seriesSearch.numbering.${release.numbering}`)}
              </Badge>
            )}
            {release.held && (
              <Badge variant="outline" className="border-amber-600/30 bg-amber-500/15 text-amber-700 dark:border-amber-400/30 dark:text-amber-300" data-testid="release-held">
                {t('seriesSearch.held')}
              </Badge>
            )}
          </span>
        ) : (
          <span className="text-xs">—</span>
        )}
      </TableCell>
      <TableCell className="align-top whitespace-nowrap">{release.indexer}</TableCell>
      <TableCell className="align-top whitespace-nowrap">{formatBytes(release.size)}</TableCell>
      <TableCell className="align-top whitespace-nowrap">
        {release.seeders ?? '—'} <span className="text-muted-foreground">/ {release.leechers ?? '—'}</span>
      </TableCell>
      <TableCell className="min-w-[18rem] align-top whitespace-normal" data-testid="series-release-fit">
        {release.rejections.length > 0 && (
          <span className="mb-1 flex flex-wrap gap-1">
            {release.rejections.map((r) => (
              <Badge key={r.reason} variant="outline" className={cn('whitespace-normal text-left', permanentStyle)}>
                {r.reason}
              </Badge>
            ))}
          </span>
        )}
        <ul className="flex flex-col gap-1">
          {release.versions.map((v) => (
            <li key={v.versionId} className="flex items-start gap-1">
              <span className="flex h-5 shrink-0 items-center gap-1">
                <VerdictIcon decision={v} />
                <span className={cn('text-xs font-medium whitespace-nowrap', v.approved && release.matchesSeries && 'text-foreground')}>
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
                  disabled={grabbing || !grabbable || !target}
                  aria-label={grabTip}
                  data-testid="series-grab"
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
              >
                <ChevronDownIcon />
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end" className="min-w-56">
              <DropdownMenuLabel>{t('search.grabTo')}</DropdownMenuLabel>
              {versions.map((v) => (
                <DropdownMenuItem key={v.id} onSelect={() => onGrab(v.id)}>
                  <VerdictIcon decision={decisionFor(v.id)} />
                  <span className={cn(v.id === target?.id && 'font-medium')}>{v.name}</span>
                  <span className="ml-auto pl-4 text-xs text-muted-foreground">
                    {[v.isDefault ? t('search.defaultVersion') : null, v.id === target?.id ? t('search.suggested') : null].filter(Boolean).join(' · ')}
                  </span>
                </DropdownMenuItem>
              ))}
            </DropdownMenuContent>
          </DropdownMenu>
        </div>
      </TableCell>
    </TableRow>
  )
}
