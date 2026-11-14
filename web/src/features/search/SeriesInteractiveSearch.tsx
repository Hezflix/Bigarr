import { useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { cn } from 'cn'
import { CheckIcon, SearchIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ApiError, queue, search, series as seriesApi, type Series, type SeriesRelease, type SeriesVersion } from '@/api/client'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { DropdownMenuItem } from '@/components/ui/dropdown-menu'
import { Input } from '@/components/ui/input'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { grabObjections, languagesComparator, qualityComparator } from '@/features/search/grabRules'
import { FitCell } from '@/features/search/ReasonsDisplay'
import { AdvancedToggle, SearchQueriesPanel, useAdvancedSearch } from '@/features/search/SearchQueriesPanel'
import {
  commonComparators,
  grabRequest,
  GrabSplitButton,
  IndexerErrors,
  IndexerSizePeersCells,
  isGrabbable,
  QualityLanguagesFormatsCells,
  ReleaseTitleCell,
  SearchSkeleton,
  SortableHead as SharedSortableHead,
  sortReleases,
  temporaryStyle,
  useReleaseSort,
  VerdictIcon,
} from '@/features/search/releaseTable'
import { versionFits } from '@/features/search/rejectionReasons'
import { formatAge } from '@/i18n'

import { type PendingSeriesGrab, SeriesGrabDialog } from './SeriesGrabDialog'
import { pickableEpisodes, prefillEpisodeIds, type SeriesSearchScope } from './seriesGrabScope'

export type { SeriesSearchScope }

type SortKey = keyof typeof commonComparators | 'episodes' | 'quality' | 'languages' | 'score'

const SortableHead = (props: Omit<Parameters<typeof SharedSortableHead<SortKey>>[0], 'testIdPrefix'>) => <SharedSortableHead testIdPrefix="series-sort" {...props} />

const fitsAny = (release: SeriesRelease) => release.matchesSeries && release.episodes.length > 0 && release.versions.some((v) => v.approved)

/**
 * Interactive search for a series (Sonarr's manual search): an episode, a season (packs and single episodes together)
 * or the whole series, every release mapped to episodes and judged by every version, rejections spelled out. Every grab
 * asks for its episodes, started from Bigarr's match (or the search scope when there is none): the scope decides what is
 * listed, never what a release goes to. In shadow mode the grab is recorded as a pick.
 */
export function SeriesSearchDialog({ series, scope, onClose }: { series: Series; scope: SeriesSearchScope; onClose: () => void }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [submitted, setSubmitted] = useState('')
  const [text, setText] = useState('')
  const [sort, onSort] = useReleaseSort<SortKey>()
  const [pending, setPending] = useState<PendingSeriesGrab | null>(null)
  // The dialog stays open after a grab: each grabbed row says so, the others can be grabbed too.
  const [grabbed, setGrabbed] = useState<Map<string, { version: string; shadow: boolean }>>(new Map())
  const episodes = useQuery({ queryKey: ['seriesEpisodes', series.id], queryFn: () => seriesApi.episodes(series.id) })
  const [advanced, setAdvanced] = useAdvancedSearch()
  const scopeKey = scope.kind === 'episode' ? `e${scope.episodeId}` : scope.kind === 'season' ? `s${scope.season}${scope.orderSeason ? 'o' : ''}` : 'all'
  const query = useQuery({
    queryKey: ['seriesSearch', series.id, scopeKey, submitted],
    queryFn: () =>
      search.series(series.id, scope.kind === 'episode' ? { episodeId: scope.episodeId } : scope.kind === 'season' ? { season: scope.season, orderSeason: scope.orderSeason } : {}, { query: submitted || undefined, includeQueries: advanced }),
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
    mutationFn: ({ release, versionId, episodeIds }: { release: SeriesRelease; versionId: number; episodeIds: number[] }) =>
      queue.grab({ ...grabRequest(release, versionId), episodeIds, numbering: release.numbering }),
    onSuccess: async (result, { release, versionId }) => {
      const version = versionById.get(versionId)?.name ?? ''
      setGrabbed((current) => new Map(current).set(releaseKey(release), { version, shadow: result.shadow }))
      if (result.shadow) toast.success(t('search.picked', { title: release.title, version }))
      else toast.success(t('search.grabbed', { title: release.title, version, client: result.downloadClient ?? '' }))
      await queryClient.invalidateQueries({ queryKey: ['seriesEpisodes', series.id] })
      await queryClient.invalidateQueries({ queryKey: ['seriesDetail', series.id] })
      await queryClient.invalidateQueries({ queryKey: ['queue'] })
      await query.refetch()
    },
    onError: (e) => toast.error(e instanceof ApiError ? e.message : t('search.grabFailed')),
  })

  // Every grab asks which episodes it is for, at every search level: prefilled with Bigarr's match, else with the
  // scope's episodes (none for the whole series). The grab sends exactly the episodes ticked. A release the version
  // rejects (or that names another show) lists the reasons in the same dialog.
  const requestGrab = (release: SeriesRelease, versionId: number) => {
    const mapped = release.episodes.length > 0
    const pickable = pickableEpisodes(episodes.data ?? [], versionId)
    const decision = release.versions.find((v) => v.versionId === versionId)
    setPending({
      title: release.title,
      version: versionById.get(versionId)?.name ?? '',
      reasons: grabObjections(release.matchesSeries && mapped, release.rejections, decision, t('search.noVerdict')),
      episodes: pickable,
      prefill: prefillEpisodeIds(release, scope, pickable),
      matched: mapped ? (release.episodesLabel ?? null) : null,
      go: (episodeIds) => grab.mutate({ release, versionId, episodeIds }),
    })
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
  const fitsOf = (release: SeriesRelease) => versionFits(release.versions, release.rejections.map((r) => r.reason), t('search.reasons.release'))
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
            <AdvancedToggle checked={advanced} onChange={setAdvanced} testId="series-search-advanced" />
          </form>
        </search>

        {query.isFetching && !data && <SearchSkeleton />}

        {query.error && (
          <Alert variant="destructive">
            <AlertDescription>{query.error instanceof ApiError ? query.error.message : t('search.failed')}</AlertDescription>
          </Alert>
        )}

        {data && <IndexerErrors errors={data.indexerErrors} />}

        {data && advanced && <SearchQueriesPanel queries={data.queries} />}

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
                      fits={fitsOf(release)}
                      versions={series.versions}
                      target={targetOf(release)}
                      grabbing={grab.isPending && grab.variables?.release.guid === release.guid && grab.variables.release.indexerId === release.indexerId}
                      grabbed={grabbed.get(releaseKey(release))}
                      episodesLoaded={episodes.isSuccess}
                      onGrab={(versionId) => requestGrab(release, versionId)}
                    />
                  ))}
                </TableBody>
              </Table>
            </div>
          ))}
        <SeriesGrabDialog pending={pending} onClose={() => setPending(null)} />
      </DialogContent>
    </Dialog>
  )
}

function ReleaseRow({
  release,
  fits,
  versions,
  target,
  grabbing,
  grabbed,
  episodesLoaded,
  onGrab,
}: {
  release: SeriesRelease
  fits: ReturnType<typeof versionFits>
  versions: SeriesVersion[]
  target: SeriesVersion | undefined
  grabbing: boolean
  /** Grabbed (or picked, in shadow mode) from this dialog. */
  grabbed: { version: string; shadow: boolean } | undefined
  /** The series' episodes are there to pick from. */
  episodesLoaded: boolean
  onGrab: (versionId: number) => void
}) {
  const { t } = useTranslation()
  const good = fitsAny(release)
  const mapped = release.episodes.length > 0
  const grabbable = episodesLoaded && isGrabbable(release) && !grabbed
  const details = [release.releaseGroup, release.isRepack ? t('search.repack') : null].filter((d): d is string => !!d)
  const decisionFor = (versionId: number) => release.versions.find((v) => v.versionId === versionId)
  const targetDecision = target ? decisionFor(target.id) : undefined
  const accepted = release.matchesSeries && !!targetDecision?.approved
  const grabTip = grabbed
    ? t(grabbed.shadow ? 'seriesSearch.pickedFor' : 'seriesSearch.grabbedFor', { version: grabbed.version })
    : !isGrabbable(release)
      ? t('search.grabNoLink')
      : !mapped
        ? t('seriesSearch.grabUnmapped', { version: target?.name ?? '' })
        : accepted
          ? t('seriesSearch.grabFor', { version: target?.name ?? '', episodes: release.episodesLabel ?? '' })
          : t('search.grabAnyway', { version: target?.name ?? '' })

  return (
    <TableRow className={cn(!good && !grabbed && 'text-muted-foreground', grabbed && 'bg-emerald-500/5')} data-fits={good} data-grabbed={!!grabbed} data-testid="series-release">
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
        {grabbed && (
          <Badge variant="outline" className="mb-1 border-emerald-600/30 bg-emerald-500/15 text-emerald-700 dark:border-emerald-400/30 dark:text-emerald-300" data-testid="release-grabbed">
            <CheckIcon />
            {t(grabbed.shadow ? 'seriesSearch.pickedFor' : 'seriesSearch.grabbedFor', { version: grabbed.version })}
          </Badge>
        )}
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
      <FitCell fits={fits} matches={release.matchesSeries} testId="series-release-fit" />
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

const releaseKey = (release: SeriesRelease) => `${release.indexerId}:${release.guid}`
