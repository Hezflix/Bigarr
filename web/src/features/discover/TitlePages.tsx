import { useQuery } from '@tanstack/react-query'
import { Link, useParams } from '@tanstack/react-router'
import { LibraryIcon, PlayIcon, StarIcon, UserIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import type { Availability, DiscoverItem, DiscoverSeason, RequestSlots } from '@/api/client'
import { discover } from '@/api/client'
import { Poster } from '@/components/Poster'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { RequestButton } from '@/features/requests/RequestButton'
import { RequestChips } from '@/features/requests/requestState'
import { TitleIssues } from '@/features/issues/TitleIssues'
import { AvailabilityChips } from './availability'
import { TitleCard, rowCardClass } from './TitleCard'

/** Smaller TMDB widths for the backdrop: the original is several megabytes. */
const backdrop = (url: string | null) => (url ? url.replace('/original/', '/w1280/') : null)
const profile = (url: string | null) => (url ? url.replace('/original/', '/w185/') : null)

type HeroProps = {
  title: string
  posterUrl: string | null
  backdropUrl: string | null
  facts: (string | null | undefined)[]
  genres: React.ReactNode
  voteAverage: number | null
  tagline: string | null
  overview: string | null
  availability: Availability
  requests: RequestSlots | null
  trailerUrl: string | null
  library: React.ReactNode
  request: React.ReactNode
  issues: React.ReactNode
}

function Hero({ title, posterUrl, backdropUrl, facts, genres, voteAverage, tagline, overview, availability, requests, trailerUrl, library, request, issues }: HeroProps) {
  const { t } = useTranslation()
  const image = backdrop(backdropUrl)
  return (
    <section className="relative -mx-4 -mt-4 overflow-hidden md:-mx-6 md:-mt-6">
      {image && (
        <img src={image} alt="" className="absolute inset-0 h-full w-full object-cover opacity-30 dark:opacity-25" aria-hidden="true" />
      )}
      <div className="absolute inset-0 bg-gradient-to-t from-background via-background/80 to-background/30" aria-hidden="true" />
      <div className="relative flex flex-col gap-5 px-4 pt-8 pb-6 sm:flex-row sm:items-end md:px-6 md:pt-16">
        <Poster url={posterUrl} title={title} size="large" className="shadow-lg max-sm:w-32" />
        <div className="flex min-w-0 flex-col gap-3">
          <h1 className="text-2xl font-semibold tracking-tight sm:text-3xl" data-testid="title-name">
            {title}
          </h1>
          <p className="flex flex-wrap items-center gap-x-2 gap-y-1 text-sm text-muted-foreground">
            {facts.filter(Boolean).map((fact) => (
              <span key={fact} className="after:ml-2 after:content-['·'] last:after:content-none">
                {fact}
              </span>
            ))}
            {voteAverage != null && (
              <span className="inline-flex items-center gap-1 tabular-nums">
                <StarIcon className="size-3.5 fill-amber-400 text-amber-400" aria-hidden="true" />
                {voteAverage.toFixed(1)}
              </span>
            )}
          </p>
          {genres}
          <div className="flex flex-wrap items-center gap-1.5">
            <AvailabilityChips availability={availability} />
            <RequestChips requests={requests} />
          </div>
          <div className="flex flex-wrap gap-2">
            {request}
            {trailerUrl && (
              <Button asChild variant="outline" size="sm">
                <a href={trailerUrl} target="_blank" rel="noreferrer noopener">
                  <PlayIcon />
                  {t('discover.trailer')}
                </a>
              </Button>
            )}
            {library}
            {issues}
          </div>
        </div>
      </div>
      {(tagline || overview) && (
        <div className="relative flex max-w-3xl flex-col gap-2 px-4 pb-2 md:px-6">
          {tagline && <p className="text-sm text-muted-foreground italic">{tagline}</p>}
          {overview && <p className="text-sm leading-relaxed">{overview}</p>}
        </div>
      )}
    </section>
  )
}

function Row({ title, items }: { title: string; items: DiscoverItem[] }) {
  if (items.length === 0) return null
  return (
    <section className="flex min-w-0 flex-col gap-3" aria-label={title}>
      <h2 className="text-lg font-semibold tracking-tight">{title}</h2>
      <div className="-mx-4 flex snap-x scroll-px-4 gap-3 overflow-x-auto px-4 pb-2 md:-mx-6 md:scroll-px-6 md:px-6">
        {items.map((item) => (
          <TitleCard key={`${item.mediaType}-${item.tmdbId}`} item={item} className={rowCardClass} />
        ))}
      </div>
    </section>
  )
}

function Cast({ cast }: { cast: { id: number; name: string; character: string | null; profileUrl: string | null }[] }) {
  const { t } = useTranslation()
  if (cast.length === 0) return null
  return (
    <section className="flex min-w-0 flex-col gap-3" aria-label={t('discover.cast')}>
      <h2 className="text-lg font-semibold tracking-tight">{t('discover.cast')}</h2>
      <div className="-mx-4 flex snap-x scroll-px-4 gap-3 overflow-x-auto px-4 pb-2 md:-mx-6 md:scroll-px-6 md:px-6">
        {cast.map((person) => (
          <Link
            key={person.id}
            to="/discover/person/$tmdbId"
            params={{ tmdbId: String(person.id) }}
            className="group flex w-24 shrink-0 snap-start flex-col items-center gap-1.5 text-center outline-none focus-visible:ring-2 focus-visible:ring-ring"
          >
            {person.profileUrl ? (
              <img src={profile(person.profileUrl) ?? undefined} alt="" loading="lazy" className="size-20 rounded-full bg-muted object-cover" />
            ) : (
              <span className="flex size-20 items-center justify-center rounded-full bg-muted text-muted-foreground">
                <UserIcon className="size-8" aria-hidden="true" />
              </span>
            )}
            <span className="line-clamp-2 text-xs font-medium group-hover:underline">{person.name}</span>
            {person.character && <span className="line-clamp-2 text-[11px] text-muted-foreground">{person.character}</span>}
          </Link>
        ))}
      </div>
    </section>
  )
}

function Loading({ error }: { error: Error | null }) {
  if (error) return <p className="text-sm text-destructive">{error.message}</p>
  return (
    <div className="flex flex-col gap-4 sm:flex-row">
      <Skeleton className="aspect-[2/3] w-40 sm:w-48" />
      <div className="flex flex-1 flex-col gap-3">
        <Skeleton className="h-8 w-2/3" />
        <Skeleton className="h-4 w-1/3" />
        <Skeleton className="h-16 w-full" />
      </div>
    </div>
  )
}

export function DiscoverMoviePage() {
  const { t } = useTranslation()
  const { tmdbId } = useParams({ from: '/shell/discover/movie/$tmdbId' })
  const movie = useQuery({ queryKey: ['discover', 'movie', tmdbId], queryFn: () => discover.movie(Number(tmdbId)) })
  if (!movie.data) return <Loading error={movie.error} />
  const m = movie.data

  return (
    <div className="flex min-w-0 flex-col gap-8" data-testid="discover-movie">
      <Hero
        title={m.title}
        posterUrl={m.posterUrl}
        backdropUrl={m.backdropUrl}
        facts={[m.year ? String(m.year) : null, m.runtime ? t('discover.minutes', { count: m.runtime }) : null, m.certification, m.directors.length ? t('discover.directedBy', { names: m.directors.join(', ') }) : null]}
        genres={
          <div className="flex flex-wrap gap-1.5">
            {m.genres.map((g) => (
              <Link key={g.id} to="/discover/movies" search={{ genre: g.id, name: g.name }} className="rounded-full border px-2 py-0.5 text-xs hover:bg-accent">
                {g.name}
              </Link>
            ))}
          </div>
        }
        voteAverage={m.voteAverage}
        tagline={m.tagline}
        overview={m.overview}
        availability={m.availability}
        requests={m.requests}
        request={<RequestButton mediaType="movie" tmdbId={m.tmdbId} availability={m.availability} requests={m.requests} inLibrary={m.libraryId != null} />}
        issues={<TitleIssues mediaType="movie" tmdbId={m.tmdbId} title={m.title} />}
        trailerUrl={m.trailerUrl}
        library={
          m.libraryId != null && (
            <Button asChild variant="outline" size="sm">
              <Link to="/movies/$movieId" params={{ movieId: String(m.libraryId) }} data-testid="open-in-library">
                <LibraryIcon />
                {t('discover.openInLibrary')}
              </Link>
            </Button>
          )
        }
      />
      <Cast cast={m.cast} />
      <Row title={t('discover.recommendations')} items={m.recommendations} />
      <Row title={t('discover.similar')} items={m.similar} />
    </div>
  )
}

function Seasons({ seasons }: { seasons: DiscoverSeason[] }) {
  const { t } = useTranslation()
  if (seasons.length === 0) return null
  return (
    <section className="flex min-w-0 flex-col gap-3" aria-label={t('discover.seasons')}>
      <h2 className="text-lg font-semibold tracking-tight">{t('discover.seasons')}</h2>
      <ul className="divide-y rounded-lg border" data-testid="discover-seasons">
        {seasons.map((season) => (
          <li key={season.seasonNumber} className="flex flex-wrap items-center gap-x-3 gap-y-2 px-3 py-2.5">
            <Poster url={season.posterUrl} title={season.name} size="thumbMedium" />
            <div className="min-w-0 flex-1 basis-40">
              <p className="truncate text-sm font-medium">{season.name}</p>
              <p className="text-xs text-muted-foreground">
                {[
                  season.episodeCount != null ? t('discover.episodes', { count: season.episodeCount }) : null,
                  season.airDate?.slice(0, 4),
                  season.inLibraryOnly ? t('discover.libraryOnlySeason') : null,
                ].filter(Boolean).join(' · ')}
              </p>
            </div>
            <div className="flex w-full flex-wrap gap-1.5 pl-[3.25rem] sm:w-auto sm:justify-end sm:pl-0">
              <AvailabilityChips availability={season.availability} />
              <RequestChips requests={season.requests} />
            </div>
          </li>
        ))}
      </ul>
    </section>
  )
}

export function DiscoverSeriesPage() {
  const { t } = useTranslation()
  const { tmdbId } = useParams({ from: '/shell/discover/tv/$tmdbId' })
  const series = useQuery({ queryKey: ['discover', 'tv', tmdbId], queryFn: () => discover.show(Number(tmdbId)) })
  if (!series.data) return <Loading error={series.error} />
  const s = series.data
  const years = s.firstAirDate ? `${s.firstAirDate.slice(0, 4)}${s.status === 'Ended' && s.lastAirDate ? `–${s.lastAirDate.slice(0, 4)}` : ''}` : null

  return (
    <div className="flex min-w-0 flex-col gap-8" data-testid="discover-series">
      <Hero
        title={s.title}
        posterUrl={s.posterUrl}
        backdropUrl={s.backdropUrl}
        facts={[years, s.networks.map((n) => n.name).join(', ') || null, s.runtime ? t('discover.minutes', { count: s.runtime }) : null, s.status ? t(`discover.seriesStatus.${s.status.replace(/\s+/g, '')}`, { defaultValue: s.status }) : null]}
        genres={
          <div className="flex flex-wrap gap-1.5">
            {s.genres.map((g) => (
              <Link key={g.id} to="/discover/series" search={{ genre: g.id, name: g.name }} className="rounded-full border px-2 py-0.5 text-xs hover:bg-accent">
                {g.name}
              </Link>
            ))}
          </div>
        }
        voteAverage={s.voteAverage}
        tagline={s.tagline}
        overview={s.overview}
        availability={s.availability}
        requests={s.requests}
        request={<RequestButton mediaType="series" tmdbId={s.tmdbId} availability={s.availability} requests={s.requests} seasons={s.seasons} inLibrary={s.libraryId != null} />}
        issues={<TitleIssues mediaType="series" tmdbId={s.tmdbId} tvdbId={s.tvdbId} title={s.title} />}
        trailerUrl={s.trailerUrl}
        library={
          s.libraryId != null && (
            <Button asChild variant="outline" size="sm">
              <Link to="/series/$seriesId" params={{ seriesId: String(s.libraryId) }} data-testid="open-in-library">
                <LibraryIcon />
                {t('discover.openInLibrary')}
              </Link>
            </Button>
          )
        }
      />
      <Seasons seasons={s.seasons} />
      <Cast cast={s.cast} />
      <Row title={t('discover.recommendations')} items={s.recommendations} />
      <Row title={t('discover.similar')} items={s.similar} />
    </div>
  )
}

export function DiscoverPersonPage() {
  const { t } = useTranslation()
  const { tmdbId } = useParams({ from: '/shell/discover/person/$tmdbId' })
  const person = useQuery({ queryKey: ['discover', 'person', tmdbId], queryFn: () => discover.person(Number(tmdbId)) })
  if (!person.data) return <Loading error={person.error} />
  const p = person.data

  return (
    <div className="flex min-w-0 flex-col gap-6" data-testid="discover-person">
      <div className="flex flex-col gap-5 sm:flex-row">
        <Poster url={p.profileUrl} title={p.name} size="large" className="max-sm:w-32" />
        <div className="flex min-w-0 flex-col gap-2">
          <h1 className="text-2xl font-semibold tracking-tight sm:text-3xl">{p.name}</h1>
          <p className="text-sm text-muted-foreground">
            {[p.knownFor, p.birthday ? t('discover.born', { date: p.birthday }) : null, p.placeOfBirth].filter(Boolean).join(' · ')}
          </p>
          {p.biography && <p className="line-clamp-[8] max-w-3xl text-sm leading-relaxed whitespace-pre-line">{p.biography}</p>}
        </div>
      </div>
      <section className="flex flex-col gap-3">
        <h2 className="text-lg font-semibold tracking-tight">{t('discover.knownFor')}</h2>
        <div className="grid grid-cols-3 gap-x-3 gap-y-5 sm:grid-cols-4 md:grid-cols-5 lg:grid-cols-6 xl:grid-cols-7 2xl:grid-cols-8">
          {p.credits.map((item) => (
            <TitleCard key={`${item.mediaType}-${item.tmdbId}`} item={item} />
          ))}
        </div>
      </section>
    </div>
  )
}
