import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { Loader2Icon, PlusIcon } from 'lucide-react'
import { useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { library, type LibrarySearchResult } from '@/api/client'
import { Poster } from '@/components/Poster'
import { CommandGroup, CommandItem } from '@/components/ui/command'
import { useDebounced } from '@/hooks/useDebounced'
import type { PaletteTarget } from './target'

/** The shortest term worth asking the server about: one letter matches half the library. */
export const minContentTerm = 2

type Props = {
  query: string
  onSelect: (target: PaletteTarget) => void
  /** Told the first film or series once the rows of the current term arrive, for the palette to select it. */
  onFirstResult: (value: string) => void
}

const itemValue = (item: LibrarySearchResult) => `content:${item.kind}:${item.id}`

/** Films and series of the library matching the query. */
export function ContentGroups({ query, onSelect, onFirstResult }: Props) {
  const { t } = useTranslation()
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

  // cmdk keeps the selected entry while new ones appear above it: without this the "Add" entry, there first, stays
  // selected and Enter would add instead of opening the film that was found.
  const fresh = enabled && !results.isPlaceholderData ? results.data : undefined
  useEffect(() => {
    const first = fresh?.movies[0] ?? fresh?.series[0]
    if (first) onFirstResult(itemValue(first))
  }, [fresh, onFirstResult])

  if (term.length < minContentTerm) return null

  const movies = enabled ? (results.data?.movies ?? []) : []
  const series = enabled ? (results.data?.series ?? []) : []
  const searching = results.isFetching || debounced !== term

  return (
    <>
      {movies.length > 0 && (
        <CommandGroup heading={t('palette.content.movies')}>
          {movies.map((item) => (
            <ContentItem key={item.id} item={item} onSelect={() => onSelect({ kind: 'movie', id: item.id })} />
          ))}
        </CommandGroup>
      )}
      {series.length > 0 && (
        <CommandGroup heading={t('palette.content.series')}>
          {series.map((item) => (
            <ContentItem key={item.id} item={item} onSelect={() => onSelect({ kind: 'series', id: item.id })} />
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

function ContentItem({ item, onSelect }: { item: LibrarySearchResult; onSelect: () => void }) {
  const { t } = useTranslation()
  return (
    <CommandItem value={itemValue(item)} onSelect={onSelect} className="py-1!" data-testid={`palette-${item.kind}-${item.id}`}>
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
