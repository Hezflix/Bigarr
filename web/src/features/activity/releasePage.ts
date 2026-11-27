import type { HistoryEvent } from '@/api/client'

/** The release's page on the indexer, kept by the grab (`nzbInfoUrl`): only a web address opens. */
export function releasePage(event: Pick<HistoryEvent, 'data'>): string | null {
  const url = event.data.nzbInfoUrl?.trim()
  return url && /^https?:\/\//i.test(url) ? url : null
}
