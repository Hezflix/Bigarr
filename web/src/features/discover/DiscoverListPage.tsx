import { useInfiniteQuery, useQuery } from '@tanstack/react-query'
import { useParams, useSearch } from '@tanstack/react-router'
import { useEffect, useRef } from 'react'
import { useTranslation } from 'react-i18next'
import { discover, type DiscoverPage } from '@/api/client'
import { Button } from '@/components/ui/button'
import { SearchBox } from './DiscoverPage'
import { sliderTitle } from './sliders'
import { TitleCard, TitleCardSkeleton, gridClass, skeletonKeys } from './TitleCard'

type Fetch = (page: number) => Promise<DiscoverPage>

/** A full list, page after page as the reader scrolls (a button too, for when the sentinel is never reached). */
function InfiniteGrid({ queryKey, fetch, empty }: { queryKey: unknown[]; fetch: Fetch; empty: string }) {
  const { t } = useTranslation()
  const list = useInfiniteQuery({
    queryKey,
    queryFn: ({ pageParam }) => fetch(pageParam),
    initialPageParam: 1,
    getNextPageParam: (last) => (last.page < last.totalPages ? last.page + 1 : undefined),
    staleTime: 5 * 60_000,
  })
  const sentinel = useRef<HTMLDivElement>(null)
  const { hasNextPage, isFetchingNextPage, fetchNextPage } = list

  useEffect(() => {
    const node = sentinel.current
    if (!node || !hasNextPage) return
    const observer = new IntersectionObserver((entries) => {
      if (entries.some((e) => e.isIntersecting) && !isFetchingNextPage) void fetchNextPage()
    }, { rootMargin: '600px' })
    observer.observe(node)
    return () => observer.disconnect()
  }, [hasNextPage, isFetchingNextPage, fetchNextPage])

  // A title can sit on two TMDB pages when the ranking moves between requests: shown once.
  const seen = new Set<string>()
  const items = (list.data?.pages ?? []).flatMap((p) => p.results).filter((item) => {
    const key = `${item.mediaType}-${item.tmdbId}`
    if (seen.has(key)) return false
    seen.add(key)
    return true
  })

  return (
    <>
      {list.error && <p className="text-sm text-destructive">{list.error.message}</p>}
      {list.data && items.length === 0 && <p className="text-sm text-muted-foreground">{empty}</p>}
      <div className={gridClass} data-testid="discover-grid">
        {items.map((item) => (
          <TitleCard key={`${item.mediaType}-${item.tmdbId}`} item={item} />
        ))}
        {(list.isPending || isFetchingNextPage) && skeletonKeys(12).map((key) => <TitleCardSkeleton key={key} />)}
      </div>
      <div ref={sentinel} />
      {hasNextPage && !isFetchingNextPage && (
        <Button variant="outline" className="self-center" onClick={() => void fetchNextPage()}>
          {t('discover.loadMore')}
        </Button>
      )}
    </>
  )
}

/** "See all" of a row. */
export function DiscoverRowPage() {
  const { t } = useTranslation()
  const { sliderId } = useParams({ from: '/shell/discover/rows/$sliderId' })
  const id = Number(sliderId)
  const home = useQuery({ queryKey: ['discover', 'home'], queryFn: discover.home, staleTime: 5 * 60_000 })
  const slider = home.data?.sliders.find((s) => s.id === id)
  return (
    <div className="flex min-w-0 flex-col gap-5">
      <h1 className="text-2xl font-semibold tracking-tight">{slider ? sliderTitle(t, slider.kind, slider.title) : t('discover.title')}</h1>
      <InfiniteGrid queryKey={['discover', 'slider', id, 'all']} fetch={(page) => discover.slider(id, page)} empty={t('discover.empty')} />
    </div>
  )
}

/** Films of a genre, a studio or a keyword. */
export function DiscoverMoviesPage() {
  const { t } = useTranslation()
  const { genre, studio, keyword, name } = useSearch({ from: '/shell/discover/movies' })
  const filter = { genre, studio, keyword }
  return (
    <div className="flex min-w-0 flex-col gap-5">
      <h1 className="text-2xl font-semibold tracking-tight">{name ? t('discover.moviesOf', { name }) : t('discover.movies')}</h1>
      <InfiniteGrid queryKey={['discover', 'movies', filter]} fetch={(page) => discover.movies(filter, page)} empty={t('discover.empty')} />
    </div>
  )
}

/** Series of a genre, a network or a keyword. */
export function DiscoverSeriesListPage() {
  const { t } = useTranslation()
  const { genre, network, keyword, name } = useSearch({ from: '/shell/discover/series' })
  const filter = { genre, network, keyword }
  return (
    <div className="flex min-w-0 flex-col gap-5">
      <h1 className="text-2xl font-semibold tracking-tight">{name ? t('discover.seriesOf', { name }) : t('discover.series')}</h1>
      <InfiniteGrid queryKey={['discover', 'series', filter]} fetch={(page) => discover.series(filter, page)} empty={t('discover.empty')} />
    </div>
  )
}

/** Films, series and people matching the words. */
export function DiscoverSearchPage() {
  const { t } = useTranslation()
  const { q } = useSearch({ from: '/shell/discover/search' })
  return (
    <div className="flex min-w-0 flex-col gap-5">
      <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <h1 className="text-2xl font-semibold tracking-tight">{q ? t('discover.resultsFor', { q }) : t('discover.search')}</h1>
        <SearchBox key={q} initial={q ?? ''} className="sm:w-80" />
      </div>
      {q && <InfiniteGrid queryKey={['discover', 'search', q]} fetch={(page) => discover.search(q, page)} empty={t('discover.noResults')} />}
    </div>
  )
}
