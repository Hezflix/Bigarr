import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { Loader2Icon, PlusIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { library, type LibrarySearchResult } from '@/api/client'
import { Poster } from '@/components/Poster'
import { CommandGroup, CommandItem } from '@/components/ui/command'
import { useDebounced } from '@/hooks/useDebounced'
import { contentScore, contentTier } from './filter'
import type { PaletteTarget } from './target'

/** The shortest term worth asking the server about: one letter matches half the library. */
export const minContentTerm = 2

/** The value carries how well the hit matches (`exact`, `prefix`…): cmdk ranks it against pages and actions with that. */
const itemValue = (item: LibrarySearchResult, term: string) => `content:${item.kind}:${item.id}:${contentTier(term, item.matchedTitle ?? item.title)}`

/** Films and series of the library matching the query, searched on the server a moment after typing stops. */
export function useContentSearch(query: string) {
  const term = query.trim()
  const debounced = useDebounced(term, 150)
  const enabled = debounced.length >= minContentTerm

  const results = useQuery({
    queryKey: ['librarySearch', debounced],
    queryFn: () => library.search(debounced),
    enabled,
    staleTime: 30_000,
    // The previous term's rows stay while the next ones load, so the list does not blink at every key.
    placeholderData: keepPreviousData,
  })

  const shown = term.length >= minContentTerm && enabled
  const movies = shown ? (results.data?.movies ?? []) : []
  const series = shown ? (results.data?.series ?? []) : []
  const first = movies[0] ?? series[0]
  return {
    term,
    debounced,
    movies,
    series,
    searching: results.isFetching || debounced !== term,
    /** The first film or series and how well it matches; the rows of the current term only, not the previous term's kept while loading. */
    fresh: enabled && !results.isPlaceholderData ? results.data : undefined,
    /** The first row shown (whatever term it is for) and how well it matches, for ranking the groups. */
    top: first ? { value: itemValue(first, debounced), score: contentScore(contentTier(debounced, first.matchedTitle ?? first.title)) } : null,
  }
}

export type ContentSearch = ReturnType<typeof useContentSearch>

/** The first film or series of fresh results, and its value, for the palette to select. */
export function firstResult(fresh: ContentSearch['fresh'], term: string): { value: string; score: number } | null {
  const first = fresh?.movies[0] ?? fresh?.series[0]
  if (!first) return null
  return { value: itemValue(first, term), score: contentScore(contentTier(term, first.matchedTitle ?? first.title)) }
}

/** Films and series of the library matching the query. */
export function ContentGroups({ search, onSelect }: { search: ContentSearch; onSelect: (target: PaletteTarget) => void }) {
  const { t } = useTranslation()
  const { term, debounced, movies, series, searching } = search
  if (term.length < minContentTerm) return null

  return (
    <>
      {movies.length > 0 && (
        <CommandGroup heading={t('palette.content.movies')}>
          {movies.map((item) => (
            <ContentItem key={item.id} item={item} term={debounced} onSelect={() => onSelect({ kind: 'movie', id: item.id })} />
          ))}
        </CommandGroup>
      )}
      {series.length > 0 && (
        <CommandGroup heading={t('palette.content.series')}>
          {series.map((item) => (
            <ContentItem key={item.id} item={item} term={debounced} onSelect={() => onSelect({ kind: 'series', id: item.id })} />
          ))}
        </CommandGroup>
      )}
      {searching && movies.length === 0 && series.length === 0 && (
        <div className="flex items-center gap-2 px-4 py-2 text-xs text-muted-foreground" role="status">
          <Loader2Icon className="size-3.5 animate-spin" aria-hidden="true" />
          {t('palette.content.searching')}
        </div>
      )}
    </>
  )
}

/** "Add “…” from Discover": opens Discover's search on the query, where films and series are added. Rendered last. */
export function AddGroup({ query, onSelect }: { query: string; onSelect: (target: PaletteTarget) => void }) {
  const { t } = useTranslation()
  const term = query.trim()
  if (term.length < minContentTerm) return null
  return (
    <CommandGroup heading={t('palette.content.add')}>
      <CommandItem value="add:discover" onSelect={() => onSelect({ kind: 'add', term })} data-testid="palette-add">
        <PlusIcon />
        <span className="truncate">{t('palette.content.addFromDiscover', { term })}</span>
      </CommandItem>
    </CommandGroup>
  )
}

function ContentItem({ item, term, onSelect }: { item: LibrarySearchResult; term: string; onSelect: () => void }) {
  const { t } = useTranslation()
  return (
    <CommandItem value={itemValue(item, term)} onSelect={onSelect} className="py-1!" data-testid={`palette-${item.kind}-${item.id}`}>
      <Poster url={item.posterUrl} title={item.title} size="thumb" className="rounded-sm" />
      <span className="flex min-w-0 flex-col">
        <span className="truncate">
          {item.title}
          {item.year && <span className="ml-1.5 text-muted-foreground tabular-nums">{item.year}</span>}
        </span>
        {item.matchedTitle && (
          <span className="truncate text-xs text-muted-foreground">{t('palette.content.alsoKnownAs', { title: item.matchedTitle })}</span>
        )}
      </span>
    </CommandItem>
  )
}
