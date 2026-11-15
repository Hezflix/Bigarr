import { useQuery } from '@tanstack/react-query'
import { Link, useNavigate } from '@tanstack/react-router'
import { cn } from 'cn'
import { ChevronRightIcon, SearchIcon } from 'lucide-react'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { auth, discover, isAdmin, type DiscoverSlider } from '@/api/client'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Input } from '@/components/ui/input'
import { Skeleton } from '@/components/ui/skeleton'
import { sliderTitle } from './sliders'
import { TitleCard, TitleCardSkeleton, rowCardClass, skeletonKeys } from './TitleCard'

/** Discover: rows of TMDB titles with their availability in the library, a search, and links to full lists. */
export function DiscoverPage() {
  const { t } = useTranslation()
  const home = useQuery({ queryKey: ['discover', 'home'], queryFn: discover.home, staleTime: 5 * 60_000 })
  const me = useQuery({ queryKey: ['me'], queryFn: auth.me, staleTime: 60_000 })

  return (
    <div className="flex min-w-0 flex-col gap-8" data-testid="discover-page">
      <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <h1 className="text-2xl font-semibold tracking-tight">{t('discover.title')}</h1>
        <SearchBox className="sm:w-80" />
      </div>

      {home.error && <p className="text-sm text-destructive">{home.error.message}</p>}
      {home.data && !home.data.configured && (
        <Alert>
          <AlertTitle>{t('discover.notConfigured.title')}</AlertTitle>
          <AlertDescription>{isAdmin(me.data) ? t('discover.notConfigured.admin') : t('discover.notConfigured.user')}</AlertDescription>
        </Alert>
      )}
      {home.isPending && (
        <div className="flex flex-col gap-8">
          {skeletonKeys(2).map((row) => (
            <div key={row} className="flex flex-col gap-3">
              <Skeleton className="h-6 w-40" />
              <div className="flex gap-3 overflow-hidden">
                {skeletonKeys(8).map((key) => (
                  <TitleCardSkeleton key={key} className={rowCardClass} />
                ))}
              </div>
            </div>
          ))}
        </div>
      )}
      {home.data?.configured && home.data.sliders.length === 0 && <p className="text-sm text-muted-foreground">{t('discover.noRows')}</p>}
      {home.data?.sliders.map((slider) => (slider.tiles ? <TileRow key={slider.id} slider={slider} /> : <TitleRow key={slider.id} slider={slider} />))}
    </div>
  )
}

/** Sends the words to the search page, which searches films, series and people. */
export function SearchBox({ className, initial = '' }: { className?: string; initial?: string }) {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const [text, setText] = useState(initial)
  return (
    <form
      className={cn('relative w-full', className)}
      onSubmit={(e) => {
        e.preventDefault()
        if (text.trim()) void navigate({ to: '/discover/search', search: { q: text.trim() } })
      }}
    >
      <SearchIcon className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-muted-foreground" aria-hidden="true" />
      <Input
        type="search"
        value={text}
        onChange={(e) => setText(e.target.value)}
        placeholder={t('discover.searchPlaceholder')}
        aria-label={t('discover.searchPlaceholder')}
        className="pl-8"
        data-testid="discover-search"
      />
    </form>
  )
}

function RowHeader({ title, to }: { title: string; to?: React.ReactNode }) {
  return (
    <div className="flex items-baseline justify-between gap-3">
      <h2 className="text-lg font-semibold tracking-tight">{title}</h2>
      {to}
    </div>
  )
}

function TitleRow({ slider }: { slider: DiscoverSlider }) {
  const { t } = useTranslation()
  const page = useQuery({ queryKey: ['discover', 'slider', slider.id, 1], queryFn: () => discover.slider(slider.id, 1), staleTime: 5 * 60_000 })
  const title = sliderTitle(t, slider.kind, slider.title)
  if (page.data && page.data.results.length === 0) return null

  return (
    <section className="flex min-w-0 flex-col gap-3" aria-label={title} data-testid={`row-${slider.kind}`}>
      <RowHeader
        title={title}
        to={
          <Link
            to="/discover/rows/$sliderId"
            params={{ sliderId: String(slider.id) }}
            className="inline-flex shrink-0 items-center text-sm text-muted-foreground hover:text-foreground"
          >
            {t('discover.seeAll')}
            <ChevronRightIcon className="size-4" aria-hidden="true" />
          </Link>
        }
      />
      {page.error && <p className="text-sm text-destructive">{page.error.message}</p>}
      <div className="-mx-4 flex snap-x scroll-px-4 gap-3 overflow-x-auto px-4 pb-2 md:-mx-6 md:scroll-px-6 md:px-6">
        {page.isPending
          ? skeletonKeys(8).map((key) => <TitleCardSkeleton key={key} className={rowCardClass} />)
          : page.data?.results.map((item) => <TitleCard key={`${item.mediaType}-${item.tmdbId}`} item={item} className={rowCardClass} />)}
      </div>
    </section>
  )
}

/** Gradients for the tiles, picked by id so a genre keeps its colour. */
const tileColours = [
  'from-rose-500 to-orange-400',
  'from-violet-600 to-indigo-500',
  'from-emerald-600 to-teal-400',
  'from-sky-600 to-cyan-400',
  'from-amber-500 to-yellow-400',
  'from-fuchsia-600 to-pink-400',
  'from-slate-700 to-slate-500',
  'from-lime-600 to-green-400',
]

function TileRow({ slider }: { slider: DiscoverSlider }) {
  const { t } = useTranslation()
  const title = sliderTitle(t, slider.kind, slider.title)
  return (
    <section className="flex min-w-0 flex-col gap-3" aria-label={title} data-testid={`row-${slider.kind}`}>
      <RowHeader title={title} />
      <div className="-mx-4 flex snap-x scroll-px-4 gap-3 overflow-x-auto px-4 pb-2 md:-mx-6 md:scroll-px-6 md:px-6">
        {slider.tiles?.map((tile) => {
          const classes = cn(
            'flex h-20 w-40 shrink-0 snap-start items-end rounded-lg bg-gradient-to-br p-3 text-sm font-semibold text-white shadow-sm outline-none hover:brightness-110 focus-visible:ring-2 focus-visible:ring-ring sm:w-48',
            tileColours[tile.id % tileColours.length],
          )
          const search = { name: tile.name }
          switch (slider.kind) {
            case 'movieGenres':
              return <Link key={tile.id} to="/discover/movies" search={{ ...search, genre: tile.id }} className={classes}>{tile.name}</Link>
            case 'seriesGenres':
              return <Link key={tile.id} to="/discover/series" search={{ ...search, genre: tile.id }} className={classes}>{tile.name}</Link>
            case 'studios':
              return <Link key={tile.id} to="/discover/movies" search={{ ...search, studio: tile.id }} className={classes}>{tile.name}</Link>
            default:
              return <Link key={tile.id} to="/discover/series" search={{ ...search, network: tile.id }} className={classes}>{tile.name}</Link>
          }
        })}
      </div>
    </section>
  )
}
