import type { TFunction } from 'i18next'
import { toast } from 'sonner'
import { ApiError, type AutomaticSearchResult } from '@/api/client'

/** One toast per version: the release grabbed, or why nothing was. */
export function toastSearchResults(t: TFunction, results: AutomaticSearchResult[]) {
  for (const r of results) {
    const label = r.movieTitle ? `${r.movieTitle} [${r.versionName}]` : r.versionName
    switch (r.outcome) {
      case 'grabbed':
        toast.success(t('search.autoGrabbed', { label, title: r.releaseTitle ?? '' }))
        break
      case 'nothingFound':
        toast.info(t('search.autoNothing', { label, detail: r.detail ?? '' }))
        break
      case 'noIndexer':
        toast.warning(t('search.autoNoIndexer', { label }))
        break
      case 'skipped':
        toast.info(t('search.autoSkipped', { label, detail: r.detail ?? '' }))
        break
      case 'failed':
        toast.error(t('search.autoFailed', { label, detail: r.detail ?? '' }))
        break
      case 'picked':
        toast.success(t('search.autoPicked', { label, title: r.releaseTitle ?? '' }))
        break
    }
  }
}

export function toastSearchError(t: TFunction, error: unknown) {
  toast.error(error instanceof ApiError ? error.message : t('search.failed'))
}
