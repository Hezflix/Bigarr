import { useEffect, useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import type { TFunction } from 'i18next'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { auth, discover, requests, type Availability, type AvailabilityState, type DiscoverSeason, type RequestMediaType, type RequestSlots } from '@/api/client'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { type SeasonChoice, TitleForm } from '@/features/library/TitleForm'
import { requestRights } from './requestState'

type Slot = 'standard' | 'fourK'

/** Why a slot (or a season) cannot be asked for: already in the library, or already requested. Null when it is open. */
export function takenReason(t: TFunction, availability: Availability | undefined, requested: RequestSlots | null | undefined, slot: Slot): string | null {
  const state: AvailabilityState | null | undefined = availability?.[slot]
  if (state && state !== 'notInLibrary') return t(`discover.state.${state}`)
  const request = requested?.[slot]
  return request ? t(`requests.badge.${request}`) : null
}

/** The seasons of a title page as the picker lists them, each with why it is taken in the slot, if it is. */
export function seasonChoices(t: TFunction, seasons: DiscoverSeason[], slot: Slot): SeasonChoice[] {
  return seasons.map((s) => ({ seasonNumber: s.seasonNumber, name: s.name, episodeCount: s.episodeCount, taken: takenReason(t, s.availability, s.requests, slot) }))
}

/**
 * Ask for a film, or for seasons of a series: the version slot as the user's permissions allow (standard, and 4K with
 * "request 4K"), the seasons still open (all of them ticked to start with), and what is left of their quota.
 */
export function RequestDialog({ mediaType, tmdbId, onClose }: { mediaType: RequestMediaType; tmdbId: number; onClose: () => void }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const me = useQuery({ queryKey: ['me'], queryFn: auth.me, staleTime: 60_000 })
  const quota = useQuery({ queryKey: ['requests', 'quota'], queryFn: requests.quota })
  const movie = useQuery({ queryKey: ['discover', 'movie', String(tmdbId)], queryFn: () => discover.movie(tmdbId), enabled: mediaType === 'movie' })
  const show = useQuery({ queryKey: ['discover', 'tv', String(tmdbId)], queryFn: () => discover.show(tmdbId), enabled: mediaType === 'series' })
  const title = mediaType === 'movie' ? movie.data : show.data
  const rights = requestRights(me.data)
  const fourKOffered = title?.availability.fourK != null

  const standardTaken = title ? (mediaType === 'movie' ? takenReason(t, title.availability, title.requests, 'standard') : null) : null
  const fourKTaken = title ? (mediaType === 'movie' ? takenReason(t, title.availability, title.requests, 'fourK') : null) : null
  const [slot, setSlot] = useState<Slot>('standard')
  const [seasons, setSeasons] = useState<number[] | null>(null)

  // A film whose standard slot is taken starts on 4K when the user may ask for it.
  useEffect(() => {
    if (standardTaken && rights.fourK && fourKOffered && !fourKTaken) setSlot('fourK')
  }, [standardTaken, fourKTaken, rights.fourK, fourKOffered])

  const choices = useMemo(() => (show.data ? seasonChoices(t, show.data.seasons, slot) : []), [show.data, slot, t])
  // Every regular season still open, until the user changes it (and again when the slot changes).
  const picked = seasons ?? choices.filter((c) => !c.taken && c.seasonNumber > 0).map((c) => c.seasonNumber)

  const ask = useMutation({
    mutationFn: () => requests.create(mediaType, tmdbId, slot === 'fourK', mediaType === 'series' ? picked : null),
    onSuccess: async (created) => {
      toast.success(t(created.state === 'pending' ? 'requests.dialog.sent' : 'requests.dialog.approved', { title: created.title }))
      await Promise.all([queryClient.invalidateQueries({ queryKey: ['discover'] }), queryClient.invalidateQueries({ queryKey: ['requests'] })])
      onClose()
    },
  })

  if (!title) return null
  const quotaLeft = mediaType === 'movie' ? quota.data?.films : quota.data?.seasons
  const overQuota = quotaLeft?.remaining != null && (mediaType === 'movie' ? quotaLeft.remaining < 1 : quotaLeft.remaining < picked.length)

  return (
    <TitleForm
      kind={mediaType === 'movie' ? 'movie' : 'series'}
      posterUrl={title.posterUrl}
      title={title.title}
      year={title.year}
      overview={title.overview}
      slot={{
        fourK: slot === 'fourK',
        onChange: (fourK) => {
          setSlot(fourK ? 'fourK' : 'standard')
          setSeasons(null)
        },
        standardAllowed: true,
        fourKAllowed: rights.fourK && fourKOffered,
        standardTaken,
        fourKTaken,
      }}
      seasons={mediaType === 'series' ? { choices, value: picked, onChange: setSeasons } : undefined}
      onCancel={onClose}
      onSubmit={() => ask.mutate()}
      submitLabel={mediaType === 'series' && picked.length > 0 ? t('requests.dialog.submitSeasons', { count: picked.length }) : t('requests.dialog.submit')}
      submitTestId="request-submit"
      submitDisabled={(slot === 'standard' ? !!standardTaken : !!fourKTaken) || overQuota}
      pending={ask.isPending}
      error={ask.error}
    >
      {quotaLeft?.limit != null && (
        <Alert variant={overQuota ? 'destructive' : 'default'} data-testid="request-quota">
          <AlertDescription>
            {t(mediaType === 'movie' ? 'requests.dialog.quotaFilms' : 'requests.dialog.quotaSeasons', { remaining: quotaLeft.remaining, limit: quotaLeft.limit, days: quotaLeft.days })}
          </AlertDescription>
        </Alert>
      )}
    </TitleForm>
  )
}
