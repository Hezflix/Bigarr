import { useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { cn } from 'cn'
import { CopyPlusIcon, SearchIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ApiError, queue, search, type Movie, type Release, type Version } from '@/api/client'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { DropdownMenuItem, DropdownMenuSeparator } from '@/components/ui/dropdown-menu'
import { Input } from '@/components/ui/input'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { OneShotGrabDialog } from '@/features/search/OneShotGrabDialog'
import { grabObjections } from '@/features/search/grabRules'
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
  ReleaseTitleCell,
  SearchSkeleton,
  SortableHead as SharedSortableHead,
  sortReleases,
  useReleaseSort,
  VerdictIcon,
  VersionVerdicts,
} from '@/features/search/releaseTable'
import { formatAge, formatDateTime } from '@/i18n'

type SortKey = keyof typeof commonComparators

const SortableHead = (props: Omit<Parameters<typeof SharedSortableHead<SortKey>>[0], 'testIdPrefix'>) => <SharedSortableHead testIdPrefix="sort" {...props} />

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
 * Interactive search for one film: every indexer once, every release judged by every version. The
 * version is chosen when grabbing; each release suggests the one it fits. A typed text replaces the film's ids and
 * titles for films listed under another name; the releases are still judged against this film's versions.
 */
export function InteractiveSearch({ movie }: { movie: Movie }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  // null: nothing searched yet. '' is the film's own search (ids and titles), anything else the user's text.
  const [submitted, setSubmitted] = useState<string | null>(() => cachedSearchText(queryClient, movie.id))
  const [text, setText] = useState(submitted ?? '')
  const [sort, onSort] = useReleaseSort<SortKey>()
  const [oneShot, setOneShot] = useState<{ release: Release; profileId: number } | null>(null)
  const [pending, setPending] = useState<PendingGrab | null>(null)
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

  const grab = useMutation({
    mutationFn: ({ release, versionId }: { release: Release; versionId: number }) =>
      queue.grab(grabRequest(release, versionId)),
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

  // A release the version rejects (or that is not this film) is grabbed only after the reasons are confirmed.
  const requestGrab = (release: Release, versionId: number) => {
    const go = () => grab.mutate({ release, versionId })
    const decision = release.versions.find((v) => v.versionId === versionId)
    const reasons = grabObjections(release.matchesMovie, release.rejections, decision, t('search.noVerdict'))
    if (reasons.length === 0) go()
    else setPending({ title: release.title, version: versionById.get(versionId)?.name ?? '', reasons, go })
  }

  const data = query.data
  const releases = data ? sortReleases(data.releases, sort, commonComparators) : []
  const acceptable = movie.versions.map((v) => ({
    version: v,
    count: data?.releases.filter((r) => r.matchesMovie && r.versions.some((d) => d.versionId === v.id && d.approved)).length ?? 0,
  }))

  return (
    <section className="flex flex-col gap-3" data-testid="interactive-search">
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
            <Button type="submit" disabled={query.isFetching || movie.versions.length === 0} data-testid="search-run">
              <SearchIcon className={query.isFetching ? 'animate-pulse' : undefined} />
              {query.isFetching ? t('search.searching') : submitted !== null && submitted === text.trim() ? t('search.searchAgain') : t('search.search')}
            </Button>
          </form>
        </search>
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
                        onGrab={(versionId) => requestGrab(release, versionId)}
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

      <GrabConfirmDialog pending={pending} onClose={() => setPending(null)} />
      {oneShot && <OneShotGrabDialog movie={movie} release={oneShot.release} defaultProfileId={oneShot.profileId} onClose={() => setOneShot(null)} />}
    </section>
  )
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
  /** Creates a one-shot version for this release and grabs it there. */
  onGrabOneShot: () => void
}) {
  const { t } = useTranslation()
  const good = fits(release)
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
      <ReleaseTitleCell release={release} good={good} details={details} className="min-w-[24rem]" />
      <IndexerSizePeersCells release={release} />
      <TableCell className="min-w-[18rem] align-top whitespace-normal" data-testid="release-fit">
        {!release.matchesMovie && (
          <span className="mb-1 flex flex-wrap gap-1">
            {release.rejections.map((r) => (
              <Badge key={r.reason} variant="outline" className={cn('h-auto rounded-md whitespace-normal text-left', permanentStyle)}>
                {t('search.notThisFilm', { reason: r.reason })}
              </Badge>
            ))}
          </span>
        )}
        <VersionVerdicts decisions={release.versions} matches={release.matchesMovie} />
      </TableCell>
      <TableCell className="align-top">
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
          <DropdownMenuItem onSelect={onGrabOneShot} data-testid="grab-oneshot">
            <CopyPlusIcon />
            {t('search.grabOneShot')}
          </DropdownMenuItem>
        </GrabSplitButton>
      </TableCell>
    </TableRow>
  )
}
