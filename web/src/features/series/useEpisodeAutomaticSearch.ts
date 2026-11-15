import { useMutation, useQueryClient } from '@tanstack/react-query'
import type { TFunction } from 'i18next'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ApiError, series as seriesApi } from '@/api/client'

type EpisodeSearchResult = Awaited<ReturnType<typeof seriesApi.searchEpisode>>[number]

/** One toast per version: grabbed, picked (shadow mode) or nothing found. */
export function toastEpisodeSearch(t: TFunction, results: EpisodeSearchResult[]) {
  for (const r of results) {
    if (r.grabbed > 0) toast.success(t('episodeDetail.autoGrabbed', { version: r.versionName }))
    else if (r.picked > 0) toast.success(t('episodeDetail.autoPicked', { version: r.versionName }))
    else toast.info(t('episodeDetail.autoNothing', { version: r.versionName, detail: r.detail ?? '' }))
  }
}

/** The automatic search of one episode, now, by every version of the series; shared by the episode rows and the episode details. */
export function useEpisodeAutomaticSearch(seriesId: number, onChanged: () => void) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (episodeId: number) => seriesApi.searchEpisode(seriesId, episodeId),
    onSuccess: (results, episodeId) => {
      toastEpisodeSearch(t, results)
      void queryClient.invalidateQueries({ queryKey: ['history', 'episode', episodeId] })
      void queryClient.invalidateQueries({ queryKey: ['queue'] })
      onChanged()
    },
    onError: (e) => toast.error(e instanceof ApiError ? e.message : t('search.failed')),
  })
}
