import { useEffect, useRef, useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { cn } from 'cn'
import { CircleCheckIcon, CircleIcon, CopyPlusIcon, PencilLineIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ApiError, queue, search, type Movie, type Release, type Version } from '@/api/client'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { DropdownMenuItem, DropdownMenuSeparator } from '@/components/ui/dropdown-menu'
import { Input } from '@/components/ui/input'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { OneShotGrabDialog } from '@/features/search/OneShotGrabDialog'
import { GrabOverrideDialog } from '@/features/search/GrabOverrideDialog'
import type { GrabOverride } from '@/features/search/grabOverride'
import { grabObjections } from '@/features/search/grabRules'
import { FitCell } from '@/features/search/ReasonsDisplay'
import { IndexerPicker, useIndexerPick } from '@/features/search/IndexerPicker'
import { InteractiveSearchIcon } from '@/features/search/SearchButtons'
import { ShowQueriesToggle, SearchQueriesPanel, useShowQueries } from '@/features/search/SearchQueriesPanel'
import {
  commonComparators,
  GrabConfirmDialog,
  grabRequest,
  GrabSplitButton,
  IndexerErrors,
  IndexerSizePeersCells,
  isGrabbable,
  type PendingGrab,
  ReleaseTitleCell,
  SearchSkeleton,
  SortableHead as SharedSortableHead,
  sortReleases,
  useReleaseSort,
  VerdictIcon,
} from '@/features/search/releaseTable'
import { versionFits } from '@/features/search/rejectionReasons'
import { isCurrentPick, PickBadge, pickedVariant, pickIcons, picksOf, pickStyles } from '@/features/search/releasePicks'
import { pickKey } from '@/features/search/searchIndexers'
import { formatAge, formatDateTime } from '@/i18n'

type SortKey = keyof typeof commonComparators

const releaseKey = (release: Release) => `${release.indexerId}:${release.guid}`

const SortableHead = (props: Omit<Parameters<typeof SharedSortableHead<SortKey>>[0], 'testIdPrefix'>) => <SharedSortableHead testIdPrefix="sort" {...props} />

/** The film is acceptable for at least one version, and the release is the film. */
function fitsAny(release: Release) {
  return release.matchesMovie && release.versions.some((v) => v.approved)
}

/** A search as run: its text ('' is the film's own search) and the indexers picked (undefined: every one). */
type Submitted = { text: string; ids: number[] | undefined }

/** The last search shown for this film, so a remount (navigating away and back) shows it again with its text and indexers. */
function cachedSearch(queryClient: ReturnType<typeof useQueryClient>, movieId: number): Submitted | null {
  const latest = queryClient
    .getQueryCache()
    .findAll({ queryKey: ['search', movieId] })
    .filter((q) => q.state.data !== undefined)
    .sort((a, b) => b.state.dataUpdatedAt - a.state.dataUpdatedAt)[0]
  if (!latest) return null
  const ids = String(latest.queryKey[3] ?? 'all')
  return { text: String(latest.queryKey[2] ?? ''), ids: ids === 'all' ? undefined : ids.split(',').filter(Boolean).map(Number) }
}

/**
 * Interactive search for one film: every indexer once, every release judged by every version. The
 * version is chosen when grabbing; each release suggests the one it fits. A typed text replaces the film's ids and
 * titles for films listed under another name; the releases are still judged against this film's versions.
 */
export function InteractiveSearch({ movie, runRequest = 0 }: { movie: Movie; /** Bumped from the page's header button: search now and scroll here. */ runRequest?: number }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  // null: nothing searched yet. Text '' is the film's own search (ids and titles), anything else the user's text.
  const [submitted, setSubmitted] = useState<Submitted | null>(() => cachedSearch(queryClient, movie.id))
  const [text, setText] = useState(submitted?.text ?? '')
  const indexerPick = useIndexerPick()
  const [sort, onSort] = useReleaseSort<SortKey>()
  const [oneShot, setOneShot] = useState<{ release: Release; profileId: number } | null>(null)
  const [pending, setPending] = useState<PendingGrab | null>(null)
  const [overriding, setOverriding] = useState<{ release: Release; versionId: number | undefined } | null>(null)
  const [showQueries, setShowQueries] = useShowQueries()
  const variant = pickedVariant()
  // The row just grabbed (or asked for from the picks banner): scrolled to and lit for a moment.
  const [flash, setFlash] = useState<string | null>(null)
  useEffect(() => {
    if (flash === null) return
    section.current?.querySelector(`[data-release-key="${CSS.escape(flash)}"]`)?.scrollIntoView({ behavior: 'smooth', block: 'nearest' })
    const timer = setTimeout(() => setFlash(null), 2400)
    return () => clearTimeout(timer)
  }, [flash])
  const query = useQuery({
    queryKey: ['search', movie.id, submitted?.text ?? '', pickKey(submitted?.ids)],
    queryFn: () => search.movie(movie.id, { query: submitted?.text || undefined, includeQueries: showQueries, indexerIds: submitted?.ids }),
    enabled: submitted !== null,
    staleTime: Number.POSITIVE_INFINITY,
    retry: false,
  })

  // Every click searches again: grabs and imports since the last results change the verdicts. The indexers picked
  // apply from the next click on.
  const isSubmitted = (nextText: string) => submitted !== null && submitted.text === nextText && pickKey(submitted.ids) === pickKey(indexerPick.ids)
  const run = (nextText: string) => {
    if (isSubmitted(nextText)) {
      void query.refetch()
      return
    }
    queryClient.removeQueries({ queryKey: ['search', movie.id, nextText, pickKey(indexerPick.ids)], exact: true })
    setSubmitted({ text: nextText, ids: indexerPick.ids })
  }
  // A new pick of indexers, once it has settled, searches again with the text last searched: no click needed.
  // biome-ignore lint/correctness/useExhaustiveDependencies: only a settled pick runs it
  useEffect(() => {
    if (indexerPick.settledKey === null || submitted === null || indexerPick.none || indexerPick.settledKey === pickKey(submitted.ids)) return
    run(submitted.text)
  }, [indexerPick.settledKey])
  const section = useRef<HTMLElement>(null)
  const lastRun = useRef(runRequest)
  // biome-ignore lint/correctness/useExhaustiveDependencies: only a new request from the header runs it, with the text as it is then
  useEffect(() => {
    if (runRequest === lastRun.current) return
    lastRun.current = runRequest
    section.current?.scrollIntoView({ behavior: 'smooth', block: 'start' })
    scrollOnResults.current = true
    run(text.trim())
  }, [runRequest])
  // The page is often too short to bring the section to the top before the results arrive: scroll again once they do.
  const scrollOnResults = useRef(false)
  useEffect(() => {
    if (query.isFetching || !scrollOnResults.current) return
    scrollOnResults.current = false
    section.current?.scrollIntoView({ behavior: 'smooth', block: 'start' })
  }, [query.isFetching])
  const submit = (e: FormEvent) => {
    e.preventDefault()
    run(text.trim())
  }

  const versionById = new Map(movie.versions.map((v) => [v.id, v]))
  const defaultVersion = movie.versions.find((v) => v.isDefault) ?? movie.versions[0]

  const grab = useMutation({
    mutationFn: ({ release, versionId, override }: { release: Release; versionId: number; override?: GrabOverride }) =>
      queue.grab({ ...grabRequest(release, versionId), ...override }),
    onSuccess: async (result, { release, versionId }) => {
      const version = versionById.get(versionId)?.name ?? ''
      // Shadow mode: the release is recorded as Bigarr's pick, nothing reaches a client.
      if (result.shadow) toast.success(t('search.picked', { title: release.title, version }))
      else toast.success(t('search.grabbed', { title: release.title, version, client: result.downloadClient ?? '' }))
      await queryClient.invalidateQueries({ queryKey: ['movie', movie.id] })
      await queryClient.invalidateQueries({ queryKey: ['movies'] })
      await queryClient.invalidateQueries({ queryKey: ['queue'] })
      await queryClient.invalidateQueries({ queryKey: ['history'] })
      await query.refetch()
      setFlash(releaseKey(release))
    },
    onError: (e) => toast.error(e instanceof ApiError ? e.message : t('search.grabFailed')),
  })

  // A release the version rejects (or that is not this film) is grabbed only after the reasons are confirmed.
  const requestGrab = (release: Release, versionId: number) => {
    const go = () => grab.mutate({ release, versionId })
    const decision = release.versions.find((v) => v.versionId === versionId)
    const reasons = grabObjections(release.matchesMovie, release.rejections, decision, t('search.noVerdict'))
    if (reasons.length === 0) go()
    else setPending({ title: release.title, version: versionById.get(versionId)?.name ?? '', reasons, go })
  }

  const data = query.data
  const sorted = data ? sortReleases(data.releases, sort, commonComparators) : []
  // Proposal 3: the picked releases stay on top, whatever the sort.
  const pinned = variant === 3 ? sorted.filter((r) => (r.picks ?? []).length > 0) : []
  const releases = variant === 3 ? sorted.filter((r) => (r.picks ?? []).length === 0) : sorted
  const picked = sorted.filter((r) => (r.picks ?? []).length > 0)
  const fitsOf = (release: Release) => versionFits(release.versions, release.matchesMovie ? [] : release.rejections.map((r) => t('search.notThisFilm', { reason: r.reason })), t('search.reasons.release'))
  const acceptable = movie.versions.map((v) => ({
    version: v,
    count: data?.releases.filter((r) => r.matchesMovie && r.versions.some((d) => d.versionId === v.id && d.approved)).length ?? 0,
  }))

  return (
    <section ref={section} className="flex scroll-mt-4 flex-col gap-3" data-testid="interactive-search">
      <div className="flex flex-wrap items-center gap-3">
        <h2 className="text-xl font-semibold tracking-tight">{t('search.title')}</h2>
        <search className="ml-auto">
          <form onSubmit={submit} className="flex flex-wrap items-center gap-2">
            <Input
              value={text}
              onChange={(e) => setText(e.target.value)}
              placeholder={t('search.queryPlaceholder')}
              aria-label={t('search.queryLabel')}
              maxLength={200}
              className="w-72"
              data-testid="search-query"
            />
            <IndexerPicker pick={indexerPick} testId="search-indexers" />
            <Button type="submit" disabled={query.isFetching || movie.versions.length === 0 || indexerPick.none || !indexerPick.ready} title={indexerPick.none ? t('searchIndexers.noneHint') : undefined} data-testid="search-run">
              <InteractiveSearchIcon className={query.isFetching ? 'animate-pulse' : undefined} aria-hidden="true" />
              {query.isFetching ? t('search.searching') : isSubmitted(text.trim()) ? t('search.searchAgain') : t('search.search')}
            </Button>
          </form>
        </search>
        <ShowQueriesToggle checked={showQueries} onChange={setShowQueries} testId="search-show-queries" />
      </div>

      {submitted === null && <p className="text-sm text-muted-foreground">{t('search.hint')}</p>}

      {query.isFetching && !data && <SearchSkeleton />}

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

          <IndexerErrors errors={data.indexerErrors} />

          {showQueries && <SearchQueriesPanel queries={data.queries} />}

          {variant === 4 && picked.length > 0 && (
            <div className="flex flex-col gap-1.5 rounded-lg border bg-muted/40 p-3" data-testid="search-picks">
              <p className="text-xs font-medium text-muted-foreground">{t('search.pick.bannerTitle')}</p>
              {picked.flatMap((release) =>
                picksOf(release).map((pick) => (
                  <div key={`${releaseKey(release)}:${pick.versionId}`} className="flex flex-wrap items-center gap-2 text-sm">
                    <PickBadge pick={pick} />
                    <span className="min-w-0 flex-1 truncate" title={release.title}>
                      {release.title}
                    </span>
                    <span className="text-xs text-muted-foreground">{release.indexer}</span>
                    <Button type="button" variant="link" size="sm" className="h-auto p-0" onClick={() => setFlash(releaseKey(release))}>
                      {t('search.pick.showInList')}
                    </Button>
                  </div>
                )),
              )}
            </div>
          )}

          {sorted.length === 0 ? (
            <p className="text-sm text-muted-foreground">{t('search.noResults')}</p>
          ) : (
            <div className="max-h-[32rem] overflow-y-auto overflow-x-auto rounded-lg border">
              <Table data-testid="search-results">
                <TableHeader className="sticky top-0 z-10 bg-background">
                  <TableRow>
                    {variant === 5 && (
                      <TableHead className="w-8 px-2">
                        <span className="sr-only">{t('search.pick.column')}</span>
                      </TableHead>
                    )}
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
                  {pinned.length > 0 && <GroupRow label={t('search.pick.pinnedGroup')} columns={7} />}
                  {[...pinned, ...(pinned.length > 0 ? [null] : []), ...releases].map((release) => {
                    if (release === null) return <GroupRow key="others" label={t('search.pick.otherGroup')} columns={7} />
                    const target = versionById.get(release.suggestedVersionId ?? -1) ?? defaultVersion
                    return (
                      <ReleaseRow
                        key={releaseKey(release)}
                        variant={variant}
                        flash={flash === releaseKey(release)}
                        release={release}
                        fits={fitsOf(release)}
                        versions={movie.versions}
                        target={target}
                        grabbing={grab.isPending && grab.variables?.release.guid === release.guid && grab.variables.release.indexerId === release.indexerId}
                        onGrab={(versionId) => requestGrab(release, versionId)}
                        onGrabOneShot={() => setOneShot({ release, profileId: target?.qualityProfileId ?? defaultVersion?.qualityProfileId ?? 0 })}
                        onOverride={() => setOverriding({ release, versionId: target?.id })}
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

      <GrabConfirmDialog pending={pending} onClose={() => setPending(null)} />
      {overriding && (
        <GrabOverrideDialog
          release={overriding.release}
          versions={movie.versions}
          defaultVersionId={overriding.versionId}
          onGrab={(versionId, override) => grab.mutate({ release: overriding.release, versionId, override })}
          onClose={() => setOverriding(null)}
        />
      )}
      {oneShot && <OneShotGrabDialog movie={movie} release={oneShot.release} defaultProfileId={oneShot.profileId} onClose={() => setOneShot(null)} />}
    </section>
  )
}

function GroupRow({ label, columns }: { label: string; columns: number }) {
  return (
    <TableRow className="hover:bg-transparent">
      <TableCell colSpan={columns} className="bg-muted/50 py-1.5 text-xs font-medium text-muted-foreground">
        {label}
      </TableCell>
    </TableRow>
  )
}

function ReleaseRow({
  variant,
  flash,
  release,
  fits,
  versions,
  target,
  grabbing,
  onGrab,
  onGrabOneShot,
  onOverride,
}: {
  /** Which proposal shows the picks (?picked=N). */
  variant: number
  /** Just grabbed or asked for: lit for a moment. */
  flash: boolean
  release: Release
  fits: ReturnType<typeof versionFits>
  versions: Version[]
  /** Where the grab button sends the release: the version that accepts it, or the closest. */
  target: Version | undefined
  grabbing: boolean
  onGrab: (versionId: number) => void
  /** Creates a one-shot version for this release and grabs it there. */
  onGrabOneShot: () => void
  /** Grabs it as the user says it is: version, quality, languages. */
  onOverride: () => void
}) {
  const { t } = useTranslation()
  const good = fitsAny(release)
  const grabbable = isGrabbable(release)
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
  const picks = picksOf(release)
  const current = isCurrentPick(release)
  const badges = picks.map((pick) => <PickBadge key={pick.versionId} pick={pick} />)

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
    <TableRow
      className={cn(
        !good && 'text-muted-foreground',
        variant === 1 && current && 'bg-primary/[0.06] shadow-[inset_3px_0_0_var(--color-primary)] hover:bg-primary/10',
        flash && 'bg-primary/15 transition-colors duration-700',
      )}
      data-fits={good}
      data-picked={picks.length > 0}
      data-release-key={`${release.indexerId}:${release.guid}`}
    >
      {variant === 5 && (
        <TableCell className="w-8 px-2 align-top">
          {picks.length > 0 ? (
            <Tooltip>
              <TooltipTrigger asChild>
                <span className={cn('inline-flex size-6 items-center justify-center rounded-full border', pickStyles[picks[0].kind])} data-testid="release-pick-dot">
                  {(() => {
                    const Icon = current ? CircleCheckIcon : pickIcons[picks[0].kind]
                    return <Icon className="size-3.5" aria-hidden="true" />
                  })()}
                </span>
              </TooltipTrigger>
              <TooltipContent>
                <div className="flex flex-col gap-1">
                  {badges}
                </div>
              </TooltipContent>
            </Tooltip>
          ) : (
            <CircleIcon className="mt-1 size-3.5 text-muted-foreground/30" aria-hidden="true" />
          )}
        </TableCell>
      )}
      <TableCell className="align-top whitespace-nowrap">
        <Tooltip>
          <TooltipTrigger asChild>
            <span>{formatAge(release.publishDate)}</span>
          </TooltipTrigger>
          <TooltipContent>{formatDateTime(release.publishDate)}</TooltipContent>
        </Tooltip>
      </TableCell>
      <ReleaseTitleCell release={release} good={good} details={details} className="min-w-[24rem]" extra={variant === 1 || variant === 3 ? badges : undefined} />
      <IndexerSizePeersCells release={release} />
      <FitCell fits={fits} matches={release.matchesMovie} testId="release-fit" />
      <TableCell className="align-top">
        {variant === 2 && picks.length > 0 && <div className="mb-1.5 flex flex-col items-end gap-1">{badges}</div>}
        <GrabSplitButton
          accepted={accepted}
          tip={grabTip}
          targetName={target?.name}
          grabbing={grabbing}
          disabled={grabbing || targetBusy || !grabbable || !target}
          menuDisabled={grabbing || !grabbable}
          onGrab={() => target && onGrab(target.id)}
          testId="grab"
          menuTestId="grab-menu"
          menuClassName="min-w-64"
        >
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
          <DropdownMenuItem onSelect={onOverride} data-testid="grab-override">
            <PencilLineIcon />
            {t('grabOverride.menu')}
          </DropdownMenuItem>
          <DropdownMenuItem onSelect={onGrabOneShot} data-testid="grab-oneshot">
            <CopyPlusIcon />
            {t('search.grabOneShot')}
          </DropdownMenuItem>
        </GrabSplitButton>
      </TableCell>
    </TableRow>
  )
}
