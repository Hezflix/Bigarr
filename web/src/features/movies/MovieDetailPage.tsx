import { useEffect, useRef, useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useParams } from '@tanstack/react-router'
import { EyeIcon, EyeOffIcon, FileXIcon, PlusIcon, RefreshCwIcon, SearchIcon, StarIcon, Trash2Icon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ApiError, customFormats, history, movies, quality, rootFolders, search, shadow, type MediaInfo, type MinimumAvailability, type Movie, type Version, type VersionMode } from '@/api/client'
import { Poster } from '@/components/Poster'
import { StatusBadge } from '@/components/StatusBadge'
import { CompareTable } from '@/features/activity/CompareTable'
import { HistoryTable } from '@/features/activity/HistoryTable'
import { extraSummary } from '@/features/import/extraFiles'
import { RenameDialog } from '@/features/movies/RenameDialog'
import { InteractiveSearch } from '@/features/search/InteractiveSearch'
import { toastSearchError, toastSearchResults } from '@/features/search/searchOutcome'
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
import { Checkbox } from '@/components/ui/checkbox'
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle, DialogTrigger } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { formatBytes, formatDate } from '@/i18n'

const availabilities: MinimumAvailability[] = ['announced', 'inCinemas', 'released']

export function MovieDetailPage() {
  const { t } = useTranslation()
  const { movieId } = useParams({ from: '/app/movies/$movieId' })
  const id = Number(movieId)
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const movie = useQuery({
    queryKey: ['movie', id],
    queryFn: () => movies.get(id),
    // Poll while a grab is in flight so the version's status (and the search verdicts once it clears, below) catch
    // up on their own; otherwise a completed download only shows once something else happens to touch this page.
    refetchInterval: (query) => (query.state.data?.versions.some((v) => v.status === 'downloading') ? 5_000 : false),
  })
  const wasDownloading = useRef(false)
  useEffect(() => {
    const downloading = movie.data?.versions.some((v) => v.status === 'downloading') ?? false
    if (wasDownloading.current && !downloading) void queryClient.invalidateQueries({ queryKey: ['search', id] })
    wasDownloading.current = downloading
  }, [movie.data, id, queryClient])
  const [deleteOpen, setDeleteOpen] = useState(false)
  const [exclude, setExclude] = useState(false)
  const [deleteFiles, setDeleteFiles] = useState(false)

  const invalidate = () => {
    void queryClient.invalidateQueries({ queryKey: ['movie', id] })
    void queryClient.invalidateQueries({ queryKey: ['movies'] })
  }
  const afterSearch = () => {
    invalidate()
    void queryClient.invalidateQueries({ queryKey: ['history', id] })
    void queryClient.invalidateQueries({ queryKey: ['queue'] })
    void queryClient.invalidateQueries({ queryKey: ['search', id] })
  }
  const searchAll = useMutation({
    mutationFn: () => search.automatic(id),
    onSuccess: (results) => {
      toastSearchResults(t, results)
      afterSearch()
    },
    onError: (e) => toastSearchError(t, e),
  })

  const refresh = useMutation({
    mutationFn: () => movies.refresh(id),
    onSuccess: invalidate,
    onError: (e) => toast.error(e instanceof ApiError ? e.message : t('common.unexpectedError')),
  })
  const remove = useMutation({
    mutationFn: ({ addImportExclusion, files }: { addImportExclusion: boolean; files: boolean }) => movies.remove(id, addImportExclusion, files),
    onSuccess: async () => {
      setDeleteOpen(false)
      await queryClient.invalidateQueries({ queryKey: ['movies'] })
      await navigate({ to: '/' })
    },
    onError: (e) => toast.error(e instanceof ApiError ? e.message : t('common.unexpectedError')),
  })

  if (movie.isPending) {
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

  if (movie.error) {
    return (
      <Alert variant="destructive">
        <AlertDescription>{t('movie.notFound')}</AlertDescription>
      </Alert>
    )
  }

  const m = movie.data

  return (
    <>
      <div className="flex flex-col gap-6 sm:flex-row" data-testid="movie-header">
        <Poster url={m.posterUrl} title={m.title} size="large" />
        <div className="flex min-w-0 flex-1 flex-col gap-3">
          <div>
            <h1 className="flex flex-wrap items-baseline gap-2 text-2xl font-semibold tracking-tight">
              {m.title}
              {m.year && <span className="text-lg font-normal text-muted-foreground">({m.year})</span>}
            </h1>
            {m.originalTitle && m.originalTitle !== m.title && (
              <p className="text-sm text-muted-foreground">{m.originalTitle}</p>
            )}
          </div>

          <div className="flex flex-wrap items-center gap-2">
            <Badge variant="secondary">{t(`movieStatus.${m.effectiveStatus}`)}</Badge>
            {m.certification && <Badge variant="outline">{m.certification}</Badge>}
            {m.runtime && <Badge variant="outline">{t('common.minutes', { count: m.runtime })}</Badge>}
            {m.genres.map((genre) => (
              <Badge key={genre} variant="outline">
                {genre}
              </Badge>
            ))}
            {m.ratings?.tmdb && <Badge variant="outline">{t('movie.tmdbRating', { value: m.ratings.tmdb.value.toFixed(1) })}</Badge>}
          </div>

          {m.overview && <p className="max-w-3xl text-sm leading-relaxed text-muted-foreground">{m.overview}</p>}

          <div className="flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-muted-foreground">
            <a
              href={`https://www.themoviedb.org/movie/${m.tmdbId}`}
              target="_blank"
              rel="noreferrer"
              className="underline underline-offset-4 hover:text-foreground"
            >
              TMDB {m.tmdbId}
            </a>
            {m.imdbId && (
              <a
                href={`https://www.imdb.com/title/${m.imdbId}/`}
                target="_blank"
                rel="noreferrer"
                className="underline underline-offset-4 hover:text-foreground"
              >
                IMDb {m.imdbId}
              </a>
            )}
            {m.inCinemasDate && <span>{t('movie.inCinemas', { date: formatDate(m.inCinemasDate) })}</span>}
            {m.digitalReleaseDate && <span>{t('movie.digital', { date: formatDate(m.digitalReleaseDate) })}</span>}
            {m.physicalReleaseDate && <span>{t('movie.physical', { date: formatDate(m.physicalReleaseDate) })}</span>}
          </div>

          <div className="mt-1 flex flex-wrap gap-2">
            <Button type="button" onClick={() => searchAll.mutate()} disabled={searchAll.isPending || m.versions.length === 0} data-testid="search-all-versions">
              <SearchIcon className={searchAll.isPending ? 'animate-pulse' : undefined} />
              {searchAll.isPending ? t('search.searching') : t('movie.searchAll')}
            </Button>
            <Button type="button" variant="outline" onClick={() => refresh.mutate()} disabled={refresh.isPending}>
              <RefreshCwIcon className={refresh.isPending ? 'animate-spin' : undefined} />
              {t('movie.refresh')}
            </Button>
            <RenameDialog movie={m} onRenamed={invalidate} />
            <Button type="button" variant="destructive" onClick={() => setDeleteOpen(true)} disabled={remove.isPending}>
              <Trash2Icon />
              {t('movie.delete')}
            </Button>
          </div>
        </div>
      </div>

      <AlertDialog open={deleteOpen} onOpenChange={setDeleteOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>{t('movie.deleteTitle')}</AlertDialogTitle>
            <AlertDialogDescription>{t('movie.confirmDelete', { title: m.title })}</AlertDialogDescription>
          </AlertDialogHeader>
          <div className="flex flex-col gap-3">
            <div className="flex items-center gap-2">
              <Checkbox id="delete-exclude" checked={exclude} onCheckedChange={(checked) => setExclude(checked === true)} />
              <Label htmlFor="delete-exclude" className="font-normal">
                {t('movie.confirmExclude')}
              </Label>
            </div>
            {m.versions.some((v) => v.file) && (
              <div className="flex items-start gap-2">
                <Checkbox id="delete-files" checked={deleteFiles} onCheckedChange={(checked) => setDeleteFiles(checked === true)} className="mt-0.5" data-testid="delete-files" />
                <div className="grid gap-0.5">
                  <Label htmlFor="delete-files" className="font-normal">
                    {t('movie.deleteFiles')}
                  </Label>
                  <span className="text-xs text-muted-foreground">{t('movie.deleteFilesHint')}</span>
                </div>
              </div>
            )}
          </div>
          <AlertDialogFooter>
            <AlertDialogCancel>{t('common.cancel')}</AlertDialogCancel>
            <AlertDialogAction
              onClick={(event) => {
                event.preventDefault()
                remove.mutate({ addImportExclusion: exclude, files: deleteFiles })
              }}
              disabled={remove.isPending}
            >
              {t('movie.delete')}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>

      <div className="flex flex-wrap items-center gap-3">
        <h2 className="text-xl font-semibold tracking-tight">{t('versions.title')}</h2>
        <AddVersionDialog movie={m} onAdded={invalidate} />
      </div>
      <VersionsTable movie={m} onChanged={invalidate} onSearched={afterSearch} />
      <InteractiveSearch movie={m} />
      <MovieComparison tmdbId={m.tmdbId} />
      <MovieHistory movieId={m.id} />
    </>
  )
}

/**
 * Shadow mode, for this film: what the followed Radarrs grabbed next to what Bigarr would have grabbed, the rows of
 * Activity → Compare without the film column. Shown when shadow mode is on, or when there is something to compare.
 */
function MovieComparison({ tmdbId }: { tmdbId: number }) {
  const { t } = useTranslation()
  const compare = useQuery({
    queryKey: ['compare', 'movie', tmdbId],
    queryFn: () => shadow.compare({ tmdbId, days: 0, windowHours: 24, page: 1, pageSize: 100 }),
  })
  const data = compare.data
  if (!data || (!data.shadowEnabled && data.rows.length === 0)) return null

  return (
    <section className="flex flex-col gap-3" data-testid="movie-compare">
      <div className="flex flex-wrap items-center gap-3">
        <h2 className="text-xl font-semibold tracking-tight">{t('compare.filmTitle')}</h2>
        <Link to="/activity/compare" className="text-sm text-muted-foreground underline-offset-4 hover:underline">
          {t('nav.compare')}
        </Link>
      </div>
      <p className="max-w-3xl text-sm text-muted-foreground">{t('compare.filmHint')}</p>
      {data.rows.length === 0 ? <p className="text-sm text-muted-foreground">{t('compare.filmEmpty')}</p> : <CompareTable rows={data.rows} showMovie={false} />}
    </section>
  )
}

/** The journal of this film: why each file is there (blueprint §3.5). */
function MovieHistory({ movieId }: { movieId: number }) {
  const { t } = useTranslation()
  const events = useQuery({ queryKey: ['history', movieId], queryFn: () => history.list({ movieId, limit: 100 }) })

  return (
    <section className="flex flex-col gap-3" data-testid="movie-history">
      <h2 className="text-xl font-semibold tracking-tight">{t('movie.history')}</h2>
      {events.data && events.data.length === 0 && <p className="text-sm text-muted-foreground">{t('history.empty')}</p>}
      {events.data && events.data.length > 0 && <HistoryTable events={events.data} showMovie={false} />}
    </section>
  )
}

/** "x265 · DV HDR10 · TrueHD Atmos 7.1 · 3840×2160": what the probe read, when it ran. */
function mediaInfoSummary(info: MediaInfo): string {
  const audio = [info.audioCodec, info.audioChannels !== null ? info.audioChannels.toFixed(1) : null].filter(Boolean).join(' ')
  return [info.videoCodec, info.videoDynamicRangeType, audio || null, info.width && info.height ? `${info.width}×${info.height}` : null].filter(Boolean).join(' · ')
}

function VersionsTable({ movie, onChanged, onSearched }: { movie: Movie; onChanged: () => void; onSearched: () => void }) {
  const { t } = useTranslation()
  const [pendingDelete, setPendingDelete] = useState<Version | null>(null)
  const [pendingFileDelete, setPendingFileDelete] = useState<Version | null>(null)
  const fail = (e: unknown) => toast.error(e instanceof ApiError ? e.message : t('common.unexpectedError'))

  const searchOne = useMutation({
    mutationFn: (v: Version) => search.automatic(movie.id, v.id),
    onSuccess: (results) => {
      toastSearchResults(t, results)
      onSearched()
    },
    onError: (e) => toastSearchError(t, e),
  })

  const deleteFile = useMutation({
    mutationFn: (v: Version) => movies.deleteFile(movie.id, v.id),
    onSuccess: () => {
      setPendingFileDelete(null)
      toast.success(t('versions.fileDeleted'))
      onChanged()
    },
    onError: (e) => {
      setPendingFileDelete(null)
      fail(e)
    },
  })

  const toggle = useMutation({
    mutationFn: (v: Version) => movies.updateVersion(movie.id, v.id, { monitored: !v.monitored }),
    onSuccess: onChanged,
    onError: fail,
  })
  const promote = useMutation({ mutationFn: (v: Version) => movies.promote(movie.id, v.id), onSuccess: onChanged, onError: fail })
  const remove = useMutation({
    mutationFn: (v: Version) => {
      let promoteId: number | undefined
      if (v.isDefault && movie.versions.length > 1) {
        const candidates = movie.versions.filter((o) => o.id !== v.id && o.mode === 'managed')
        if (candidates.length === 0) throw new ApiError(409, t('versions.noManagedCandidate'))
        promoteId = candidates[0].id
      }
      return movies.deleteVersion(movie.id, v.id, promoteId)
    },
    onSuccess: () => {
      setPendingDelete(null)
      onChanged()
    },
    onError: (e) => {
      setPendingDelete(null)
      fail(e)
    },
  })

  return (
    <>
      <div className="overflow-x-auto rounded-lg border">
        <Table data-testid="versions-table">
          <TableHeader>
            <TableRow>
              <TableHead>{t('versions.version')}</TableHead>
              <TableHead>{t('versions.mode')}</TableHead>
              <TableHead>{t('versions.profile')}</TableHead>
              <TableHead>{t('versions.minAvailability')}</TableHead>
              <TableHead>{t('versions.status')}</TableHead>
              <TableHead>{t('versions.file')}</TableHead>
              <TableHead>{t('versions.folder')}</TableHead>
              <TableHead className="text-right">{t('versions.actions')}</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {movie.versions.map((v) => (
              <TableRow key={v.id}>
                <TableCell className="align-top">
                  <span className="flex items-center gap-1 font-medium">
                    {v.isDefault && (
                      <Tooltip>
                        <TooltipTrigger asChild>
                          <StarIcon className="size-3.5 fill-current text-amber-500" aria-label={t('versions.default')} />
                        </TooltipTrigger>
                        <TooltipContent>{t('versions.default')}</TooltipContent>
                      </Tooltip>
                    )}
                    {v.name}
                  </span>
                  {v.constraints?.edition && (
                    <span className="block text-xs text-muted-foreground">{t('versions.edition', { value: v.constraints.edition })}</span>
                  )}
                  {v.constraints?.releaseGroup && (
                    <span className="block text-xs text-muted-foreground">{t('versions.group', { value: v.constraints.releaseGroup })}</span>
                  )}
                </TableCell>
                <TableCell className="align-top">{v.mode === 'managed' ? t('versions.managed') : t('versions.oneShot')}</TableCell>
                <TableCell className="align-top">{v.qualityProfile}</TableCell>
                <TableCell className="align-top">{t(`availability.${v.minimumAvailability}`)}</TableCell>
                <TableCell className="align-top">
                  <StatusBadge status={v.status} />
                </TableCell>
                <TableCell className="align-top">
                  {v.file ? (
                    <>
                      <span className="block whitespace-nowrap">
                        {v.file.quality}
                        {v.file.isRepack ? ` ${t('versions.repack')}` : ''} · {formatBytes(v.file.size)}
                      </span>
                      <span className="block max-w-[22ch] truncate text-xs text-muted-foreground" title={v.file.path}>
                        {v.file.relativePath}
                      </span>
                      <span className="block text-xs text-muted-foreground" data-testid="media-info">
                        {v.file.mediaInfo ? mediaInfoSummary(v.file.mediaInfo) : t('versions.mediaInfoMissing')}
                      </span>
                      {v.file.extras.length > 0 && (
                        <ul className="mt-1 grid gap-0.5 text-xs text-muted-foreground" aria-label={t('versions.extras', { count: v.file.extras.length })} data-testid="extra-files">
                          {v.file.extras.map((extra) => (
                            <li key={extra.id}>
                              <span className="block max-w-[22ch] truncate" title={extra.path}>
                                {extra.relativePath}
                              </span>
                              <span className="block">{extraSummary(extra, t)}</span>
                            </li>
                          ))}
                        </ul>
                      )}
                      <span className="mt-1 flex flex-wrap items-center gap-1">
                        {v.cutoffMet !== null && (
                          <Badge
                            variant="outline"
                            className={
                              v.cutoffMet
                                ? 'border-emerald-600/30 bg-emerald-500/15 text-emerald-700 dark:border-emerald-400/30 dark:text-emerald-300'
                                : 'border-amber-600/30 bg-amber-500/15 text-amber-700 dark:border-amber-400/30 dark:text-amber-300'
                            }
                          >
                            {v.cutoffMet ? t('versions.cutoffMet') : t('versions.upgradable')}
                          </Badge>
                        )}
                        {v.customFormats?.map((format) => (
                          <Badge key={format} variant="outline" className="border-violet-600/30 bg-violet-500/10 text-violet-700 dark:border-violet-400/30 dark:text-violet-300">
                            {format}
                          </Badge>
                        ))}
                        {v.customFormatScore !== null && <span className="text-xs text-muted-foreground">{t('versions.score', { score: v.customFormatScore })}</span>}
                      </span>
                    </>
                  ) : (
                    <span className="text-muted-foreground">{t('common.none')}</span>
                  )}
                </TableCell>
                <TableCell className="max-w-[20ch] truncate align-top text-xs text-muted-foreground" title={v.path}>
                  {v.folderName}
                </TableCell>
                <TableCell className="align-top">
                  <div className="flex items-center justify-end gap-1">
                    <Tooltip>
                      <TooltipTrigger asChild>
                        <Button
                          type="button"
                          variant="ghost"
                          size="icon"
                          onClick={() => searchOne.mutate(v)}
                          disabled={searchOne.isPending || v.status === 'downloading'}
                          aria-label={t('search.autoSearch')}
                          data-testid={`search-version-${v.id}`}
                        >
                          <SearchIcon className={searchOne.isPending && searchOne.variables?.id === v.id ? 'animate-pulse' : undefined} />
                        </Button>
                      </TooltipTrigger>
                      <TooltipContent>{v.status === 'downloading' ? t('search.grabBlocked') : t('search.autoSearchHint')}</TooltipContent>
                    </Tooltip>

                    <Tooltip>
                      <TooltipTrigger asChild>
                        <Button
                          type="button"
                          variant="ghost"
                          size="icon"
                          onClick={() => toggle.mutate(v)}
                          disabled={toggle.isPending}
                          aria-label={v.monitored ? t('versions.unmonitor') : t('versions.monitor')}
                        >
                          {v.monitored ? <EyeIcon /> : <EyeOffIcon className="text-muted-foreground" />}
                        </Button>
                      </TooltipTrigger>
                      <TooltipContent>{v.monitored ? t('versions.unmonitor') : t('versions.monitor')}</TooltipContent>
                    </Tooltip>

                    {!v.isDefault && v.mode === 'managed' && (
                      <Tooltip>
                        <TooltipTrigger asChild>
                          <Button
                            type="button"
                            variant="ghost"
                            size="icon"
                            onClick={() => promote.mutate(v)}
                            disabled={promote.isPending}
                            aria-label={t('versions.promote')}
                          >
                            <StarIcon />
                          </Button>
                        </TooltipTrigger>
                        <TooltipContent>{t('versions.promote')}</TooltipContent>
                      </Tooltip>
                    )}

                    {v.file && (
                      <Tooltip>
                        <TooltipTrigger asChild>
                          <Button type="button" variant="ghost" size="icon" onClick={() => setPendingFileDelete(v)} aria-label={t('versions.deleteFile')} data-testid="delete-file">
                            <FileXIcon />
                          </Button>
                        </TooltipTrigger>
                        <TooltipContent>{t('versions.deleteFile')}</TooltipContent>
                      </Tooltip>
                    )}

                    <Tooltip>
                      <TooltipTrigger asChild>
                        <Button
                          type="button"
                          variant="ghost"
                          size="icon"
                          className="text-destructive hover:text-destructive"
                          onClick={() => setPendingDelete(v)}
                          aria-label={t('versions.delete')}
                        >
                          <Trash2Icon />
                        </Button>
                      </TooltipTrigger>
                      <TooltipContent>{t('versions.delete')}</TooltipContent>
                    </Tooltip>
                  </div>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>

      <AlertDialog open={pendingFileDelete !== null} onOpenChange={(open) => !open && setPendingFileDelete(null)}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>{t('versions.deleteFileTitle')}</AlertDialogTitle>
            <AlertDialogDescription>{t('versions.confirmDeleteFile', { name: pendingFileDelete?.name ?? '' })}</AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>{t('common.cancel')}</AlertDialogCancel>
            <AlertDialogAction
              onClick={(event) => {
                event.preventDefault()
                if (pendingFileDelete) deleteFile.mutate(pendingFileDelete)
              }}
              disabled={deleteFile.isPending}
              data-testid="delete-file-confirm"
            >
              {t('versions.deleteFile')}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>

      <AlertDialog open={pendingDelete !== null} onOpenChange={(open) => !open && setPendingDelete(null)}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>{t('versions.deleteTitle')}</AlertDialogTitle>
            <AlertDialogDescription>{t('versions.confirmDelete', { name: pendingDelete?.name ?? '' })}</AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>{t('common.cancel')}</AlertDialogCancel>
            <AlertDialogAction
              onClick={(event) => {
                event.preventDefault()
                if (pendingDelete) remove.mutate(pendingDelete)
              }}
              disabled={remove.isPending}
            >
              {t('versions.delete')}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </>
  )
}

function AddVersionDialog({ movie, onAdded }: { movie: Movie; onAdded: () => void }) {
  const { t } = useTranslation()
  const [open, setOpen] = useState(false)
  const profiles = useQuery({ queryKey: ['qualityProfiles'], queryFn: quality.profiles })
  const roots = useQuery({ queryKey: ['rootFolders'], queryFn: rootFolders.list })
  const [name, setName] = useState('')
  const [mode, setMode] = useState<VersionMode>('managed')
  const [profileId, setProfileId] = useState<number>(movie.versions[0]?.qualityProfileId ?? 0)
  const [rootId, setRootId] = useState<number>(movie.defaultRootFolderId)
  const [availability, setAvailability] = useState<MinimumAvailability>('released')
  const [edition, setEdition] = useState('')
  const [group, setGroup] = useState('')
  const [required, setRequired] = useState<number[]>([])
  const [forbidden, setForbidden] = useState<number[]>([])
  const [searchOnAdd, setSearchOnAdd] = useState(true)
  const formats = useQuery({ queryKey: ['customFormats'], queryFn: customFormats.list, enabled: open })
  const hasConstraints = edition.trim() || group.trim() || required.length > 0 || forbidden.length > 0

  const add = useMutation({
    mutationFn: () =>
      movies.addVersion(movie.id, {
        name: name.trim(),
        mode,
        qualityProfileId: profileId,
        rootFolderId: rootId,
        monitored: true,
        minimumAvailability: availability,
        searchOnAdd,
        constraints: hasConstraints
          ? {
              edition: edition.trim() || null,
              releaseGroup: group.trim() || null,
              requiredCustomFormatIds: required.length > 0 ? required : null,
              forbiddenCustomFormatIds: forbidden.length > 0 ? forbidden : null,
            }
          : null,
      }),
    onSuccess: () => {
      setOpen(false)
      setName('')
      setEdition('')
      setGroup('')
      setRequired([])
      setForbidden([])
      onAdded()
    },
  })

  const toggle = (list: number[], set: (v: number[]) => void, id: number) => set(list.includes(id) ? list.filter((x) => x !== id) : [...list, id])

  const submit = (e: FormEvent) => {
    e.preventDefault()
    add.mutate()
  }

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger asChild>
        <Button type="button" variant="outline" size="sm" data-testid="add-version">
          <PlusIcon />
          {t('versions.add')}
        </Button>
      </DialogTrigger>
      <DialogContent className="sm:max-w-2xl">
        <form onSubmit={submit}>
          <DialogHeader>
            <DialogTitle>{t('versions.new')}</DialogTitle>
          </DialogHeader>

          <div className="my-6 grid gap-4 sm:grid-cols-2">
            <div className="grid grid-cols-1 gap-2">
              <Label htmlFor="version-name">{t('versions.name')}</Label>
              <Input
                id="version-name"
                value={name}
                onChange={(e) => setName(e.target.value)}
                placeholder={t('versions.namePlaceholder')}
                autoFocus
              />
            </div>

            <div className="grid grid-cols-1 gap-2">
              <Label htmlFor="version-mode">{t('versions.mode')}</Label>
              <Select value={mode} onValueChange={(value) => setMode(value as VersionMode)}>
                <SelectTrigger id="version-mode" className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="managed">{t('versions.modeManaged')}</SelectItem>
                  <SelectItem value="oneShot">{t('versions.modeOneShot')}</SelectItem>
                </SelectContent>
              </Select>
            </div>

            <div className="grid grid-cols-1 gap-2">
              <Label htmlFor="version-profile">{t('addMovie.qualityProfile')}</Label>
              <Select value={String(profileId)} onValueChange={(value) => setProfileId(Number(value))}>
                <SelectTrigger id="version-profile" className="w-full">
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

            <div className="grid grid-cols-1 gap-2">
              <Label htmlFor="version-root">{t('addMovie.rootFolder')}</Label>
              <Select value={String(rootId)} onValueChange={(value) => setRootId(Number(value))}>
                <SelectTrigger id="version-root" className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {roots.data?.map((r) => (
                    <SelectItem key={r.id} value={String(r.id)}>
                      {r.path}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>

            <div className="grid grid-cols-1 gap-2">
              <Label htmlFor="version-availability">{t('addMovie.minimumAvailability')}</Label>
              <Select value={availability} onValueChange={(value) => setAvailability(value as MinimumAvailability)}>
                <SelectTrigger id="version-availability" className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {availabilities.map((a) => (
                    <SelectItem key={a} value={a}>
                      {t(`availability.${a}`)}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>

            <div className="grid grid-cols-1 gap-2">
              <Label htmlFor="version-edition">{t('versions.editionConstraint')}</Label>
              <Input id="version-edition" value={edition} onChange={(e) => setEdition(e.target.value)} placeholder="director'?s cut" />
            </div>

            <div className="grid grid-cols-1 gap-2">
              <Label htmlFor="version-group">{t('versions.releaseGroup')}</Label>
              <Input id="version-group" value={group} onChange={(e) => setGroup(e.target.value)} placeholder="FraMeSToR" />
            </div>

            <div className="flex items-center gap-2 sm:col-span-2">
              <Checkbox id="version-search" checked={searchOnAdd} onCheckedChange={(checked) => setSearchOnAdd(checked === true)} data-testid="version-search-on-add" />
              <Label htmlFor="version-search" className="font-normal">
                {t('addMovie.searchOnAdd')}
              </Label>
            </div>

            {formats.data && formats.data.length > 0 && (
              <>
                <fieldset className="grid grid-cols-1 gap-2">
                  <legend className="text-sm font-medium">{t('versions.requiredFormats')}</legend>
                  <div className="flex flex-wrap gap-x-4 gap-y-2" data-testid="required-formats">
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
                  <div className="flex flex-wrap gap-x-4 gap-y-2" data-testid="forbidden-formats">
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

          {add.error && (
            <Alert variant="destructive" className="mb-4">
              <AlertDescription>{add.error instanceof ApiError ? add.error.message : t('common.unexpectedError')}</AlertDescription>
            </Alert>
          )}

          <DialogFooter className="items-center sm:justify-between">
            <Link to="/settings/library" className="text-xs text-muted-foreground underline underline-offset-4 hover:text-foreground">
              {t('versions.manageRootFolders')}
            </Link>
            <div className="flex gap-2">
              <Button type="button" variant="ghost" onClick={() => setOpen(false)}>
                {t('common.cancel')}
              </Button>
              <Button type="submit" data-testid="add-version-submit" disabled={add.isPending || !name.trim()}>
                {t('versions.create')}
              </Button>
            </div>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
