import { useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { cn } from 'cn'
import { SearchIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ApiError, queue, search, type Series, type SeriesRelease, type SeriesVersion } from '@/api/client'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { DropdownMenuItem } from '@/components/ui/dropdown-menu'
import { Input } from '@/components/ui/input'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { grabObjections, languagesComparator, qualityComparator } from '@/features/search/grabRules'
import {
  commonComparators,
  GrabConfirmDialog,
  grabRequest,
  GrabSplitButton,
  IndexerErrors,
  IndexerSizePeersCells,
  isGrabbable,
  type PendingGrab,
  permanentStyle,
  QualityLanguagesFormatsCells,
  ReleaseTitleCell,
  SearchSkeleton,
  SortableHead as SharedSortableHead,
  sortReleases,
  temporaryStyle,
  useReleaseSort,
  VerdictIcon,
  VersionVerdicts,
} from '@/features/search/releaseTable'
import { formatAge } from '@/i18n'

/** What is searched: an episode, a season, or the whole series. */
export type SeriesSearchScope = { kind: 'episode'; episodeId: number; label: string } | { kind: 'season'; season: number; label: string; orderSeason?: boolean } | { kind: 'series'; label: string }

type SortKey = keyof typeof commonComparators | 'episodes' | 'quality' | 'languages' | 'score'

const SortableHead = (props: Omit<Parameters<typeof SharedSortableHead<SortKey>>[0], 'testIdPrefix'>) => <SharedSortableHead testIdPrefix="series-sort" {...props} />

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
  const [sort, onSort] = useReleaseSort<SortKey>()
  const [pending, setPending] = useState<PendingGrab | null>(null)
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
  // Where the grab button sends a release, and so the score shown and sorted on: the suggested version, else the default.
  const targetOf = (release: SeriesRelease) => versionById.get(release.suggestedVersionId ?? -1) ?? defaultVersion
  const scoreOf = (release: SeriesRelease) => release.versions.find((v) => v.versionId === targetOf(release)?.id)?.customFormatScore ?? 0
  const comparators: Record<SortKey, (a: SeriesRelease, b: SeriesRelease) => number> = {
    ...commonComparators,
    episodes: (a, b) => a.episodes.length - b.episodes.length,
    quality: qualityComparator,
    languages: languagesComparator,
    score: (a, b) => scoreOf(a) - scoreOf(b),
  }

  const grab = useMutation({
    mutationFn: ({ release, versionId }: { release: SeriesRelease; versionId: number }) =>
      queue.grab({ ...grabRequest(release, versionId), episodeIds: release.episodes.map((e) => e.id), numbering: release.numbering }),
    onSuccess: async (result, { release, versionId }) => {
      const version = versionById.get(versionId)?.name ?? ''
      if (result.shadow) toast.success(t('search.picked', { title: release.title, version }))
      else toast.success(t('search.grabbed', { title: release.title, version, client: result.downloadClient ?? '' }))
      await queryClient.invalidateQueries({ queryKey: ['seriesEpisodes', series.id] })
      await queryClient.invalidateQueries({ queryKey: ['seriesDetail', series.id] })
      await queryClient.invalidateQueries({ queryKey: ['queue'] })
      await query.refetch()
    },
    onError: (e) => toast.error(e instanceof ApiError ? e.message : t('search.grabFailed')),
  })

  // A release the version rejects (or that names another show) is grabbed only after the reasons are confirmed.
  const requestGrab = (release: SeriesRelease, versionId: number) => {
    const go = () => grab.mutate({ release, versionId })
    const decision = release.versions.find((v) => v.versionId === versionId)
    const reasons = grabObjections(release.matchesSeries, release.rejections, decision, t('search.noVerdict'))
    if (reasons.length === 0) go()
    else setPending({ title: release.title, version: versionById.get(versionId)?.name ?? '', reasons, go })
  }

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

  const data = query.data
  const releases = data ? sortReleases(data.releases, sort, comparators) : []
  const acceptable = series.versions.map((v) => ({
    version: v,
    count: data?.releases.filter((r) => r.matchesSeries && r.episodes.length > 0 && r.versions.some((d) => d.versionId === v.id && d.approved)).length ?? 0,
  }))

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="flex max-h-[90vh] flex-col sm:max-w-[min(98vw,112rem)]" data-testid="series-search">
        <DialogHeader>
          <DialogTitle>{t('seriesSearch.title', { what: scope.label, series: series.title })}</DialogTitle>
          <DialogDescription>{t(`seriesSearch.hint.${scope.kind}`)}</DialogDescription>
        </DialogHeader>

        <search>
          <form onSubmit={submit} className="flex flex-wrap items-center gap-2">
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
        </search>

        {query.isFetching && !data && <SearchSkeleton />}

        {query.error && (
          <Alert variant="destructive">
            <AlertDescription>{query.error instanceof ApiError ? query.error.message : t('search.failed')}</AlertDescription>
          </Alert>
        )}

        {data && <IndexerErrors errors={data.indexerErrors} />}

        {data?.query && <p className="text-sm">{t('seriesSearch.textResults', { query: data.query })}</p>}
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
                    <SortableHead column="quality" sort={sort} onSort={onSort}>
                      {t('search.quality')}
                    </SortableHead>
                    <SortableHead column="languages" sort={sort} onSort={onSort}>
                      {t('search.languages')}
                    </SortableHead>
                    <SortableHead column="score" sort={sort} onSort={onSort} title={t('search.scoreSort')}>
                      {t('search.formats')}
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
                      target={targetOf(release)}
                      grabbing={grab.isPending && grab.variables?.release.guid === release.guid && grab.variables.release.indexerId === release.indexerId}
                      onGrab={(versionId) => requestGrab(release, versionId)}
                    />
                  ))}
                </TableBody>
              </Table>
            </div>
          ))}
        <GrabConfirmDialog pending={pending} onClose={() => setPending(null)} />
      </DialogContent>
    </Dialog>
  )
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
  const grabbable = mapped && isGrabbable(release)
  const details = [release.releaseGroup, release.isRepack ? t('search.repack') : null].filter((d): d is string => !!d)
  const decisionFor = (versionId: number) => release.versions.find((v) => v.versionId === versionId)
  const targetDecision = target ? decisionFor(target.id) : undefined
  const accepted = release.matchesSeries && !!targetDecision?.approved
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
      <ReleaseTitleCell release={release} good={good} details={details} className="min-w-[17rem]" badges={false} />
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
              <Badge variant="outline" className={temporaryStyle} data-testid="release-held">
                {t('seriesSearch.held')}
              </Badge>
            )}
          </span>
        ) : (
          <span className="text-xs">—</span>
        )}
      </TableCell>
      <IndexerSizePeersCells release={release} />
      <QualityLanguagesFormatsCells release={release} score={targetDecision?.customFormatScore} scoreVersion={target?.name} />
      <TableCell className="min-w-[13rem] align-top whitespace-normal" data-testid="series-release-fit">
        {release.rejections.length > 0 && (
          <span className="mb-1 flex flex-wrap gap-1">
            {release.rejections.map((r) => (
              <Badge key={r.reason} variant="outline" className={cn('h-auto rounded-md whitespace-normal text-left', permanentStyle)}>
                {r.reason}
              </Badge>
            ))}
          </span>
        )}
        <VersionVerdicts decisions={release.versions} matches={release.matchesSeries} />
      </TableCell>
      <TableCell className="align-top">
        <GrabSplitButton
          accepted={accepted}
          tip={grabTip}
          targetName={target?.name}
          grabbing={grabbing}
          disabled={grabbing || !grabbable || !target}
          menuDisabled={grabbing || !grabbable}
          onGrab={() => target && onGrab(target.id)}
          testId="series-grab"
          menuClassName="min-w-56"
        >
          {versions.map((v) => (
            <DropdownMenuItem key={v.id} onSelect={() => onGrab(v.id)}>
              <VerdictIcon decision={decisionFor(v.id)} />
              <span className={cn(v.id === target?.id && 'font-medium')}>{v.name}</span>
              <span className="ml-auto pl-4 text-xs text-muted-foreground">
                {[v.isDefault ? t('search.defaultVersion') : null, v.id === target?.id ? t('search.suggested') : null].filter(Boolean).join(' · ')}
              </span>
            </DropdownMenuItem>
          ))}
        </GrabSplitButton>
      </TableCell>
    </TableRow>
  )
}
