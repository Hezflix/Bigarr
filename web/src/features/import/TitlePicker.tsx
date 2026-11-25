import { useEffect, useRef, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { cn } from 'cn'
import { CheckIcon, ChevronDownIcon, GlobeIcon, PlusIcon, SparklesIcon, XIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { library, movies, series as seriesApi } from '@/api/client'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Command, CommandGroup, CommandInput, CommandItem, CommandList, CommandSeparator } from '@/components/ui/command'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import { bestGuesses, type GuessCandidate } from '@/features/import/titleGuess'
import { AdminAddDialog } from '@/features/requests/AdminAddDialog'

type Kind = 'movie' | 'series'

/** A title found on TMDB (films) or TVDB (series) that the library does not hold yet. */
type Outside = { key: string; tmdbId: number | null; tvdbId: number | null; title: string; year: number | null; posterUrl: string | null; overview: string | null; detail: string | null }

const label = (c: { title: string; year: number | null }) => `${c.title}${c.year ? ` (${c.year})` : ''}`

/** Typing settles before the library and TMDB/TVDB are asked. */
function useSettled(value: string, ms = 250) {
  const [settled, setSettled] = useState(value)
  useEffect(() => {
    const timer = setTimeout(() => setSettled(value), ms)
    return () => clearTimeout(timer)
  }, [value, ms])
  return settled
}

/**
 * The manual import's film or series pick. Opened, it shows only the best guesses for the file (what its name says,
 * and `first`: what other rows already picked); the library is searched once something is typed, by title, other
 * titles and year, with TMDB or TVDB below it for a title not in the library yet, added and picked without leaving.
 */
export function TitlePicker({
  kind,
  items,
  value,
  guess,
  onChange,
  testId,
}: {
  kind: Kind
  /** The library's films or series: the current pick's label and the guesses come from it. */
  items: GuessCandidate[]
  value: number | null
  guess: { title: string | null; year: number | null; first?: number[] }
  onChange: (id: number | null) => void
  testId?: string
}) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [open, setOpen] = useState(false)
  const [text, setText] = useState('')
  const [adding, setAdding] = useState<Outside | null>(null)
  const term = useSettled(text.trim())
  const searching = term.length >= 2
  const current = items.find((i) => i.id === value)
  // The row's handler of the latest render: after an add it must see the library list that holds the new title.
  const latest = useRef(onChange)
  latest.current = onChange

  const inLibrary = useQuery({
    queryKey: ['library', 'search', term],
    queryFn: () => library.search(term, 10),
    enabled: open && searching,
    staleTime: 30_000,
  })
  const outside = useQuery({
    queryKey: ['titlePicker', 'lookup', kind, term],
    queryFn: async (): Promise<Outside[]> =>
      kind === 'movie'
        ? (await movies.lookup(term))
            .filter((m) => m.existingMovieId === null)
            .map((m) => ({ key: `tmdb-${m.tmdbId}`, tmdbId: m.tmdbId, tvdbId: null, title: m.title, year: m.year, posterUrl: m.posterUrl, overview: m.overview, detail: m.originalTitle !== m.title ? m.originalTitle : null }))
        : (await seriesApi.lookup(term))
            .filter((s) => s.existingSeriesId === null)
            .map((s) => ({ key: `tv-${s.tvdbId ?? s.tmdbId}`, tmdbId: s.tmdbId, tvdbId: s.tvdbId, title: s.title, year: s.year, posterUrl: s.posterUrl, overview: s.overview, detail: s.network })),
    enabled: open && searching,
    staleTime: 60_000,
    retry: false,
  })

  const guesses = bestGuesses(items, guess.title, guess.year, 5, guess.first)
  const found = (kind === 'movie' ? inLibrary.data?.movies : inLibrary.data?.series) ?? []
  const close = () => {
    setOpen(false)
    setText('')
  }
  const pick = (id: number | null) => {
    onChange(id)
    close()
  }

  const added = async (libraryId: number) => {
    // The row's version comes from the library list: the list refetches and renders before the pick lands.
    await queryClient.refetchQueries({ queryKey: [kind === 'movie' ? 'movies' : 'series'] })
    await new Promise((resolve) => setTimeout(resolve, 0))
    setAdding(null)
    latest.current(libraryId)
    close()
  }

  const libraryItem = (c: GuessCandidate, key: string, sparkle: boolean) => (
    <CommandItem key={key} value={key} onSelect={() => pick(c.id)}>
      {sparkle && <SparklesIcon className="text-amber-500" />}
      <span className="truncate">{label(c)}</span>
      {c.id === value && <CheckIcon className="ml-auto" />}
    </CommandItem>
  )

  return (
    <>
      <Popover open={open} onOpenChange={(next) => (next ? setOpen(true) : close())}>
        <PopoverTrigger asChild>
          <Button type="button" variant="outline" size="sm" className={cn('min-w-44 max-w-64 justify-between font-normal', !current && 'text-muted-foreground')} data-testid={testId}>
            <span className="truncate">{current ? label(current) : t(kind === 'movie' ? 'manualImport.noMovie' : 'seriesImport.noSeries')}</span>
            <ChevronDownIcon className="opacity-50" />
          </Button>
        </PopoverTrigger>
        <PopoverContent align="start" className="w-96 p-0">
          <Command shouldFilter={false}>
            <CommandInput value={text} onValueChange={setText} placeholder={t(kind === 'movie' ? 'titlePicker.searchMovies' : 'titlePicker.searchSeries')} data-testid="title-picker-input" />
            <CommandList className="max-h-80">
              {!searching && (
                <>
                  {guesses.length > 0 ? (
                    <CommandGroup heading={t('titlePicker.guesses')}>{guesses.map((c) => libraryItem(c, `guess-${c.id}`, true))}</CommandGroup>
                  ) : (
                    <p className="px-3 py-3 text-xs text-muted-foreground">{t('titlePicker.noGuess')}</p>
                  )}
                  <p className="px-3 pb-2 text-xs text-muted-foreground">{t('titlePicker.typeToSearch')}</p>
                </>
              )}
              {searching && (
                <>
                  <CommandGroup heading={t('titlePicker.library')}>
                    {inLibrary.isPending ? (
                      <p className="px-2 py-1.5 text-xs text-muted-foreground">{t('titlePicker.searching')}</p>
                    ) : found.length === 0 ? (
                      <p className="px-2 py-1.5 text-xs text-muted-foreground">{t('titlePicker.noneInLibrary', { term })}</p>
                    ) : (
                      found.map((r) => (
                        <CommandItem key={`lib-${r.id}`} value={`lib-${r.id}`} onSelect={() => pick(r.id)}>
                          <span className="grid min-w-0">
                            <span className="truncate">{label(r)}</span>
                            {r.matchedTitle && r.matchedTitle !== r.title && <span className="truncate text-xs text-muted-foreground">{r.matchedTitle}</span>}
                          </span>
                          {r.id === value && <CheckIcon className="ml-auto" />}
                        </CommandItem>
                      ))
                    )}
                  </CommandGroup>
                  <CommandSeparator />
                  <CommandGroup heading={t(kind === 'movie' ? 'titlePicker.outsideMovies' : 'titlePicker.outsideSeries')}>
                    {outside.isPending ? (
                      <p className="px-2 py-1.5 text-xs text-muted-foreground">{t('titlePicker.searching')}</p>
                    ) : outside.isError ? (
                      <p className="px-2 py-1.5 text-xs text-destructive">{t('titlePicker.lookupFailed')}</p>
                    ) : (outside.data ?? []).length === 0 ? (
                      <p className="px-2 py-1.5 text-xs text-muted-foreground">{t('titlePicker.noneOutside')}</p>
                    ) : (
                      (outside.data ?? []).slice(0, 8).map((o) => (
                        <CommandItem key={o.key} value={o.key} onSelect={() => setAdding(o)} className="items-start">
                          <GlobeIcon className="mt-0.5 text-sky-500" />
                          <span className="grid min-w-0">
                            <span className="truncate">{label(o)}</span>
                            {o.detail && <span className="truncate text-xs text-muted-foreground">{o.detail}</span>}
                          </span>
                          <Badge variant="outline" className="ml-auto shrink-0 gap-1">
                            <PlusIcon className="size-3" />
                            {t('titlePicker.addPick')}
                          </Badge>
                        </CommandItem>
                      ))
                    )}
                  </CommandGroup>
                </>
              )}
              {value !== null && (
                <>
                  <CommandSeparator />
                  <CommandGroup>
                    <CommandItem value="none" onSelect={() => pick(null)} className="text-muted-foreground">
                      <XIcon />
                      {t(kind === 'movie' ? 'manualImport.noMovie' : 'seriesImport.noSeries')}
                    </CommandItem>
                  </CommandGroup>
                </>
              )}
            </CommandList>
          </Command>
        </PopoverContent>
      </Popover>
      {adding && (
        <AdminAddDialog
          mediaType={kind}
          tmdbId={adding.tmdbId}
          tvdbId={adding.tvdbId}
          preset={{ title: adding.title, year: adding.year, posterUrl: adding.posterUrl, overview: adding.overview }}
          onClose={() => setAdding(null)}
          onAdded={(id) => void added(id)}
        />
      )}
    </>
  )
}
