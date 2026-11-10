import { useDeferredValue, useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { CheckIcon, LoaderIcon, SearchIcon, SparklesIcon } from 'lucide-react'
import { cn } from 'cn'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ApiError, movies, playback, type LinkSuggestion, type MovieSummary, type PlaybackItemRef } from '@/api/client'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'

/** How many films the list shows: enough to pick from, short enough to scan. */
const shownFilms = 30

/** Lower case without accents: "Amélie" is found by "amelie". */
const fold = (text: string) => text.normalize('NFD').replace(/\p{Diacritic}/gu, '').toLowerCase()

/**
 * Links a film the server played to a Bigarr film, by its media server item: every play of the item goes to the film
 * picked, and so do the ones to come. The library is searched by title as typed; TMDB's search, which knows the titles
 * in other languages, adds the library's films it finds. On request, Bigarr suggests the likeliest films from the
 * item's ids and titles, each with why; one click picks one. An item already linked can be moved or unlinked.
 */
export function LinkFilmDialog({ item, onClose, onLinked }: { item: PlaybackItemRef | null; onClose: () => void; onLinked?: () => void }) {
  return (
    <Dialog open={item !== null} onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="max-h-[90vh] overflow-y-auto sm:max-w-lg" data-testid="link-film">
        {item && <LinkFilmForm item={item} onClose={onClose} onLinked={onLinked} />}
      </DialogContent>
    </Dialog>
  )
}

function LinkFilmForm({ item, onClose, onLinked }: { item: PlaybackItemRef; onClose: () => void; onLinked?: () => void }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const sourceTitle = item.serverTitle ?? item.title
  const [search, setSearch] = useState(sourceTitle)
  const [movieId, setMovieId] = useState<number | null>(item.movieId)
  const [versionId, setVersionId] = useState<number | null>(null)
  const library = useQuery({ queryKey: ['movies'], queryFn: movies.list })
  const term = useDeferredValue(search.trim())
  const tmdb = useQuery({ queryKey: ['movies', 'lookup', term], queryFn: () => movies.lookup(term), enabled: term.length >= 2, staleTime: 5 * 60_000, retry: false })
  // Only once asked for: opening the picker never runs it. Suggestions and search results take turns in the same place:
  // typing a title goes back to the search, Suggest brings the suggestions back.
  const [suggesting, setSuggesting] = useState(false)
  const suggestions = useQuery({ queryKey: ['playback', 'suggestions', item.id], queryFn: () => playback.linkSuggestions(item.id), enabled: suggesting, retry: false })
  const pick = (id: number) => {
    setMovieId(id)
    setVersionId(null)
  }

  const films = useMemo(() => {
    const all = library.data ?? []
    const byId = new Map(all.map((m) => [m.id, m]))
    const needle = fold(term)
    const hits: MovieSummary[] = needle ? all.filter((m) => fold(m.title).includes(needle) || fold(m.sortTitle).includes(needle)) : []
    // The same year first: a remake shares its title.
    hits.sort((a, b) => Number(b.year === item.year) - Number(a.year === item.year) || a.sortTitle.localeCompare(b.sortTitle))
    for (const found of tmdb.data ?? []) {
      const film = found.existingMovieId !== null ? byId.get(found.existingMovieId) : undefined
      if (film && !hits.includes(film)) hits.push(film)
    }
    const current = item.movieId !== null ? byId.get(item.movieId) : undefined
    if (current && !hits.includes(current)) hits.unshift(current)
    return hits.slice(0, shownFilms)
  }, [library.data, tmdb.data, term, item.year, item.movieId])

  const chosen = library.data?.find((m) => m.id === movieId) ?? null
  const save = useMutation({
    mutationFn: (target: number | null) => playback.linkItem(item.id, target, target === null ? null : versionId),
    onSuccess: async (link) => {
      await Promise.all([queryClient.invalidateQueries({ queryKey: ['playback'] }), queryClient.invalidateQueries({ queryKey: ['movies'] })])
      toast.success(link.movieId === null ? t('playback.link.unlinked') : t('playback.link.linked', { count: link.plays }))
      onClose()
      onLinked?.()
    },
    onError: (e) => toast.error(e instanceof ApiError ? e.message : t('playback.link.failed')),
  })

  return (
    <form
      className="flex min-w-0 flex-col gap-4"
      onSubmit={(e) => {
        e.preventDefault()
        if (movieId !== null) save.mutate(movieId)
      }}
    >
      <DialogHeader>
        <DialogTitle>{item.movieId === null ? t('playback.link.title') : t('playback.link.changeTitle')}</DialogTitle>
        <DialogDescription>{t('playback.link.hint', { title: item.year ? `${sourceTitle} (${item.year})` : sourceTitle })}</DialogDescription>
      </DialogHeader>
      <div className="flex flex-col gap-2">
        <div className="flex gap-2">
          <div className="relative min-w-0 flex-1">
            <SearchIcon className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-muted-foreground" aria-hidden="true" />
            <Input type="search" value={search} onChange={(e) => {
                setSearch(e.target.value)
                setSuggesting(false)
              }} placeholder={t('playback.link.search')} aria-label={t('playback.link.search')} className="pl-8" autoFocus data-testid="link-search" />
          </div>
          <Button type="button" variant="outline" className="shrink-0 gap-1.5" onClick={() => (suggesting ? void suggestions.refetch() : setSuggesting(true))} aria-pressed={suggesting} disabled={suggestions.isFetching} data-testid="suggest-films">
            {suggestions.isFetching ? <LoaderIcon className="animate-spin" aria-hidden="true" /> : <SparklesIcon aria-hidden="true" />}
            {t('playback.link.suggest')}
          </Button>
        </div>
        {suggesting && <Suggestions query={suggestions} movieId={movieId} onPick={pick} />}
        <ul className={cn('h-64 overflow-y-auto rounded-md border p-1', suggesting && 'hidden')} role="listbox" aria-label={t('playback.link.films')} data-testid="link-results">
          {library.isPending && <Skeleton className="h-full w-full" />}
          {films.map((film) => (
            <li key={film.id} role="option" aria-selected={film.id === movieId}>
              <button
                type="button"
                onClick={() => pick(film.id)}
                className={cn('flex w-full items-center gap-2.5 rounded-sm px-2 py-1.5 text-left text-sm hover:bg-accent hover:text-accent-foreground', film.id === movieId && 'bg-accent text-accent-foreground')}
                data-testid="link-film-option"
              >
                {film.posterUrl ? (
                  <img src={film.posterUrl.replace('/original/', '/w92/')} alt="" className="aspect-[2/3] w-7 shrink-0 rounded-sm object-cover" />
                ) : (
                  <span className="aspect-[2/3] w-7 shrink-0 rounded-sm bg-muted" aria-hidden="true" />
                )}
                <span className="min-w-0 flex-1 truncate">
                  {film.title}
                  {film.year && <span className="text-muted-foreground"> ({film.year})</span>}
                </span>
                {film.versions.length > 1 && <span className="shrink-0 text-xs text-muted-foreground">{t('playback.link.versions', { count: film.versions.length })}</span>}
                <CheckIcon className={cn('size-4 shrink-0', film.id === movieId ? 'opacity-100' : 'opacity-0')} aria-hidden="true" />
              </button>
            </li>
          ))}
          {library.data && films.length === 0 && <li className="px-2 py-6 text-center text-sm text-muted-foreground">{term ? t('playback.link.noFilm') : t('playback.link.typeTitle')}</li>}
        </ul>
      </div>
      {chosen && chosen.versions.length > 0 && (
        <div className="flex flex-col gap-2">
          <Label htmlFor="link-version">{t('playback.version')}</Label>
          <Select value={versionId === null ? 'none' : String(versionId)} onValueChange={(v) => setVersionId(v === 'none' ? null : Number(v))}>
            <SelectTrigger id="link-version" className="w-full" data-testid="link-version">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="none">{t('playback.link.versionUnknown')}</SelectItem>
              {chosen.versions.map((v) => (
                <SelectItem key={v.id} value={String(v.id)}>
                  {v.name}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
      )}
      <DialogFooter className="gap-2 sm:justify-between">
        {item.movieId !== null ? (
          <Button type="button" variant="ghost" className="text-muted-foreground" onClick={() => save.mutate(null)} disabled={save.isPending} data-testid="unlink-film">
            {t('playback.link.unlink')}
          </Button>
        ) : (
          <span />
        )}
        <div className="flex flex-col-reverse gap-2 sm:flex-row">
          <Button type="button" variant="outline" onClick={onClose}>
            {t('common.cancel')}
          </Button>
          <Button type="submit" disabled={movieId === null || save.isPending} data-testid="save-link">
            {t('playback.link.save')}
          </Button>
        </div>
      </DialogFooter>
    </form>
  )
}

/** The films Bigarr finds likeliest, best first, each with why it came up; a click picks it. */
function Suggestions({ query, movieId, onPick }: { query: { data?: { suggestions: LinkSuggestion[]; tmdbSearched: boolean }; isPending: boolean; isError: boolean }; movieId: number | null; onPick: (id: number) => void }) {
  const { t } = useTranslation()
  return (
    <section className="flex flex-col gap-1.5 rounded-md border bg-muted/30 p-2" aria-label={t('playback.link.suggestions')} data-testid="link-suggestions">
      <h3 className="px-1 text-xs font-medium text-muted-foreground">{t('playback.link.suggestions')}</h3>
      {query.isPending && <Skeleton className="h-16 w-full" />}
      {query.isError && <p className="px-1 py-2 text-sm text-destructive">{t('playback.link.suggestFailed')}</p>}
      {query.data && query.data.suggestions.length === 0 && <p className="px-1 py-2 text-sm text-muted-foreground">{t('playback.link.noSuggestion')}</p>}
      {query.data && query.data.suggestions.length > 0 && (
        <ul className="flex flex-col" role="listbox" aria-label={t('playback.link.suggestions')}>
          {query.data.suggestions.map((s) => (
            <li key={s.movieId} role="option" aria-selected={s.movieId === movieId}>
              <button
                type="button"
                onClick={() => onPick(s.movieId)}
                className={cn('flex w-full items-center gap-2.5 rounded-sm px-2 py-1.5 text-left text-sm hover:bg-accent hover:text-accent-foreground', s.movieId === movieId && 'bg-accent text-accent-foreground')}
                data-testid="link-suggestion"
              >
                {s.posterUrl ? (
                  <img src={s.posterUrl.replace('/original/', '/w92/')} alt="" className="aspect-[2/3] w-7 shrink-0 rounded-sm object-cover" />
                ) : (
                  <span className="aspect-[2/3] w-7 shrink-0 rounded-sm bg-muted" aria-hidden="true" />
                )}
                <span className="flex min-w-0 flex-1 flex-col gap-1">
                  <span className="truncate">
                    {s.title}
                    {s.year && <span className="text-muted-foreground"> ({s.year})</span>}
                  </span>
                  <span className="flex flex-wrap gap-1">
                    {s.reasons.map((reason) => (
                      <Badge key={reason} variant="outline" className="px-1.5 py-0 text-[11px] font-normal">
                        {t(`playback.link.reasons.${reason}`)}
                      </Badge>
                    ))}
                  </span>
                </span>
                <span className="shrink-0 text-xs text-muted-foreground">{t('playback.link.versions', { count: s.versions })}</span>
                <CheckIcon className={cn('size-4 shrink-0', s.movieId === movieId ? 'opacity-100' : 'opacity-0')} aria-hidden="true" />
              </button>
            </li>
          ))}
        </ul>
      )}
      {query.data && !query.data.tmdbSearched && <p className="px-1 text-xs text-muted-foreground">{t('playback.link.tmdbUnavailable')}</p>}
    </section>
  )
}
