import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { cn } from 'cn'
import { PlusIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { auth, type Availability, type DiscoverSeason, type RequestMediaType, type RequestSlots } from '@/api/client'
import { Button } from '@/components/ui/button'
import { RequestDialog } from './RequestDialog'
import { requestRights } from './requestState'

type Title = { mediaType: RequestMediaType; tmdbId: number; availability: Availability | null; requests?: RequestSlots | null; seasons?: DiscoverSeason[] }

const open = (availability: Availability | null | undefined, requests: RequestSlots | null | undefined, slot: 'standard' | 'fourK') =>
  availability?.[slot] === 'notInLibrary' && !requests?.[slot]

/**
 * Whether the signed-in user can still ask for something of this title: the type allowed by their permissions, and a
 * slot (or, for a series, a season) not in the library nor requested yet. A series card does not know its seasons: the
 * dialog does, so the button shows unless the whole series is there or asked for.
 */
export function useCanRequest(title: Title) {
  const me = useQuery({ queryKey: ['me'], queryFn: auth.me, staleTime: 60_000 })
  const rights = requestRights(me.data)
  if (title.mediaType === 'movie' ? !rights.films : !rights.series) return false
  const slots: ('standard' | 'fourK')[] = rights.fourK && title.availability?.fourK != null ? ['standard', 'fourK'] : ['standard']
  const seasons = title.mediaType === 'series' ? title.seasons : undefined
  if (seasons) {
    return slots.some((slot) => seasons.some((s) => open(s.availability, s.requests, slot)))
  }

  return slots.some((slot) => open(title.availability, title.requests, slot) || (title.mediaType === 'series' && title.availability?.[slot] !== 'available' && !title.requests?.[slot]))
}

/** The "Request" button of a title page, opening the request dialog. */
export function RequestButton(title: Title) {
  const { t } = useTranslation()
  const [asking, setAsking] = useState(false)
  if (!useCanRequest(title)) return null
  return (
    <>
      <Button size="sm" onClick={() => setAsking(true)} data-testid="request-button">
        <PlusIcon />
        {t('requests.request')}
      </Button>
      {asking && <RequestDialog mediaType={title.mediaType} tmdbId={title.tmdbId} onClose={() => setAsking(false)} />}
    </>
  )
}

/** The round "+" on a Discover card, over the poster's corner; the card itself stays a link. */
export function CardRequestButton({ className, ...title }: Title & { className?: string }) {
  const { t } = useTranslation()
  const [asking, setAsking] = useState(false)
  if (!useCanRequest(title)) return null
  return (
    <>
      <button
        type="button"
        onClick={() => setAsking(true)}
        aria-label={t('requests.request')}
        title={t('requests.request')}
        className={cn(
          'absolute right-1.5 bottom-1.5 flex size-9 items-center justify-center rounded-full bg-primary text-primary-foreground shadow-md transition-opacity outline-none focus-visible:opacity-100 focus-visible:ring-2 focus-visible:ring-ring sm:size-8 sm:opacity-0 sm:group-hover/card:opacity-100',
          className,
        )}
        data-testid="card-request"
      >
        <PlusIcon className="size-4" aria-hidden="true" />
      </button>
      {asking && <RequestDialog mediaType={title.mediaType} tmdbId={title.tmdbId} onClose={() => setAsking(false)} />}
    </>
  )
}
