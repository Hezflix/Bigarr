import { useMemo, useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useNavigate, useParams } from '@tanstack/react-router'
import { cn } from 'cn'
import { BookmarkIcon, ChevronRightIcon, ListChecksIcon, PlusIcon, RefreshCwIcon, StarIcon, Trash2Icon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import {
  ApiError,
  monitoringOptions,
  quality,
  series as seriesApi,
  slugify,
  type Episode,
  type MonitoringOption,
  type Season,
  type Series,
  type SeriesVersion,
} from '@/api/client'
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
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { formatBytes, formatDate } from '@/i18n'
import { EpisodeProgress } from './EpisodeProgress'

const errorMessage = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback)

export function SeriesDetailPage() {
  const { t } = useTranslation()
  const { seriesId } = useParams({ from: '/app/series/$seriesId' })
  const id = Number(seriesId)
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const detail = useQuery({ queryKey: ['seriesDetail', id], queryFn: () => seriesApi.get(id) })
  const episodes = useQuery({ queryKey: ['seriesEpisodes', id], queryFn: () => seriesApi.episodes(id) })
  const [chosenVersion, setChosenVersion] = useState<number | null>(null)
  const [deleteOpen, setDeleteOpen] = useState(false)

  const invalidate = () => {
    void queryClient.invalidateQueries({ queryKey: ['seriesDetail', id] })
    void queryClient.invalidateQueries({ queryKey: ['seriesEpisodes', id] })
    void queryClient.invalidateQueries({ queryKey: ['series'] })
  }

  const refresh = useMutation({
    mutationFn: () => seriesApi.refresh(id),
    onSuccess: () => {
      toast.success(t('seriesDetail.refreshed'))
      invalidate()
    },
    onError: (error) => toast.error(errorMessage(error, t('common.unexpectedError'))),
  })
  const remove = useMutation({
    mutationFn: () => seriesApi.remove(id),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['series'] })
      void navigate({ to: '/series' })
    },
  })

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
            <span>{t('seriesDetail.order')}</span>
          </div>

          <div className="mt-1 flex flex-wrap gap-2">
            <Button type="button" variant="outline" onClick={() => refresh.mutate()} disabled={refresh.isPending}>
              <RefreshCwIcon className={refresh.isPending ? 'animate-spin' : undefined} />
              {t('seriesDetail.refresh')}
            </Button>
            <Button type="button" variant="destructive" onClick={() => setDeleteOpen(true)} disabled={remove.isPending}>
              <Trash2Icon />
              {t('seriesDetail.delete')}
            </Button>
          </div>
        </div>
      </div>

      <AlertDialog open={deleteOpen} onOpenChange={setDeleteOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>{t('seriesDetail.deleteTitle')}</AlertDialogTitle>
            <AlertDialogDescription>{t('seriesDetail.confirmDelete', { title: s.title })}</AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>{t('common.cancel')}</AlertDialogCancel>
            <AlertDialogAction variant="destructive" onClick={() => remove.mutate()}>
              {t('seriesDetail.delete')}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>

      {version && <VersionBar series={s} version={version} onChoose={setChosenVersion} onChanged={invalidate} />}

      {version && (
        <SeasonList
          series={s}
          version={version}
          episodes={episodes.data}
          loading={episodes.isPending}
          onChanged={invalidate}
        />
      )}
    </>
  )
}

// ---- versions -------------------------------------------------------------------------------------------------------

function VersionBar({ series: s, version, onChoose, onChanged }: { series: Series; version: SeriesVersion; onChoose: (id: number) => void; onChanged: () => void }) {
  const { t } = useTranslation()
  const [monitoringOpen, setMonitoringOpen] = useState(false)
  const [addOpen, setAddOpen] = useState(false)
  const [deleteOpen, setDeleteOpen] = useState(false)

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
        <div className="ml-auto flex gap-2">
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
    </Card>
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
        <Select value={option} onValueChange={(value) => setOption(value as MonitoringOption)}>
          <SelectTrigger className="w-full" aria-label={t('addSeries.monitor')}>
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {monitoringOptions.map((o) => (
              <SelectItem key={o} value={o}>
                {t(`monitoringOption.${o}`)}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
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
  const profiles = useQuery({ queryKey: ['qualityProfiles'], queryFn: quality.profiles, enabled: open })
  const [name, setName] = useState('')
  const [profileId, setProfileId] = useState<number | ''>('')
  const [monitor, setMonitor] = useState<MonitoringOption>('all')
  const profile = profiles.data?.find((p) => p.id === profileId) ?? profiles.data?.[0]
  const effectiveName = name.trim() || (profile ? slugify(profile.name) : '')

  const add = useMutation({
    mutationFn: () => seriesApi.addVersion(s.id, { name: effectiveName, qualityProfileId: profile!.id, monitor }),
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
            <Select value={profile ? String(profile.id) : undefined} onValueChange={(value) => setProfileId(Number(value))}>
              <SelectTrigger id="series-version-new-profile" className="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {profiles.data?.map((p) => (
                  <SelectItem key={p.id} value={String(p.id)}>
                    {p.name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <div className="grid gap-2">
            <Label htmlFor="series-version-new-monitor">{t('addSeries.monitor')}</Label>
            <Select value={monitor} onValueChange={(value) => setMonitor(value as MonitoringOption)}>
              <SelectTrigger id="series-version-new-monitor" className="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {monitoringOptions.map((o) => (
                  <SelectItem key={o} value={o}>
                    {t(`monitoringOption.${o}`)}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
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
  const promoteTo = successor ?? others[0]?.id
  const last = s.versions.length === 1
  const remove = useMutation({
    mutationFn: () => seriesApi.deleteVersion(s.id, version.id, version.isDefault ? promoteTo : undefined),
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
            <Select value={promoteTo ? String(promoteTo) : undefined} onValueChange={(value) => setSuccessor(Number(value))}>
              <SelectTrigger id="series-version-successor" className="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {others.map((v) => (
                  <SelectItem key={v.id} value={String(v.id)}>
                    {v.name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
        )}
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

function SeasonList({ series: s, version, episodes, loading, onChanged }: { series: Series; version: SeriesVersion; episodes: Episode[] | undefined; loading: boolean; onChanged: () => void }) {
  const seasons = useMemo(() => [...s.seasons].sort(seasonOrder), [s.seasons])
  // Open on the latest season that has episodes (TVDB lists announced seasons before their episodes).
  const latest = (seasons.find((season) => season.seasonNumber > 0 && season.episodeCount > 0) ?? seasons.find((season) => season.seasonNumber > 0))?.seasonNumber
  const [open, setOpen] = useState<Set<number>>(() => new Set(latest === undefined ? [] : [latest]))
  const bySeason = useMemo(() => {
    const map = new Map<number, Episode[]>()
    for (const episode of episodes ?? []) map.set(episode.seasonNumber, [...(map.get(episode.seasonNumber) ?? []), episode])
    return map
  }, [episodes])
  // TVDB numbers every show absolutely; like Sonarr, only anime shows the column.
  const showAbsolute = s.seriesType === 'anime'

  return (
    <div className="flex flex-col gap-3" data-testid="series-seasons">
      {seasons.map((season) => (
        <SeasonBlock
          key={season.seasonNumber}
          series={s}
          season={season}
          version={version}
          episodes={bySeason.get(season.seasonNumber) ?? []}
          loading={loading}
          showAbsolute={showAbsolute}
          open={open.has(season.seasonNumber)}
          onToggleOpen={() =>
            setOpen((current) => {
              const next = new Set(current)
              if (next.has(season.seasonNumber)) next.delete(season.seasonNumber)
              else next.add(season.seasonNumber)
              return next
            })
          }
          onChanged={onChanged}
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
  loading: boolean
  showAbsolute: boolean
  open: boolean
  onToggleOpen: () => void
  onChanged: () => void
}

function SeasonBlock({ series: s, season, version, episodes, loading, showAbsolute, open, onToggleOpen, onChanged }: SeasonBlockProps) {
  const { t } = useTranslation()
  const state = season.versions.find((v) => v.versionId === version.id)
  const monitored = state?.monitored ?? false
  const name = season.seasonNumber === 0 ? t('seriesDetail.specials') : t('seriesDetail.season', { number: season.seasonNumber })
  const toggle = useMutation({
    mutationFn: () => seriesApi.setSeason(s.id, version.id, season.seasonNumber, !monitored),
    onSuccess: onChanged,
    onError: (error) => toast.error(errorMessage(error, t('common.unexpectedError'))),
  })

  return (
    <Card className="gap-0 py-0" data-testid="season" data-season={season.seasonNumber}>
      <div className="flex items-center gap-3 px-4 py-3">
        <MonitorToggle
          monitored={monitored}
          label={t('seriesDetail.toggleSeason', { number: season.seasonNumber, version: version.name })}
          onToggle={() => toggle.mutate()}
          disabled={toggle.isPending}
        />
        <button type="button" className="flex min-w-0 flex-1 items-center gap-2 text-left" onClick={onToggleOpen} aria-expanded={open}>
          <ChevronRightIcon className={cn('size-4 shrink-0 transition-transform', open && 'rotate-90')} />
          <span className="font-medium">{name}</span>
          <span className="text-xs text-muted-foreground tabular-nums">{t('seriesDetail.episodeCount', { aired: season.airedEpisodeCount, count: season.episodeCount })}</span>
        </button>
        {state && <span className="text-xs text-muted-foreground tabular-nums">{t('seriesDetail.monitoredEpisodes', { count: state.statistics.monitoredEpisodeCount })}</span>}
        {state && <EpisodeProgress statistics={state.statistics} monitored={monitored} className="hidden w-36 sm:flex" />}
      </div>
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
            <EpisodeTable series={s} version={version} episodes={episodes} showAbsolute={showAbsolute} onChanged={onChanged} />
          )}
        </div>
      )}
    </Card>
  )
}

function EpisodeTable({ series: s, version, episodes, showAbsolute, onChanged }: { series: Series; version: SeriesVersion; episodes: Episode[]; showAbsolute: boolean; onChanged: () => void }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
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

  return (
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead className="w-10 pl-4" aria-label={t('seriesDetail.columns.monitored')} />
          <TableHead className="w-12 text-right">{t('seriesDetail.columns.number')}</TableHead>
          {showAbsolute && <TableHead className="w-14 text-right">{t('seriesDetail.columns.absolute')}</TableHead>}
          <TableHead>{t('seriesDetail.columns.title')}</TableHead>
          <TableHead className="w-32">{t('seriesDetail.columns.airDate')}</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {episodes.map((episode) => {
          const state = episode.versions.find((v) => v.versionId === version.id)
          const monitored = state?.monitored ?? false
          return (
            <TableRow key={episode.id} data-testid="episode" className={cn(!episode.hasAired && 'text-muted-foreground')}>
              <TableCell className="py-1.5 pl-4">
                <MonitorToggle
                  monitored={monitored}
                  label={t('seriesDetail.toggleEpisode', { version: version.name })}
                  onToggle={() => toggle.mutate({ episode, monitored: !monitored })}
                  size="sm"
                />
              </TableCell>
              <TableCell className="text-right tabular-nums">{episode.episodeNumber}</TableCell>
              {showAbsolute && <TableCell className="text-right text-muted-foreground tabular-nums">{episode.absoluteEpisodeNumber ?? ''}</TableCell>}
              <TableCell className="max-w-0">
                <span className="block truncate" title={episode.overview ?? episode.title}>
                  {episode.title}
                </span>
              </TableCell>
              <TableCell className="tabular-nums" title={episode.hasAired ? undefined : t('seriesDetail.unaired')}>
                {episode.airDate ? formatDate(episode.airDate) : t('seriesDetail.tba')}
              </TableCell>
            </TableRow>
          )
        })}
      </TableBody>
    </Table>
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
