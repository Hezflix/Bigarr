import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { ClapperboardIcon, Link2Icon, Link2OffIcon, PlusIcon, TriangleAlertIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ApiError, profilesFor, quality, rootFolders, seriesFilms, type SeriesFilm } from '@/api/client'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'

/**
 * Specials that are films (S8): candidates detected among the specials, with a one-click "Add as film", and the
 * linked films with where they sit in the show and whether the special is hardlinked in each version.
 */
export function SeriesFilmsSection({ seriesId, onChanged }: { seriesId: number; onChanged: () => void }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const list = useQuery({ queryKey: ['seriesFilms', seriesId], queryFn: () => seriesFilms.list(seriesId) })
  const [adding, setAdding] = useState<SeriesFilm | null>(null)
  const refresh = () => {
    void queryClient.invalidateQueries({ queryKey: ['seriesFilms', seriesId] })
    onChanged()
  }
  const fail = (e: unknown) => toast.error(e instanceof ApiError ? e.message : t('common.unexpectedError'))
  const autoAdd = useMutation({ mutationFn: (value: boolean) => seriesFilms.setAutoAdd(value), onSuccess: refresh, onError: fail })
  const unlink = useMutation({
    mutationFn: (film: SeriesFilm) => seriesFilms.remove(seriesId, film.episodeId),
    onSuccess: (_, film) => {
      toast.success(t('seriesFilms.unlinked', { label: film.label }))
      refresh()
    },
    onError: fail,
  })

  if (!list.data || list.data.films.length === 0) return null

  return (
    <section className="flex flex-col gap-2" data-testid="series-films">
      <div className="flex flex-wrap items-end justify-between gap-2">
        <div className="flex flex-col gap-1">
          <h2 className="text-lg font-semibold tracking-tight">{t('seriesFilms.title', { count: list.data.films.length })}</h2>
          <p className="max-w-3xl text-xs text-muted-foreground">
            {t('seriesFilms.hint')}{' '}
            <Link to="/library/specials-films" search={{}} className="underline underline-offset-4 hover:text-foreground">
              {t('seriesFilms.reviewAll')}
            </Link>
          </p>
        </div>
        <div className="flex items-center gap-2">
          <Checkbox id="series-films-auto" checked={list.data.autoAdd} onCheckedChange={(checked) => autoAdd.mutate(checked === true)} disabled={autoAdd.isPending} />
          <Label htmlFor="series-films-auto" className="text-xs font-normal">
            {t('seriesFilms.autoAdd')}
          </Label>
        </div>
      </div>
      <div className="overflow-x-auto rounded-lg border">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead className="w-20 pl-4">{t('seriesFilms.columns.special')}</TableHead>
              <TableHead className="w-[34%]">{t('seriesFilms.columns.title')}</TableHead>
              <TableHead className="w-36">{t('seriesFilms.columns.placement')}</TableHead>
              <TableHead className="w-24">{t('seriesFilms.columns.runtime')}</TableHead>
              <TableHead>{t('seriesFilms.columns.film')}</TableHead>
              <TableHead className="w-32 pr-4" />
            </TableRow>
          </TableHeader>
          <TableBody>
            {list.data.films.map((film) => (
              <TableRow key={film.episodeId} data-testid="series-film">
                <TableCell className="pl-4 tabular-nums">{film.label}</TableCell>
                <TableCell className="whitespace-normal">
                  <span className="block" title={film.title}>
                    {film.title}
                  </span>
                  <span className="flex flex-wrap items-center gap-1 pt-0.5">
                    {film.confidence && (
                      <Badge variant={film.confidence === 'flagged' ? 'secondary' : 'outline'} title={film.evidence.join(' · ')}>
                        {t(`seriesFilms.confidence.${film.confidence}`)}
                      </Badge>
                    )}
                    {film.airDate && <span className="text-xs text-muted-foreground tabular-nums">{film.airDate.slice(0, 4)}</span>}
                  </span>
                </TableCell>
                <TableCell className="text-sm">
                  {film.placementLabel ? (
                    <span title={t(`seriesFilms.placementSource.${film.placementSource}`)}>{film.placementLabel}</span>
                  ) : (
                    <span className="text-muted-foreground">—</span>
                  )}
                </TableCell>
                <TableCell className="text-sm tabular-nums">{film.runtime ? t('seriesFilms.minutes', { count: film.runtime }) : '—'}</TableCell>
                <TableCell className="text-sm">
                  <FilmState film={film} />
                </TableCell>
                <TableCell className="py-1 pr-4 text-right whitespace-nowrap">
                  {film.link ? (
                    <Button type="button" size="sm" variant="ghost" onClick={() => unlink.mutate(film)} disabled={unlink.isPending} title={t('seriesFilms.unlinkHint')}>
                      <Link2OffIcon />
                      {t('seriesFilms.unlink')}
                    </Button>
                  ) : (
                    <Button type="button" size="sm" variant="outline" onClick={() => setAdding(film)} disabled={!film.tmdbId} data-testid="series-film-add">
                      <PlusIcon />
                      {t('seriesFilms.add')}
                    </Button>
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>
      {adding && <AddFilmDialog seriesId={seriesId} film={adding} onClose={() => setAdding(null)} onDone={refresh} />}
    </section>
  )
}

/** Linked: the film, and per version whether the special is its hardlink yet. Not linked: whether the film is already a film of the library. */
function FilmState({ film }: { film: SeriesFilm }) {
  const { t } = useTranslation()
  if (!film.link) {
    if (film.existingMovieId) {
      return (
        <Link to="/movies/$movieId" params={{ movieId: String(film.existingMovieId) }} className="text-muted-foreground underline underline-offset-4 hover:text-foreground">
          {t('seriesFilms.inLibrary')}
        </Link>
      )
    }

    return <span className="text-muted-foreground">{film.tmdbId ? t('seriesFilms.notAdded') : t('seriesFilms.noTmdb')}</span>
  }

  const link = film.link
  const wanted = link.versions.filter((v) => v.monitored)
  return (
    <span className="flex flex-col gap-0.5">
      <Link to="/movies/$movieId" params={{ movieId: String(link.movieId) }} className="flex items-center gap-1 font-medium underline-offset-4 hover:underline">
        <ClapperboardIcon className="size-3.5 shrink-0" />
        <span className="truncate">{link.movieYear ? `${link.movieTitle} (${link.movieYear})` : link.movieTitle}</span>
        {link.autoAdded && <Badge variant="outline">{t('seriesFilms.autoAdded')}</Badge>}
      </Link>
      {link.linkError ? (
        <span className="flex items-center gap-1 text-xs text-destructive" title={link.linkError}>
          <TriangleAlertIcon className="size-3.5 shrink-0" />
          {t('seriesFilms.linkFailed')}
        </span>
      ) : !link.movieHasFile ? (
        <span className="text-xs text-muted-foreground">{t('seriesFilms.waitingForFilm')}</span>
      ) : (
        wanted.map((v) => (
          <span key={v.versionId} className="flex items-center gap-1 text-xs text-muted-foreground" title={v.relativePath ?? undefined}>
            <Link2Icon className="size-3.5 shrink-0" />
            {v.linked ? t('seriesFilms.linkedIn', { version: v.versionName }) : t('seriesFilms.pendingIn', { version: v.versionName })}
          </span>
        ))
      )}
    </span>
  )
}

function AddFilmDialog({ seriesId, film, onClose, onDone }: { seriesId: number; film: SeriesFilm; onClose: () => void; onDone: () => void }) {
  const { t } = useTranslation()
  const roots = useQuery({ queryKey: ['rootFolders'], queryFn: rootFolders.list })
  const profiles = useQuery({ queryKey: ['qualityProfiles'], queryFn: quality.profiles, select: profilesFor('movies') })
  const filmRoots = (roots.data ?? []).filter((r) => r.kind === 'movies')
  const defaultProfile = profiles.data?.find((p) => p.isSystem && p.name === '1080p') ?? profiles.data?.[0]
  const [rootId, setRootId] = useState<string | null>(null)
  const [profileId, setProfileId] = useState<string | null>(null)
  const [search, setSearch] = useState(true)
  const root = rootId ?? (filmRoots[0] ? String(filmRoots[0].id) : '')
  const profile = profileId ?? (defaultProfile ? String(defaultProfile.id) : '')
  const add = useMutation({
    mutationFn: () => seriesFilms.add(seriesId, { episodeId: film.episodeId, rootFolderId: Number(root), qualityProfileId: Number(profile), searchOnAdd: search }),
    onSuccess: () => {
      toast.success(t('seriesFilms.added', { title: film.title }))
      onDone()
      onClose()
    },
  })

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>{t('seriesFilms.addTitle', { label: film.label })}</DialogTitle>
          <DialogDescription>{film.existingMovieId ? t('seriesFilms.addExisting', { title: film.title }) : t('seriesFilms.addHint', { title: film.title })}</DialogDescription>
        </DialogHeader>
        {!film.existingMovieId && (
          <div className="grid gap-3">
            <div className="grid gap-1.5">
              <Label htmlFor="series-film-root">{t('seriesFilms.rootFolder')}</Label>
              <Select value={root} onValueChange={setRootId}>
                <SelectTrigger id="series-film-root" className="w-full">
                  <SelectValue placeholder={t('seriesFilms.noFilmRoot')} />
                </SelectTrigger>
                <SelectContent>
                  {filmRoots.map((r) => (
                    <SelectItem key={r.id} value={String(r.id)}>
                      {r.path}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <div className="grid gap-1.5">
              <Label htmlFor="series-film-profile">{t('seriesFilms.profile')}</Label>
              <Select value={profile} onValueChange={setProfileId}>
                <SelectTrigger id="series-film-profile" className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {(profiles.data ?? []).map((p) => (
                    <SelectItem key={p.id} value={String(p.id)}>
                      {p.name}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <div className="flex items-center gap-2">
              <Checkbox id="series-film-search" checked={search} onCheckedChange={(checked) => setSearch(checked === true)} />
              <Label htmlFor="series-film-search" className="font-normal">
                {t('seriesFilms.searchOnAdd')}
              </Label>
            </div>
          </div>
        )}
        {add.error && (
          <Alert variant="destructive">
            <AlertDescription>{add.error instanceof ApiError ? add.error.message : t('common.unexpectedError')}</AlertDescription>
          </Alert>
        )}
        <DialogFooter>
          <Button type="button" variant="outline" onClick={onClose}>
            {t('common.cancel')}
          </Button>
          <Button type="button" onClick={() => add.mutate()} disabled={add.isPending || (!film.existingMovieId && (!root || !profile))} data-testid="series-film-add-confirm">
            {t('seriesFilms.add')}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
