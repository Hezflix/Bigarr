import type { TFunction } from 'i18next'
import { useEffect, useMemo, useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useParams } from '@tanstack/react-router'
import { cn } from 'cn'
import { BookmarkIcon, ChevronRightIcon, ClapperboardIcon, Columns3Icon, Link2Icon, FilePenLineIcon, FilterIcon, FolderInputIcon, FolderSearchIcon, ListChecksIcon, ListOrderedIcon, PencilIcon, PlusIcon, RefreshCwIcon, StarIcon, TextSearchIcon, Trash2Icon, WrenchIcon, ZapIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { LibraryIssuesLink } from '@/features/issues/TitleIssues'
import { SeriesCompareSection } from '@/features/activity/SeriesCompare'
import { toast } from 'sonner'
import {
  ApiError,
  auth,
  customFormats,
  quality,
  series as seriesApi,
  seriesWanted,
  seriesVersionName,
  type Episode,
  type EpisodeFile,
  type MonitoringOption,
  type Season,
  type Series,
  type SeriesVersion,
  type UnmatchedEpisodeFile,
  profilesFor,
} from '@/api/client'
import { episodeCode } from '@/lib/episodeCode'
import { Poster } from '@/components/Poster'
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { DropdownMenu, DropdownMenuCheckboxItem, DropdownMenuContent, DropdownMenuLabel, DropdownMenuTrigger } from '@/components/ui/dropdown-menu'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { IdSelect } from '@/components/IdSelect'
import { MonitoringSelect } from '@/components/MonitoringSelect'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { formatBytes, formatDate } from '@/i18n'
import { EpisodeDetailDialog } from './EpisodeDetailDialog'
import { EpisodeProgress } from './EpisodeProgress'
import { SeasonAlignButton, SeasonAlignDialog } from './SeasonAlignDialog'
import { orderSeason } from './orderSeason'
import { audioLabel, type EpisodeColumn, episodeColumns, type EpisodeStatus, episodeStatus, fileFlags, fileLanguages } from './episodeColumns'
import { defaultEpisodeSort, type EpisodeSort, type EpisodeSortKey, nextEpisodeSort, sortEpisodes, sortStillShown } from './episodeSort'
import { useSeasonTablePrefs } from './seasonTablePrefs'
import { ariaSort, SortButton } from '@/features/library/SortButton'
import { SeriesFilmsSection } from './SeriesFilmsSection'
import { AutomaticSearchButton, InteractiveSearchButton } from '@/features/search/SearchButtons'
import { SeriesSearchDialog, type SeriesSearchScope } from '@/features/search/SeriesInteractiveSearch'
import { EditSeriesVersionDialog } from '@/features/series/EditSeriesVersionDialog'
import type { PaletteParam } from '@/features/palette/actions'
import { parseSeriesScope, type SeriesScope, seriesScopeOptions, shownEpisodeCode } from '@/features/palette/seriesScopes'
import { toastEpisodeSearch, useEpisodeAutomaticSearch } from '@/features/series/useEpisodeAutomaticSearch'
import { Checkbox } from '@/components/ui/checkbox'
import { SeriesManualImportDialog } from '@/features/import/SeriesManualImportDialog'
import { HeldReleasesSection } from '@/features/activity/HeldPage'
import { AssignUnmatchedDialog, DeleteFilesOption, EpisodeFileActions, RenameSeriesDialog } from './SeriesFiles'
import { OrderUndoBanner, ReassignFileDialog, SeriesOrderDialog, orderLabel } from './SeriesOrderDialog'
import { useRecordRecent } from '@/features/palette/recent'
import { usePaletteActions } from '@/features/palette/actions'
import { versionActions } from '@/features/palette/pageActions'

const errorMessage = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback)

export function SeriesDetailPage() {
  const { t } = useTranslation()
  const { seriesId } = useParams({ from: '/shell/app/series/$seriesId' })
  const id = Number(seriesId)
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const detail = useQuery({ queryKey: ['seriesDetail', id], queryFn: () => seriesApi.get(id) })
  const episodes = useQuery({ queryKey: ['seriesEpisodes', id], queryFn: () => seriesApi.episodes(id) })
  const files = useQuery({ queryKey: ['seriesFiles', id], queryFn: () => seriesApi.files(id) })
  useRecordRecent(detail.data ? { kind: 'series', id, title: detail.data.title, year: detail.data.year } : null)
  const [chosenVersion, setChosenVersion] = useState<number | null>(null)
  const [deleteOpen, setDeleteOpen] = useState(false)
  const [searchScope, setSearchScope] = useState<SeriesSearchScope | null>(null)
  const [renameOpen, setRenameOpen] = useState(false)
  const [importOpen, setImportOpen] = useState(false)
  const [editOpen, setEditOpen] = useState(false)
  const [deleteFiles, setDeleteFiles] = useState(false)
  const [orderOpen, setOrderOpen] = useState(false)

  const invalidate = () => {
    void queryClient.invalidateQueries({ queryKey: ['seriesDetail', id] })
    void queryClient.invalidateQueries({ queryKey: ['seriesEpisodes', id] })
    void queryClient.invalidateQueries({ queryKey: ['seriesFiles', id] })
    void queryClient.invalidateQueries({ queryKey: ['series'] })
    void queryClient.invalidateQueries({ queryKey: ['seriesOrders', id] })
    void queryClient.invalidateQueries({ queryKey: ['episodes', id] })
    void queryClient.invalidateQueries({ queryKey: ['seriesFilms', id] })
  }

  // The rescan runs in the background; poll while it is queued or running, refresh what it changed once it ends.
  const rescanStatus = useQuery({
    queryKey: ['seriesRescan', id],
    queryFn: () => seriesApi.rescanStatus(id),
    refetchInterval: (query) => (query.state.data?.state === 'running' || query.state.data?.state === 'queued' ? 1000 : false),
  })
  const rescan = useMutation({
    mutationFn: () => seriesApi.rescan(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['seriesRescan', id] }),
    onError: (error) => toast.error(errorMessage(error, t('common.unexpectedError'))),
  })
  const rescanState = rescanStatus.data?.state
  const rescanning = rescanState === 'running' || rescanState === 'queued'
  useEffect(() => {
    if (rescanState !== 'completed' || !rescanStatus.data?.finishedAt) return
    void queryClient.invalidateQueries({ queryKey: ['seriesDetail', id] })
    void queryClient.invalidateQueries({ queryKey: ['seriesEpisodes', id] })
    void queryClient.invalidateQueries({ queryKey: ['seriesFiles', id] })
  }, [rescanState, rescanStatus.data?.finishedAt, id, queryClient])

  const refresh = useMutation({
    mutationFn: () => seriesApi.refresh(id),
    onSuccess: () => {
      toast.success(t('seriesDetail.refreshed'))
      invalidate()
    },
    onError: (error) => toast.error(errorMessage(error, t('common.unexpectedError'))),
  })
  const remove = useMutation({
    mutationFn: () => seriesApi.remove(id, deleteFiles),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['series'] })
      void navigate({ to: '/series' })
    },
  })

  // Cmd+K on this page: the header's buttons, and the shown version's folder and monitoring.
  const loaded = detail.data
  // Tab on a search narrows it to a season or an episode.
  // Seasons and codes follow the show's order, as on the page.
  const altOrder = detail.data ? detail.data.ordering !== 'tvdb' : false
  const scopeParam = (run: (scope: SeriesScope) => unknown): PaletteParam => ({
    hint: t('palette.param.seriesHint'),
    options: () => seriesScopeOptions(t, episodes.data ?? [], altOrder),
    run: (option) => {
      const scope = parseSeriesScope(option.id, episodes.data ?? [], altOrder)
      if (scope) return run(scope)
    },
  })
  const shownVersion = loaded ? (loaded.versions.find((v) => v.id === chosenVersion) ?? loaded.versions.find((v) => v.isDefault) ?? loaded.versions[0]) : undefined
  usePaletteActions(
    loaded ? t('palette.page.series', { title: loaded.title }) : '',
    loaded
      ? [
          {
            id: 'search-missing',
            label: t('palette.page.searchMissing'),
            icon: ZapIcon,
            keywords: ['automatic', 'wanted'],
            run: () => searchMissingEpisodes(t, id),
            param: scopeParam((scope) => {
              if (scope.kind === 'series') return searchMissingEpisodes(t, id)
              if (scope.kind === 'season') return searchMissingEpisodes(t, id, new Set(scope.episodeIds))
              return seriesApi.searchEpisode(id, scope.episode.id).then(
                (results) => {
                  toastEpisodeSearch(t, results)
                  void queryClient.invalidateQueries({ queryKey: ['queue'] })
                },
                (error) => toast.error(errorMessage(error, t('search.failed'))),
              )
            }),
          },
          {
            id: 'interactive-search',
            label: t('palette.page.interactiveSearch'),
            icon: TextSearchIcon,
            keywords: ['releases', 'manual'],
            run: () => setSearchScope({ kind: 'series', label: loaded.title }),
            param: scopeParam((scope) =>
              setSearchScope(
                scope.kind === 'series'
                  ? { kind: 'series', label: loaded.title }
                  : scope.kind === 'season'
                    ? {
                        kind: 'season',
                        season: scope.season,
                        orderSeason: scope.orderSeason,
                        label: scope.season === 0 ? t('palette.param.specials') : t('palette.param.season', { number: scope.season }),
                      }
                    : { kind: 'episode', episodeId: scope.episode.id, label: shownEpisodeCode(scope.episode, altOrder) },
              ),
            ),
          },
          { id: 'refresh', label: t('palette.page.refresh'), icon: RefreshCwIcon, keywords: ['tvdb', 'tmdb', 'metadata'], run: () => refresh.mutate() },
          { id: 'rescan', label: t('palette.page.rescan'), icon: FolderSearchIcon, keywords: ['scan', 'disk'], run: () => rescan.mutate() },
          { id: 'rename', label: t('palette.page.rename'), icon: FilePenLineIcon, keywords: ['files'], run: () => setRenameOpen(true) },
          { id: 'manual-import', label: t('palette.page.manualImport'), icon: FolderInputIcon, keywords: ['import', 'files'], run: () => setImportOpen(true) },
          ...versionActions(t, shownVersion, (monitored) => seriesApi.updateVersion(id, shownVersion?.id ?? 0, { monitored }), invalidate, () => setEditOpen(true)),
        ]
      : null,
  )

  if (detail.isPending) {
    return (
      <div className="flex flex-col gap-6 sm:flex-row">
        <Skeleton className="aspect-[2/3] w-40 rounded-md sm:w-48" />
        <div className="flex flex-1 flex-col gap-3">
          <Skeleton className="h-8 w-2/3" />
          <Skeleton className="h-4 w-1/3" />
          <Skeleton className="h-20 w-full" />
        </div>
      </div>
    )
  }

  if (detail.error) {
    return (
      <Alert variant="destructive">
        <AlertDescription>{t('seriesDetail.notFound')}</AlertDescription>
      </Alert>
    )
  }

  const s = detail.data
  const version = s.versions.find((v) => v.id === chosenVersion) ?? s.versions.find((v) => v.isDefault) ?? s.versions[0]

  return (
    <>
      <div className="flex flex-col gap-6 sm:flex-row" data-testid="series-header">
        <Poster url={s.posterUrl} title={s.title} size="large" />
        <div className="flex min-w-0 flex-1 flex-col gap-3">
          <div>
            <h1 className="flex flex-wrap items-baseline gap-2 text-2xl font-semibold tracking-tight">
              {s.title}
              {s.year && <span className="text-lg font-normal text-muted-foreground">({s.year})</span>}
            </h1>
            {s.originalTitle && s.originalTitle !== s.title && <p className="text-sm text-muted-foreground">{s.originalTitle}</p>}
          </div>

          <div className="flex flex-wrap items-center gap-2">
            <Badge variant="secondary">{t(`seriesStatus.${s.status}`)}</Badge>
            <Badge variant="secondary">{t(`seriesType.${s.seriesType}`)}</Badge>
            {s.network && <Badge variant="outline">{s.network}</Badge>}
            {s.runtime && <Badge variant="outline">{t('common.minutes', { count: s.runtime })}</Badge>}
            {s.genres.slice(0, 5).map((genre) => (
              <Badge key={genre} variant="outline">
                {genre}
              </Badge>
            ))}
          </div>

          {s.overview && <p className="max-w-3xl text-sm leading-relaxed text-muted-foreground">{s.overview}</p>}

          <LibraryIssuesLink seriesId={s.id} />

          <div className="flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-muted-foreground">
            {s.tvdbId && (
              <a href={`https://thetvdb.com/dereferrer/series/${s.tvdbId}`} target="_blank" rel="noreferrer" className="underline underline-offset-4 hover:text-foreground">
                {t('seriesDetail.ids.tvdb', { id: s.tvdbId })}
              </a>
            )}
            {s.tmdbId && (
              <a href={`https://www.themoviedb.org/tv/${s.tmdbId}`} target="_blank" rel="noreferrer" className="underline underline-offset-4 hover:text-foreground">
                {t('seriesDetail.ids.tmdb', { id: s.tmdbId })}
              </a>
            )}
            {s.imdbId && (
              <a href={`https://www.imdb.com/title/${s.imdbId}/`} target="_blank" rel="noreferrer" className="underline underline-offset-4 hover:text-foreground">
                {t('seriesDetail.ids.imdb', { id: s.imdbId })}
              </a>
            )}
            {s.firstAired && <span>{t('seriesDetail.firstAired', { date: formatDate(s.firstAired) })}</span>}
            {s.airTime && <span>{t('seriesDetail.airs', { time: s.airTime })}</span>}
            <span data-testid="series-order-current">{t('seriesDetail.order', { order: orderLabel(t, s) })}</span>
          </div>

          <div className="mt-1 flex flex-wrap gap-2">
            <InteractiveSearchButton onClick={() => setSearchScope({ kind: 'series', label: s.title })} data-testid="series-search-all" />
            <Button type="button" variant="outline" onClick={() => refresh.mutate()} disabled={refresh.isPending}>
              <RefreshCwIcon className={refresh.isPending ? 'animate-spin' : undefined} />
              {t('seriesDetail.refresh')}
            </Button>
            <Button type="button" variant="outline" onClick={() => rescan.mutate()} disabled={rescanning || rescan.isPending} data-testid="series-rescan">
              <FolderSearchIcon className={rescanning ? 'animate-pulse' : undefined} />
              {rescanning ? t('seriesDetail.rescanning') : t('seriesDetail.rescan')}
            </Button>
            <Button type="button" variant="outline" onClick={() => setRenameOpen(true)} data-testid="series-rename-open">
              <FilePenLineIcon />
              {t('seriesFiles.rename')}
            </Button>
            <Button type="button" variant="outline" onClick={() => setOrderOpen(true)} data-testid="series-order-open">
              <ListOrderedIcon />
              {t('seriesOrder.open')}
            </Button>
            <Button type="button" variant="outline" onClick={() => setImportOpen(true)} data-testid="series-manual-import-open">
              <FolderInputIcon />
              {t('seriesFiles.manualImport')}
            </Button>
            <Button type="button" variant="destructive" onClick={() => setDeleteOpen(true)} disabled={remove.isPending}>
              <Trash2Icon />
              {t('seriesDetail.delete')}
            </Button>
          </div>
          <OrderUndoBanner series={s} onDone={invalidate} />
        </div>
      </div>

      <AlertDialog open={deleteOpen} onOpenChange={setDeleteOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>{t('seriesDetail.deleteTitle')}</AlertDialogTitle>
            <AlertDialogDescription>{t('seriesDetail.confirmDelete', { title: s.title })}</AlertDialogDescription>
          </AlertDialogHeader>
          <DeleteFilesOption checked={deleteFiles} onChange={setDeleteFiles} hint={t('seriesFiles.deleteSeriesFilesHint')} />
          {remove.error && (
            <Alert variant="destructive">
              <AlertDescription>{errorMessage(remove.error, t('common.unexpectedError'))}</AlertDescription>
            </Alert>
          )}
          <AlertDialogFooter>
            <AlertDialogCancel>{t('common.cancel')}</AlertDialogCancel>
            <AlertDialogAction variant="destructive" onClick={(e) => { e.preventDefault(); remove.mutate() }} disabled={remove.isPending}>
              {t('seriesDetail.delete')}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>

      {version && <VersionBar series={s} version={version} onChoose={setChosenVersion} onChanged={invalidate} onEdit={() => setEditOpen(true)} />}

      {version && (
        <SeasonList
          series={s}
          version={version}
          episodes={episodes.data}
          files={files.data?.files.filter((f) => f.versionId === version.id)}
          loading={episodes.isPending}
          onChanged={invalidate}
          onSearch={setSearchScope}
        />
      )}

      <SeriesFilmsSection seriesId={s.id} onChanged={invalidate} />

      {searchScope && <SeriesSearchDialog key={JSON.stringify(searchScope)} series={s} scope={searchScope} onClose={() => setSearchScope(null)} />}

      {version && files.data && <UnmatchedFiles seriesId={s.id} files={files.data.unmatched.filter((f) => f.versionId === version.id)} onChanged={invalidate} />}
      <HeldReleasesSection seriesId={s.id} />

      {orderOpen && <SeriesOrderDialog series={s} onClose={() => setOrderOpen(false)} onDone={invalidate} />}
      {renameOpen && version && <RenameSeriesDialog series={s} versionId={version.id} onClose={() => setRenameOpen(false)} onDone={invalidate} />}
      {editOpen && version && <EditSeriesVersionDialog key={version.id} series={s} version={version} onClose={() => setEditOpen(false)} onSaved={invalidate} />}
      {importOpen && <SeriesManualImportDialog seriesId={s.id} path={version?.path} onClose={() => { setImportOpen(false); invalidate() }} />}

      <SeriesCompareSection tvdbId={s.tvdbId} />
    </>
  )
}

// ---- versions -------------------------------------------------------------------------------------------------------

function VersionBar({ series: s, version, onChoose, onChanged, onEdit }: { series: Series; version: SeriesVersion; onChoose: (id: number) => void; onChanged: () => void; onEdit: () => void }) {
  const { t } = useTranslation()
  const [monitoringOpen, setMonitoringOpen] = useState(false)
  const [addOpen, setAddOpen] = useState(false)
  const [deleteOpen, setDeleteOpen] = useState(false)
  const [constraintsOpen, setConstraintsOpen] = useState(false)

  const toggle = useMutation({
    mutationFn: () => seriesApi.updateVersion(s.id, version.id, { monitored: !version.monitored }),
    onSuccess: onChanged,
    onError: (error) => toast.error(errorMessage(error, t('common.unexpectedError'))),
  })
  const promote = useMutation({
    mutationFn: () => seriesApi.promote(s.id, version.id),
    onSuccess: onChanged,
    onError: (error) => toast.error(errorMessage(error, t('common.unexpectedError'))),
  })

  return (
    <Card className="gap-3 px-4 py-3" data-testid="series-versions">
      <div className="flex flex-wrap items-center gap-2">
        <span className="text-sm font-medium">{t('seriesDetail.version')}</span>
        <div className="flex flex-wrap gap-1" role="tablist">
          {s.versions.map((v) => (
            <Button
              key={v.id}
              type="button"
              size="sm"
              role="tab"
              aria-selected={v.id === version.id}
              variant={v.id === version.id ? 'secondary' : 'ghost'}
              onClick={() => onChoose(v.id)}
              className={cn(!v.monitored && 'text-muted-foreground')}
            >
              {v.isDefault && <StarIcon className="fill-current" aria-label={t('seriesDetail.default')} />}
              {v.name}
            </Button>
          ))}
        </div>
        <div className="ml-auto flex flex-wrap gap-2">
          <Button type="button" size="sm" variant="outline" onClick={() => setMonitoringOpen(true)}>
            <ListChecksIcon />
            {t('seriesDetail.applyMonitoring')}
          </Button>
          <Button type="button" size="sm" variant="outline" onClick={() => setAddOpen(true)}>
            <PlusIcon />
            {t('seriesDetail.addVersion')}
          </Button>
        </div>
      </div>

      <div className="flex flex-wrap items-center gap-x-4 gap-y-2 text-sm">
        <Button
          type="button"
          size="sm"
          variant={version.monitored ? 'secondary' : 'outline'}
          aria-pressed={version.monitored}
          onClick={() => toggle.mutate()}
          disabled={toggle.isPending}
          data-testid="version-monitored"
        >
          <BookmarkIcon className={cn(version.monitored && 'fill-current')} />
          {version.monitored ? t('seriesDetail.monitored') : t('seriesDetail.unmonitored')}
        </Button>
        <span className="text-muted-foreground">{version.qualityProfile}</span>
        <span className="truncate font-mono text-xs text-muted-foreground" title={version.path}>
          {version.path}
        </span>
        <span className="text-muted-foreground">{version.seasonFolder ? t('seriesDetail.seasonFolder') : t('seriesDetail.noSeasonFolder')}</span>
        <span className="text-muted-foreground">{t('seriesDetail.newSeasons', { value: t(`monitoringOption.${version.monitorNewItems === 'all' ? 'all' : 'none'}`) })}</span>
        {version.statistics.sizeOnDisk > 0 && <span className="text-muted-foreground">{formatBytes(version.statistics.sizeOnDisk)}</span>}
        <EpisodeProgress statistics={version.statistics} monitored={version.monitored} className="w-48" />
        <ConstraintsSummary version={version} />
        <div className="ml-auto flex gap-2">
          <Button type="button" size="sm" variant="ghost" onClick={onEdit} data-testid="version-edit">
            <PencilIcon />
            {t('versions.edit')}
          </Button>
          <Button type="button" size="sm" variant="ghost" onClick={() => setConstraintsOpen(true)} data-testid="version-constraints">
            <FilterIcon />
            {t('seriesDetail.constraints')}
          </Button>
          {!version.isDefault && (
            <Button type="button" size="sm" variant="ghost" onClick={() => promote.mutate()} disabled={promote.isPending}>
              <StarIcon />
              {t('seriesDetail.promote')}
            </Button>
          )}
          <Button type="button" size="sm" variant="ghost" className="text-destructive" onClick={() => setDeleteOpen(true)}>
            <Trash2Icon />
            {t('seriesDetail.deleteVersion')}
          </Button>
        </div>
      </div>

      <ApplyMonitoringDialog series={s} version={version} open={monitoringOpen} onOpenChange={setMonitoringOpen} onDone={onChanged} />
      <AddVersionDialog series={s} open={addOpen} onOpenChange={setAddOpen} onDone={(created) => { onChoose(created); onChanged() }} />
      <DeleteVersionDialog series={s} version={version} open={deleteOpen} onOpenChange={setDeleteOpen} onDone={onChanged} />
      {constraintsOpen && <ConstraintsDialog key={version.id} series={s} version={version} onClose={() => setConstraintsOpen(false)} onDone={onChanged} />}
    </Card>
  )
}

/** The version's constraints, when it has some: what tells two versions with one profile apart. */
function ConstraintsSummary({ version }: { version: SeriesVersion }) {
  const { t } = useTranslation()
  const c = version.constraints
  if (!c) return null
  const parts = [
    c.edition ? t('seriesDetail.titlePattern', { value: c.edition }) : null,
    c.releaseGroup ? t('versions.group', { value: c.releaseGroup }) : null,
    c.requiredCustomFormatIds?.length ? t('seriesDetail.requiredCount', { count: c.requiredCustomFormatIds.length }) : null,
    c.forbiddenCustomFormatIds?.length ? t('seriesDetail.forbiddenCount', { count: c.forbiddenCustomFormatIds.length }) : null,
  ].filter(Boolean)
  return (
    <span className="text-xs text-muted-foreground" data-testid="version-constraints-summary">
      {parts.join(' · ')}
    </span>
  )
}

/**
 * Edits a series version's constraints, like a film version's: a title pattern (episode names carry no edition, so it
 * is matched on the release title), a release group, required and forbidden custom formats.
 */
function ConstraintsDialog({ series: s, version, onClose, onDone }: { series: Series; version: SeriesVersion; onClose: () => void; onDone: () => void }) {
  const { t } = useTranslation()
  const current = version.constraints
  const [pattern, setPattern] = useState(current?.edition ?? '')
  const [group, setGroup] = useState(current?.releaseGroup ?? '')
  const [required, setRequired] = useState<number[]>(current?.requiredCustomFormatIds ?? [])
  const [forbidden, setForbidden] = useState<number[]>(current?.forbiddenCustomFormatIds ?? [])
  const formats = useQuery({ queryKey: ['customFormats'], queryFn: customFormats.list })
  const empty = !pattern.trim() && !group.trim() && required.length === 0 && forbidden.length === 0
  const save = useMutation({
    mutationFn: () =>
      seriesApi.updateVersion(
        s.id,
        version.id,
        empty
          ? { clearConstraints: true }
          : {
              constraints: {
                edition: pattern.trim() || null,
                releaseGroup: group.trim() || null,
                requiredCustomFormatIds: required.length > 0 ? required : null,
                forbiddenCustomFormatIds: forbidden.length > 0 ? forbidden : null,
              },
            },
      ),
    onSuccess: () => {
      onDone()
      onClose()
    },
  })
  const toggle = (list: number[], set: (v: number[]) => void, id: number) => set(list.includes(id) ? list.filter((x) => x !== id) : [...list, id])

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="sm:max-w-2xl">
        <form
          onSubmit={(e: FormEvent) => {
            e.preventDefault()
            save.mutate()
          }}
        >
          <DialogHeader>
            <DialogTitle>{t('seriesDetail.constraintsTitle', { version: version.name })}</DialogTitle>
            <DialogDescription>{t('seriesDetail.constraintsHint')}</DialogDescription>
          </DialogHeader>
          <div className="my-6 grid gap-4 sm:grid-cols-2">
            <div className="grid grid-cols-1 gap-2">
              <Label htmlFor="series-constraint-pattern">{t('seriesDetail.titlePatternLabel')}</Label>
              <Input id="series-constraint-pattern" value={pattern} onChange={(e) => setPattern(e.target.value)} placeholder="vostfr|multi" />
            </div>
            <div className="grid grid-cols-1 gap-2">
              <Label htmlFor="series-constraint-group">{t('versions.releaseGroup')}</Label>
              <Input id="series-constraint-group" value={group} onChange={(e) => setGroup(e.target.value)} placeholder="NTb" />
            </div>
            {formats.data && formats.data.length > 0 && (
              <>
                <fieldset className="grid grid-cols-1 gap-2">
                  <legend className="text-sm font-medium">{t('versions.requiredFormats')}</legend>
                  <div className="flex flex-wrap gap-x-4 gap-y-2">
                    {formats.data.map((f) => (
                      <label key={f.id} className="flex items-center gap-2 text-sm font-normal">
                        <Checkbox checked={required.includes(f.id)} disabled={forbidden.includes(f.id)} onCheckedChange={() => toggle(required, setRequired, f.id)} />
                        {f.name}
                      </label>
                    ))}
                  </div>
                </fieldset>
                <fieldset className="grid grid-cols-1 gap-2">
                  <legend className="text-sm font-medium">{t('versions.forbiddenFormats')}</legend>
                  <div className="flex flex-wrap gap-x-4 gap-y-2">
                    {formats.data.map((f) => (
                      <label key={f.id} className="flex items-center gap-2 text-sm font-normal">
                        <Checkbox checked={forbidden.includes(f.id)} disabled={required.includes(f.id)} onCheckedChange={() => toggle(forbidden, setForbidden, f.id)} />
                        {f.name}
                      </label>
                    ))}
                  </div>
                </fieldset>
              </>
            )}
          </div>
          {save.error && (
            <Alert variant="destructive" className="mb-4">
              <AlertDescription>{errorMessage(save.error, t('common.unexpectedError'))}</AlertDescription>
            </Alert>
          )}
          <DialogFooter>
            <Button type="button" variant="ghost" onClick={onClose}>
              {t('common.cancel')}
            </Button>
            <Button type="submit" disabled={save.isPending} data-testid="version-constraints-save">
              {t('common.save')}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}

function ApplyMonitoringDialog({ series: s, version, open, onOpenChange, onDone }: { series: Series; version: SeriesVersion; open: boolean; onOpenChange: (open: boolean) => void; onDone: () => void }) {
  const { t } = useTranslation()
  const [option, setOption] = useState<MonitoringOption>('all')
  const apply = useMutation({
    mutationFn: () => seriesApi.applyMonitoring(s.id, version.id, option),
    onSuccess: () => {
      onOpenChange(false)
      onDone()
    },
  })
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t('seriesDetail.applyMonitoringTitle')}</DialogTitle>
          <DialogDescription>{t('seriesDetail.applyMonitoringHint', { version: version.name })}</DialogDescription>
        </DialogHeader>
        <MonitoringSelect value={option} onChange={setOption} ariaLabel={t('addSeries.monitor')} />
        {apply.error && (
          <Alert variant="destructive">
            <AlertDescription>{errorMessage(apply.error, t('common.unexpectedError'))}</AlertDescription>
          </Alert>
        )}
        <DialogFooter>
          <Button type="button" variant="ghost" onClick={() => onOpenChange(false)}>
            {t('common.cancel')}
          </Button>
          <Button type="button" onClick={() => apply.mutate()} disabled={apply.isPending}>
            {t('seriesDetail.apply')}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

function AddVersionDialog({ series: s, open, onOpenChange, onDone }: { series: Series; open: boolean; onOpenChange: (open: boolean) => void; onDone: (id: number) => void }) {
  const { t } = useTranslation()
  const profiles = useQuery({ queryKey: ['qualityProfiles'], queryFn: quality.profiles, select: profilesFor('series'), enabled: open })
  const [name, setName] = useState('')
  const [profileId, setProfileId] = useState<number | ''>('')
  const [monitor, setMonitor] = useState<MonitoringOption>('all')
  const profile = profiles.data?.find((p) => p.id === profileId) ?? profiles.data?.[0]
  const effectiveName = name.trim() || (profile ? seriesVersionName(profile.name) : '')

  const add = useMutation({
    mutationFn: () => seriesApi.addVersion(s.id, { name: effectiveName, qualityProfileId: profile?.id ?? 0, monitor }),
    onSuccess: (created) => {
      onOpenChange(false)
      setName('')
      onDone(created.id)
    },
  })
  const submit = (e: FormEvent) => {
    e.preventDefault()
    add.mutate()
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <form onSubmit={submit} className="flex flex-col gap-4">
          <DialogHeader>
            <DialogTitle>{t('seriesDetail.addVersionTitle')}</DialogTitle>
          </DialogHeader>
          <div className="grid gap-2">
            <Label htmlFor="series-version-new-name">{t('seriesDetail.versionName')}</Label>
            <Input id="series-version-new-name" value={name} onChange={(e) => setName(e.target.value)} placeholder={effectiveName} />
            <p className="text-xs text-muted-foreground">{t('addMovie.versionNameHint')}</p>
          </div>
          <div className="grid gap-2">
            <Label htmlFor="series-version-new-profile">{t('addMovie.qualityProfile')}</Label>
            <IdSelect id="series-version-new-profile" value={profile?.id} onChange={setProfileId} options={profiles.data?.map((p) => ({ id: p.id, label: p.name }))} />
          </div>
          <div className="grid gap-2">
            <Label htmlFor="series-version-new-monitor">{t('addSeries.monitor')}</Label>
            <MonitoringSelect id="series-version-new-monitor" value={monitor} onChange={setMonitor} />
          </div>
          {add.error && (
            <Alert variant="destructive">
              <AlertDescription>{errorMessage(add.error, t('common.unexpectedError'))}</AlertDescription>
            </Alert>
          )}
          <DialogFooter>
            <Button type="button" variant="ghost" onClick={() => onOpenChange(false)}>
              {t('common.cancel')}
            </Button>
            <Button type="submit" disabled={add.isPending || !profile || effectiveName === ''}>
              {t('addSeries.submit')}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}

function DeleteVersionDialog({ series: s, version, open, onOpenChange, onDone }: { series: Series; version: SeriesVersion; open: boolean; onOpenChange: (open: boolean) => void; onDone: () => void }) {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const others = s.versions.filter((v) => v.id !== version.id && v.mode === 'managed')
  const [successor, setSuccessor] = useState<number | null>(null)
  const [deleteFiles, setDeleteFiles] = useState(false)
  const promoteTo = successor ?? others[0]?.id
  const last = s.versions.length === 1
  const remove = useMutation({
    mutationFn: () => last ? seriesApi.remove(s.id, deleteFiles) : seriesApi.deleteVersion(s.id, version.id, version.isDefault ? promoteTo : undefined, deleteFiles),
    onSuccess: () => {
      onOpenChange(false)
      if (last) void navigate({ to: '/series' })
      else onDone()
    },
  })
  return (
    <AlertDialog open={open} onOpenChange={onOpenChange}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{t('seriesDetail.deleteVersion')}</AlertDialogTitle>
          <AlertDialogDescription>{last ? t('seriesDetail.lastVersion') : t('seriesDetail.confirmDeleteVersion', { version: version.name })}</AlertDialogDescription>
        </AlertDialogHeader>
        {version.isDefault && others.length > 0 && (
          <div className="grid gap-2">
            <Label htmlFor="series-version-successor">{t('seriesDetail.promoteTo')}</Label>
            <IdSelect id="series-version-successor" value={promoteTo} onChange={setSuccessor} options={others.map((v) => ({ id: v.id, label: v.name }))} />
          </div>
        )}
        <DeleteFilesOption checked={deleteFiles} onChange={setDeleteFiles} hint={t('seriesFiles.deleteVersionFilesHint')} />
        {remove.error && (
          <Alert variant="destructive">
            <AlertDescription>{errorMessage(remove.error, t('common.unexpectedError'))}</AlertDescription>
          </Alert>
        )}
        <AlertDialogFooter>
          <AlertDialogCancel>{t('common.cancel')}</AlertDialogCancel>
          <AlertDialogAction variant="destructive" onClick={(e) => { e.preventDefault(); remove.mutate() }} disabled={remove.isPending}>
            {t('seriesDetail.deleteVersion')}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  )
}

// ---- seasons & episodes ---------------------------------------------------------------------------------------------

/** Newest season first, specials last, like Sonarr. */
function seasonOrder(a: Season, b: Season) {
  if (a.seasonNumber === 0) return 1
  if (b.seasonNumber === 0) return -1
  return b.seasonNumber - a.seasonNumber
}

/** The block of the episodes the series' order has no counterpart for. */
const NOT_IN_ORDER = -1

function SeasonList({
  series: s,
  version,
  episodes,
  files,
  loading,
  onChanged,
  onSearch,
}: {
  series: Series
  version: SeriesVersion
  episodes: Episode[] | undefined
  files: EpisodeFile[] | undefined
  loading: boolean
  onChanged: () => void
  onSearch: (scope: SeriesSearchScope) => void
}) {
  // Episode id → the version's file holding it (a multi-episode file sits on several rows).
  const fileByEpisode = useMemo(() => {
    const map = new Map<number, EpisodeFile>()
    for (const file of files ?? []) for (const episodeId of file.episodeIds) map.set(episodeId, file)
    return map
  }, [files])
  // Another order regroups the episodes by its own seasons; TVDB's seasons otherwise.
  const alt = s.ordering !== 'tvdb'
  const blocks = useMemo(() => {
    if (!alt || !episodes) {
      const bySeason = new Map<number, Episode[]>()
      for (const episode of episodes ?? []) bySeason.set(episode.seasonNumber, [...(bySeason.get(episode.seasonNumber) ?? []), episode])
      return [...s.seasons].sort(seasonOrder).map((season) => ({ season, episodes: bySeason.get(season.seasonNumber) ?? [], synthetic: false }))
    }

    const byOrder = new Map<number, Episode[]>()
    const outside: Episode[] = []
    for (const episode of episodes) {
      if (episode.notInOrder) outside.push(episode)
      else byOrder.set(episode.displaySeasonNumber, [...(byOrder.get(episode.displaySeasonNumber) ?? []), episode])
    }

    const list = [...byOrder.entries()]
      .map(([number, list]) => ({ season: orderSeason(s, number, list, files ?? []), episodes: list.sort((a, b) => a.displayEpisodeNumber - b.displayEpisodeNumber), synthetic: true }))
      .sort((a, b) => seasonOrder(a.season, b.season))
    if (outside.length > 0) list.push({ season: orderSeason(s, NOT_IN_ORDER, outside, files ?? []), episodes: outside, synthetic: true })
    return list
  }, [alt, episodes, files, s])
  // Open on the latest season that has episodes (TVDB lists announced seasons before their episodes).
  const latest = (blocks.find((b) => b.season.seasonNumber > 0 && b.season.episodeCount > 0) ?? blocks.find((b) => b.season.seasonNumber > 0))?.season.seasonNumber
  const [open, setOpen] = useState<Set<number> | null>(null)
  const opened = open ?? new Set(latest === undefined ? [] : [latest])
  // TVDB numbers every show absolutely; like Sonarr, only anime shows the column.
  const showAbsolute = s.seriesType === 'anime'
  // One sort and one set of columns for every season of every show, kept like the library's view options.
  const me = useQuery({ queryKey: ['me'], queryFn: auth.me })
  const [prefs, updatePrefs] = useSeasonTablePrefs(me.data?.id)
  const onSort = (key: EpisodeSortKey) => updatePrefs((current) => ({ sort: nextEpisodeSort(current.sort, key) }))

  return (
    <div className="flex flex-col gap-3" data-testid="series-seasons">
      <div className="flex justify-end">
        <SeasonColumnsMenu
          columns={prefs.columns}
          // Hiding the column the tables are sorted on goes back to the season order: no sort on what cannot be seen.
          onChange={(columns) => updatePrefs((current) => ({ columns, sort: sortStillShown(current.sort, columns) ? current.sort : defaultEpisodeSort }))}
        />
      </div>
      {blocks.map(({ season, episodes: list, synthetic }) => (
        <SeasonBlock
          key={season.seasonNumber}
          series={s}
          season={season}
          version={version}
          episodes={list}
          files={fileByEpisode}
          loading={loading}
          showAbsolute={showAbsolute}
          sort={prefs.sort}
          onSort={onSort}
          columns={prefs.columns}
          synthetic={synthetic}
          open={opened.has(season.seasonNumber)}
          onToggleOpen={() =>
            setOpen(() => {
              const next = new Set(opened)
              if (next.has(season.seasonNumber)) next.delete(season.seasonNumber)
              else next.add(season.seasonNumber)
              return next
            })
          }
          onChanged={onChanged}
          onSearch={onSearch}
        />
      ))}
    </div>
  )
}

type SeasonBlockProps = {
  series: Series
  season: Season
  version: SeriesVersion
  episodes: Episode[]
  files: Map<number, EpisodeFile>
  loading: boolean
  showAbsolute: boolean
  sort: EpisodeSort
  onSort: (key: EpisodeSortKey) => void
  columns: EpisodeColumn[]
  /** A season of the series' order (or the episodes it lacks): flags and search go through its episodes. */
  synthetic: boolean
  open: boolean
  onToggleOpen: () => void
  onChanged: () => void
  onSearch: (scope: SeriesSearchScope) => void
}

function SeasonBlock({ series: s, season, version, episodes, files, loading, showAbsolute, sort, onSort, columns, synthetic, open, onToggleOpen, onChanged, onSearch }: SeasonBlockProps) {
  const { t } = useTranslation()
  const state = season.versions.find((v) => v.versionId === version.id)
  const monitored = state?.monitored ?? false
  const outside = season.seasonNumber === NOT_IN_ORDER
  const name = outside ? t('seriesOrder.notInOrderTitle') : season.seasonNumber === 0 ? t('seriesDetail.specials') : t('seriesDetail.season', { number: season.seasonNumber })
  const [aligning, setAligning] = useState(false)
  // A season with episodes off its group, or files from several groups to choose from (no majority yet, or a pin).
  const alignable = !synthetic && !outside && season.seasonNumber > 0 && ((!!state?.seasonGroup && (state.offGroupEpisodes ?? 0) > 0) || !!state?.mixedGroups)
  const toggle = useMutation({
    mutationFn: () =>
      synthetic
        ? seriesApi.setEpisodes(s.id, version.id, episodes.map((e) => e.id), !monitored)
        : seriesApi.setSeason(s.id, version.id, season.seasonNumber, !monitored),
    onSuccess: onChanged,
    onError: (error) => toast.error(errorMessage(error, t('common.unexpectedError'))),
  })

  return (
    <Card className="gap-0 py-0" data-testid="season" data-season={season.seasonNumber}>
      <div className="flex items-center gap-2 px-4 py-3 sm:gap-3">
        <MonitorToggle
          monitored={monitored}
          label={t('seriesDetail.toggleSeason', { number: outside ? name : season.seasonNumber, version: version.name })}
          onToggle={() => toggle.mutate()}
          disabled={toggle.isPending}
        />
        <button type="button" className="flex min-w-0 flex-1 items-center gap-2 text-left" onClick={onToggleOpen} aria-expanded={open}>
          <ChevronRightIcon className={cn('size-4 shrink-0 transition-transform', open && 'rotate-90')} />
          <span className="font-medium">{name}</span>
          <span className="text-xs text-muted-foreground tabular-nums">{t('seriesDetail.episodeCount', { aired: season.airedEpisodeCount, count: season.episodeCount })}</span>
        </button>
        {state && (
          // Stacked on a phone so the size takes no width from the season's name, side by side from sm up.
          <div className="flex flex-col items-end text-xs whitespace-nowrap text-muted-foreground tabular-nums sm:flex-row sm:items-center sm:gap-3">
            <span>{t('seriesDetail.monitoredEpisodes', { count: state.statistics.monitoredEpisodeCount })}</span>
            {state.statistics.sizeOnDisk > 0 && <span data-testid="season-size">{formatBytes(state.statistics.sizeOnDisk)}</span>}
          </div>
        )}
        {state && <EpisodeProgress statistics={state.statistics} monitored={monitored} className="hidden w-36 sm:flex" />}
        {outside && <span className="hidden text-xs text-muted-foreground md:inline">{t('seriesOrder.notInOrderHint')}</span>}
        {alignable && state && (
          <SeasonAlignButton group={state.seasonGroup ?? null} count={state.offGroupEpisodes} season={name} kept={state.keepAsIs} onClick={() => setAligning(true)} />
        )}
        {!outside ? (
          <InteractiveSearchButton iconOnly target={name} onClick={() => onSearch({ kind: 'season', season: season.seasonNumber, label: name, orderSeason: synthetic })} data-testid="season-search" />
        ) : (
          <span className="size-7" aria-hidden="true" />
        )}
      </div>
      {alignable && (
        <SeasonAlignDialog
          seriesId={s.id}
          versionId={version.id}
          versionName={version.name}
          seasonNumber={season.seasonNumber}
          seasonName={name}
          open={aligning}
          onOpenChange={setAligning}
          onDone={onChanged}
        />
      )}
      {open && (
        <div className="border-t">
          {loading ? (
            <div className="flex flex-col gap-2 p-4">
              <Skeleton className="h-5 w-full" />
              <Skeleton className="h-5 w-full" />
            </div>
          ) : episodes.length === 0 ? (
            <p className="p-4 text-sm text-muted-foreground">{t('seriesDetail.noEpisodes')}</p>
          ) : (
            <EpisodeTable
              series={s}
              version={version}
              episodes={episodes}
              files={files}
              showAbsolute={showAbsolute}
              columns={columns}
              sort={sort}
              onSort={onSort}
              onChanged={onChanged}
              onSearch={onSearch}
            />
          )}
        </div>
      )}
    </Card>
  )
}

function EpisodeTable({
  series: s,
  version,
  episodes,
  files,
  showAbsolute,
  columns,
  sort,
  onSort,
  onChanged,
  onSearch,
}: {
  series: Series
  version: SeriesVersion
  episodes: Episode[]
  files: Map<number, EpisodeFile>
  showAbsolute: boolean
  columns: EpisodeColumn[]
  sort: EpisodeSort
  onSort: (key: EpisodeSortKey) => void
  onChanged: () => void
  onSearch: (scope: SeriesSearchScope) => void
}) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [fixing, setFixing] = useState<EpisodeFile | null>(null)
  const [detail, setDetail] = useState<Episode | null>(null)
  // Another order shows its numbers; TVDB's stay visible beside them (releases, Sonarr and history use them).
  const alt = s.ordering !== 'tvdb'
  const tvdbCode = (e: Episode) => episodeCode(e.seasonNumber, e.episodeNumber)
  const shownCode = (e: Episode) => episodeCode(e.displaySeasonNumber, e.displayEpisodeNumber)
  const automatic = useEpisodeAutomaticSearch(s.id, onChanged)
  const searchEpisode = (e: Episode) => onSearch({ kind: 'episode', episodeId: e.id, label: alt && !e.notInOrder ? `${shownCode(e)} (TVDB ${tvdbCode(e)})` : tvdbCode(e) })
  const toggle = useMutation({
    mutationFn: ({ episode, monitored }: { episode: Episode; monitored: boolean }) => seriesApi.setEpisodes(s.id, version.id, [episode.id], monitored),
    // The flag flips at once; the counts follow with the refetch.
    onMutate: ({ episode, monitored }) => {
      queryClient.setQueryData<Episode[]>(['seriesEpisodes', s.id], (current) =>
        current?.map((e) => (e.id === episode.id ? { ...e, versions: e.versions.map((v) => (v.versionId === version.id ? { ...v, monitored } : v)) } : e)),
      )
    },
    onSettled: onChanged,
    onError: (error) => toast.error(errorMessage(error, t('common.unexpectedError'))),
  })

  const contextOf = (e: Episode) => ({ file: files.get(e.id), monitored: e.versions.find((v) => v.versionId === version.id)?.monitored ?? false })
  const sorted = sortEpisodes(episodes, sort, contextOf)
  // Like Sonarr, the absolute number is an anime column.
  const shown = columns.filter((c) => c !== 'absolute' || showAbsolute)
  const head = (key: EpisodeSortKey, label: string, className: string) => (
    <TableHead key={key} className={className} aria-sort={ariaSort(sort.key === key, sort.direction)}>
      <SortButton active={sort.key === key} direction={sort.direction} onClick={() => onSort(key)} testId={`episode-sort-${key}`}>
        {label}
      </SortButton>
    </TableHead>
  )

  return (
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead className="w-10 pl-4" aria-label={t('seriesDetail.columns.monitored')} />
          {head('number', t('seriesDetail.columns.number'), 'w-14')}
          {shown.includes('absolute') && head('absolute', t('seriesDetail.columns.absolute'), columnWidths.absolute)}
          {alt && <TableHead className="w-20" title={t('seriesOrder.tvdbNumbers')}>{t('seriesOrder.tvdbColumn')}</TableHead>}
          {head('title', t('seriesDetail.columns.title'), 'min-w-48')}
          {shown.filter((c) => c !== 'absolute').map((c) => head(c, t(`seriesDetail.columns.${c}`), columnWidths[c]))}
          <TableHead className="w-28 pr-4" aria-label={t('versions.actions')} />
        </TableRow>
      </TableHeader>
      <TableBody>
        {sorted.map((episode) => {
          const { file, monitored } = contextOf(episode)
          return (
            // A click anywhere on the row opens the episode: an empty button laid over the row (its positioned ancestor),
            // the row's own controls stacked above it.
            <TableRow key={episode.id} data-testid="episode" className={cn('relative cursor-pointer', !episode.hasAired && 'text-muted-foreground')}>
              <TableCell className="py-1.5 pl-4">
                <span className="relative z-10 inline-flex">
                  <MonitorToggle
                    monitored={monitored}
                    label={t('seriesDetail.toggleEpisode', { version: version.name })}
                    onToggle={() => toggle.mutate({ episode, monitored: !monitored })}
                    size="sm"
                  />
                </span>
              </TableCell>
              <TableCell className="tabular-nums">{alt && episode.notInOrder ? '—' : episode.displayEpisodeNumber}</TableCell>
              {shown.includes('absolute') && <TableCell className="text-muted-foreground tabular-nums">{episode.displayAbsoluteNumber ?? ''}</TableCell>}
              {alt && (
                <TableCell className="text-xs text-muted-foreground tabular-nums" title={t('seriesOrder.tvdbNumbers')} data-testid="episode-tvdb">
                  {tvdbCode(episode)}
                </TableCell>
              )}
              <TableCell className="max-w-0">
                <button
                  type="button"
                  className="absolute inset-0 outline-none focus-visible:ring-2 focus-visible:ring-ring/50 focus-visible:ring-inset"
                  aria-label={t('episodeDetail.open', { title: episode.title })}
                  onClick={() => setDetail(episode)}
                  data-testid="episode-open"
                />
                <span className="flex min-w-0 items-center gap-1.5">
                  <span className="truncate">{episode.title}</span>
                  {episode.ownedByFilm && (
                    <Link
                      to="/movies/$movieId"
                      params={{ movieId: String(episode.ownedByFilm.movieId) }}
                      className="relative z-10 shrink-0"
                      title={t('seriesFilms.ownedBy', { title: episode.ownedByFilm.title })}
                      data-testid="episode-owned-by-film"
                    >
                      <Badge variant="secondary">
                        <ClapperboardIcon />
                        {t('seriesFilms.film')}
                      </Badge>
                    </Link>
                  )}
                </span>
              </TableCell>
              {shown
                .filter((c) => c !== 'absolute')
                .map((c) => (
                  <EpisodeColumnCell key={c} column={c} episode={episode} file={file} monitored={monitored} />
                ))}
              <TableCell className="py-1 pr-4 whitespace-nowrap text-right">
                <span className="relative z-10 inline-flex items-center">
                {episode.notInOrder && file && (
                  <Button
                    type="button"
                    size="icon"
                    variant="ghost"
                    className="size-7 text-amber-700 dark:text-amber-300"
                    onClick={() => setFixing(file)}
                    aria-label={t('seriesOrder.fix')}
                    title={t('seriesOrder.fix')}
                    data-testid="episode-file-fix"
                  >
                    <WrenchIcon />
                  </Button>
                )}
                {file && !file.linkedFromPath && <EpisodeFileActions seriesId={s.id} file={file} onChanged={onChanged} />}
                {!episode.ownedByFilm && (
                  <>
                    <AutomaticSearchButton
                      iconOnly
                      target={`${tvdbCode(episode)} · ${episode.title}`}
                      onClick={() => automatic.mutate(episode.id)}
                      pending={automatic.isPending && automatic.variables === episode.id}
                      data-testid="episode-auto-search"
                    />
                    <InteractiveSearchButton iconOnly target={`${tvdbCode(episode)} · ${episode.title}`} onClick={() => searchEpisode(episode)} data-testid="episode-search" />
                  </>
                )}
                </span>
              </TableCell>
            </TableRow>
          )
        })}
      </TableBody>
      {fixing && <ReassignFileDialog series={s} file={fixing} onClose={() => setFixing(null)} onDone={onChanged} />}
      {detail && (
        <EpisodeDetailDialog
          series={s}
          version={version}
          episode={episodes.find((e) => e.id === detail.id) ?? detail}
          file={files.get(detail.id)}
          onClose={() => setDetail(null)}
          onInteractiveSearch={detail.ownedByFilm ? undefined : () => searchEpisode(detail)}
          onChanged={onChanged}
        />
      )}
    </Table>
  )
}

const columnWidths: Record<EpisodeColumn, string> = {
  absolute: 'w-16',
  airDate: 'w-32',
  runtime: 'w-20',
  status: 'w-32',
  quality: 'w-40',
  size: 'w-24',
  languages: 'w-28',
  audio: 'w-28',
  video: 'w-24',
  dynamicRange: 'w-24',
  subtitles: 'w-32',
  releaseGroup: 'w-28',
  formats: 'w-40',
  score: 'w-20',
  flags: 'w-28',
  relativePath: 'w-72',
}

const statusStyle: Record<EpisodeStatus, string> = {
  downloaded: 'border-emerald-600/30 bg-emerald-500/15 text-emerald-700 dark:border-emerald-400/30 dark:text-emerald-300',
  missing: 'border-red-600/30 bg-red-500/10 text-red-700 dark:border-red-400/30 dark:text-red-300',
  unmonitored: 'text-muted-foreground',
  unaired: 'text-muted-foreground',
}

/** One optional column of a season table, for the version on screen; empty when the file (or the probe) has nothing. */
function EpisodeColumnCell({ column, episode, file, monitored }: { column: EpisodeColumn; episode: Episode; file: EpisodeFile | undefined; monitored: boolean }) {
  const { t } = useTranslation()
  const muted = (value: string | null | undefined, className = '') => <TableCell className={cn('text-xs text-muted-foreground', className)}>{value || ''}</TableCell>
  switch (column) {
    case 'absolute':
      return null
    case 'airDate':
      return (
        <TableCell className="tabular-nums" title={episode.hasAired ? undefined : t('seriesDetail.unaired')}>
          {episode.airDate ? formatDate(episode.airDate) : t('seriesDetail.tba')}
        </TableCell>
      )
    case 'runtime':
      return muted(episode.runtime ? t('common.minutes', { count: episode.runtime }) : null, 'tabular-nums')
    case 'status': {
      const status = episodeStatus(episode, monitored, file)
      return (
        <TableCell data-testid="episode-status">
          <Badge variant="outline" className={statusStyle[status]}>
            {t(`seriesDetail.status.${status}`)}
          </Badge>
        </TableCell>
      )
    }
    case 'quality':
      return (
        <TableCell>
          <EpisodeQualityCell file={file} episode={episode} />
        </TableCell>
      )
    case 'size':
      return muted(file ? formatBytes(file.size) : null, 'whitespace-nowrap tabular-nums')
    case 'languages':
      return muted(fileLanguages(file).join(', '))
    case 'audio':
      return muted(audioLabel(file), 'whitespace-nowrap')
    case 'video':
      return muted(file?.mediaInfo?.videoCodec, 'whitespace-nowrap')
    case 'dynamicRange':
      return muted(file?.mediaInfo?.videoDynamicRangeType, 'whitespace-nowrap')
    case 'subtitles':
      return muted(file?.mediaInfo?.subtitles.join(', '))
    case 'releaseGroup':
      return muted(file?.releaseGroup, 'max-w-0 truncate')
    case 'formats':
      return muted(file?.customFormats?.join(', '))
    case 'score':
      return muted(file && (file.customFormats?.length || file.customFormatScore) ? (file.customFormatScore > 0 ? `+${file.customFormatScore}` : String(file.customFormatScore)) : null, 'tabular-nums')
    case 'flags':
      return muted(fileFlags(file).map((flag) => t(`search.flags.${flag}`, { defaultValue: flag })).join(', '))
    case 'relativePath':
      return (
        <TableCell className="max-w-0">
          <span className="block truncate font-mono text-xs text-muted-foreground" title={file?.path}>
            {file?.relativePath}
          </span>
        </TableCell>
      )
  }
}

/** The version's file for an episode: its quality (with the order and film-link marks), the path on hover; "missing" once aired without one. */
function EpisodeQualityCell({ file, episode }: { file: EpisodeFile | undefined; episode: Episode }) {
  const { t } = useTranslation()
  if (!file) {
    return episode.hasAired ? <span className="text-xs text-muted-foreground">{t('seriesDetail.missing')}</span> : null
  }

  const shared = file.episodeIds.length > 1
  return (
    <span className="flex min-w-0 items-center gap-1.5" title={file.path} data-testid="episode-file">
      {episode.notInOrder && (
        <Badge variant="outline" className="shrink-0 border-amber-600/30 bg-amber-500/15 text-amber-700 dark:border-amber-400/30 dark:text-amber-300" title={t('seriesOrder.flaggedHint')} data-testid="episode-file-flagged">
          {t('seriesOrder.flagged')}
        </Badge>
      )}
      {file.linkedFromPath && (
        <span className="shrink-0 text-muted-foreground" title={t('seriesFilms.hardlinkOf', { path: file.linkedFromPath })} data-testid="episode-file-film-link">
          <Link2Icon className="size-3.5" />
        </span>
      )}
      <Badge variant="outline" className="border-emerald-600/30 bg-emerald-500/15 text-emerald-700 dark:border-emerald-400/30 dark:text-emerald-300">
        {file.quality}
      </Badge>
      {shared && (
        <Badge variant="secondary" className="shrink-0" title={t('seriesDetail.multiEpisodeHint')}>
          {file.episodes}
        </Badge>
      )}
    </span>
  )
}

/** Which optional columns the season tables show: one setting for every season of every show. */
function SeasonColumnsMenu({ columns, onChange }: { columns: EpisodeColumn[]; onChange: (columns: EpisodeColumn[]) => void }) {
  const { t } = useTranslation()
  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button type="button" size="sm" variant="outline" data-testid="season-columns">
          <Columns3Icon />
          {t('seriesDetail.columnsMenu')}
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="w-64">
        <DropdownMenuLabel className="text-xs font-normal text-muted-foreground">{t('seriesDetail.columnsHint')}</DropdownMenuLabel>
        {episodeColumns.map((column) => (
          <DropdownMenuCheckboxItem
            key={column}
            checked={columns.includes(column)}
            // Keep the declared order whatever the order of the clicks.
            onCheckedChange={() => onChange(episodeColumns.filter((c) => (c === column ? !columns.includes(c) : columns.includes(c))))}
            onSelect={(e) => e.preventDefault()}
            data-testid={`season-column-${column}`}
          >
            {t(`seriesDetail.columns.${column}`)}
          </DropdownMenuCheckboxItem>
        ))}
      </DropdownMenuContent>
    </DropdownMenu>
  )
}

/** Files of the version folder the scan could not attach to episodes: listed with the reason, never touched. */
function UnmatchedFiles({ seriesId, files, onChanged }: { seriesId: number; files: UnmatchedEpisodeFile[]; onChanged: () => void }) {
  const { t } = useTranslation()
  const [assigning, setAssigning] = useState<UnmatchedEpisodeFile | null>(null)
  if (files.length === 0) return null

  return (
    <section className="flex flex-col gap-2" data-testid="unmatched-files">
      <h2 className="text-lg font-semibold tracking-tight">{t('seriesDetail.unmatched.title', { count: files.length })}</h2>
      <p className="max-w-3xl text-xs text-muted-foreground">{t('seriesDetail.unmatched.hint')}</p>
      <div className="overflow-x-auto rounded-lg border">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead className="pl-4">{t('seriesDetail.unmatched.file')}</TableHead>
              <TableHead className="w-24 text-right">{t('seriesDetail.unmatched.size')}</TableHead>
              <TableHead className="w-48">{t('seriesDetail.unmatched.reason')}</TableHead>
              <TableHead>{t('seriesDetail.unmatched.detail')}</TableHead>
              <TableHead className="w-24 pr-4" />
            </TableRow>
          </TableHeader>
          <TableBody>
            {files.map((file) => (
              <TableRow key={file.id}>
                <TableCell className="max-w-0 pl-4">
                  <span className="block truncate font-mono text-xs" title={file.path}>
                    {file.relativePath}
                  </span>
                </TableCell>
                <TableCell className="text-right text-xs text-muted-foreground tabular-nums">{formatBytes(file.size)}</TableCell>
                <TableCell>
                  <Badge variant="outline" className="border-amber-600/30 bg-amber-500/15 text-amber-700 dark:border-amber-400/30 dark:text-amber-300">
                    {t(`seriesDetail.unmatched.reasons.${file.reason}`)}
                  </Badge>
                </TableCell>
                <TableCell className="max-w-0 text-xs text-muted-foreground">
                  <span className="block truncate" title={file.detail ?? undefined}>
                    {file.detail}
                  </span>
                </TableCell>
                <TableCell className="pr-4 text-right">
                  <Button type="button" size="sm" variant="outline" onClick={() => setAssigning(file)} data-testid="unmatched-assign">
                    {t('seriesFiles.assign')}
                  </Button>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>
      {assigning && <AssignUnmatchedDialog seriesId={seriesId} file={assigning} onClose={() => setAssigning(null)} onDone={onChanged} />}
    </section>
  )
}

/** Sonarr's bookmark: filled when monitored for the version on screen. */
function MonitorToggle({ monitored, label, onToggle, disabled, size = 'md' }: { monitored: boolean; label: string; onToggle: () => void; disabled?: boolean; size?: 'sm' | 'md' }) {
  return (
    <button
      type="button"
      aria-pressed={monitored}
      aria-label={label}
      title={label}
      onClick={onToggle}
      disabled={disabled}
      className={cn(
        'inline-flex shrink-0 items-center justify-center rounded-md transition-colors hover:bg-accent focus-visible:outline-2 focus-visible:outline-ring disabled:opacity-50',
        size === 'sm' ? 'size-7' : 'size-8',
        monitored ? 'text-primary' : 'text-muted-foreground',
      )}
      data-testid="monitor-toggle"
    >
      <BookmarkIcon className={cn(size === 'sm' ? 'size-4' : 'size-5', monitored && 'fill-current')} />
    </button>
  )
}

/** The automatic search of the series' (or some episodes') missing episodes: the rows Series wanted lists for it, queued in one go. */
async function searchMissingEpisodes(t: TFunction, seriesId: number, episodeIds?: Set<number>) {
  try {
    const targetIds: number[] = []
    // Wanted pages hold 200 rows at most.
    for (let page = 1, seen = 0; ; page++) {
      const rows = await seriesWanted.list('missing', page, 200, seriesId)
      seen += rows.items.length
      targetIds.push(...rows.items.filter((item) => episodeIds === undefined || episodeIds.has(item.episodeId)).map((item) => item.targetId))
      if (rows.items.length === 0 || seen >= rows.total) break
    }
    if (targetIds.length === 0) {
      toast.info(t('palette.page.nothingMissing'))
      return
    }
    const result = await seriesWanted.search(targetIds)
    toast.success(t('wanted.queued', { count: result.queued }))
  } catch (error) {
    toast.error(errorMessage(error, t('common.unexpectedError')))
  }
}
