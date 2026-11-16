import { Link } from '@tanstack/react-router'
import { cn } from 'cn'
import { StarIcon, UserIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import type { DiscoverItem } from '@/api/client'
import { Poster } from '@/components/Poster'
import { Skeleton } from '@/components/ui/skeleton'
import { CardRequestButton } from '@/features/requests/RequestButton'
import { PosterBadges } from './availability'

/** A film, a series or a person of a Discover row or grid: the poster leads to its page. */
export function TitleCard({ item, className }: { item: DiscoverItem; className?: string }) {
  const { t } = useTranslation()
  const body = (
    <>
      <div className="relative">
        {item.mediaType === 'person' && !item.posterUrl ? (
          <div className="flex aspect-[2/3] w-full items-center justify-center rounded-md bg-muted text-muted-foreground">
            <UserIcon className="size-10" aria-hidden="true" />
          </div>
        ) : (
          <Poster url={item.posterUrl} title={item.title} className={cn(item.mediaType === 'person' && 'rounded-full aspect-square')} />
        )}
        <PosterBadges availability={item.availability} requests={item.requests} />
        {item.mediaType === 'series' && (
          <span className="pointer-events-none absolute top-1.5 right-1.5 rounded bg-black/60 px-1 text-[10px] font-semibold tracking-wide text-white uppercase">
            {t('discover.seriesTag')}
          </span>
        )}
      </div>
      <div className="mt-1.5 min-w-0 px-0.5">
        <p className="truncate text-sm font-medium leading-tight group-hover:underline">{item.title}</p>
        <p className="flex items-center gap-1.5 truncate text-xs text-muted-foreground">
          {item.mediaType === 'person' ? (
            item.subtitle ?? t('discover.person')
          ) : (
            <>
              {item.year ?? '—'}
              {item.subtitle && <span className="truncate">· {item.subtitle}</span>}
              {item.voteAverage != null && !item.subtitle && (
                <span className="inline-flex items-center gap-0.5 tabular-nums">
                  <StarIcon className="size-3 fill-current" aria-hidden="true" />
                  {item.voteAverage.toFixed(1)}
                </span>
              )}
            </>
          )}
        </p>
      </div>
    </>
  )

  const classes = cn('group block min-w-0 rounded-md outline-none focus-visible:ring-2 focus-visible:ring-ring', className)
  if (item.mediaType === 'person') {
    return (
      <Link to="/discover/person/$tmdbId" params={{ tmdbId: String(item.tmdbId) }} className={classes} data-testid="discover-card">
        {body}
      </Link>
    )
  }

  // The request button sits beside the link, over the poster, not inside it: a button in a link is not valid.
  const mediaType = item.mediaType === 'movie' ? 'movie' : 'series'
  return (
    <div className={cn('group/card relative min-w-0', className)}>
      {item.mediaType === 'movie' ? (
        <Link to="/discover/movie/$tmdbId" params={{ tmdbId: String(item.tmdbId) }} className={cn(classes, 'w-full')} data-testid="discover-card">
          {body}
        </Link>
      ) : (
        <Link to="/discover/tv/$tmdbId" params={{ tmdbId: String(item.tmdbId) }} className={cn(classes, 'w-full')} data-testid="discover-card">
          {body}
        </Link>
      )}
      <div className="pointer-events-none absolute inset-x-0 top-0 aspect-[2/3]">
        <CardRequestButton mediaType={mediaType} tmdbId={item.tmdbId} availability={item.availability} requests={item.requests} className="pointer-events-auto" />
      </div>
    </div>
  )
}

export function TitleCardSkeleton({ className }: { className?: string }) {
  return (
    <div className={className} aria-hidden="true">
      <Skeleton className="aspect-[2/3] w-full rounded-md" />
      <Skeleton className="mt-2 h-3.5 w-3/4" />
      <Skeleton className="mt-1 h-3 w-1/3" />
    </div>
  )
}

/** The width of a card in a row: about 3 on a phone, more as the window grows. */
export const rowCardClass = 'w-[7.5rem] shrink-0 snap-start sm:w-36 lg:w-40'

/** The grid of a full list. */
export const gridClass = 'grid grid-cols-3 gap-x-3 gap-y-5 sm:grid-cols-4 md:grid-cols-5 lg:grid-cols-6 xl:grid-cols-7 2xl:grid-cols-8'

/** Keys for placeholder cards. */
export const skeletonKeys = (count: number) => Array.from({ length: count }, (_, i) => `skeleton-${i}`)
