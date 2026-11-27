import type { HistoryEvent } from '@/api/client'

/**
 * A grab writes one line per episode it covers: a season pack would fill a season's history with as many identical rows.
 * The lines of one grab (same release, version and moment) fold into the first, which lists their episodes. Every other
 * event stays one row per episode: an import's path or a deletion differs from one episode to the next.
 */
export function foldGrabs(events: HistoryEvent[]): HistoryEvent[] {
  const folded: HistoryEvent[] = []
  const groups = new Map<string, { event: HistoryEvent; episodes: string[] }>()
  for (const event of events) {
    if (event.eventType !== 'grabbed') {
      folded.push(event)
      continue
    }
    const key = [event.versionName, event.sourceTitle, event.createdAt, event.data.downloadId ?? ''].join('\u0000')
    const group = groups.get(key)
    if (group) {
      if (event.data.episode) group.episodes.push(event.data.episode)
      continue
    }
    const fresh = { event: { ...event, data: { ...event.data } }, episodes: event.data.episode ? [event.data.episode] : [] }
    groups.set(key, fresh)
    folded.push(fresh.event)
  }
  for (const { event, episodes } of groups.values()) {
    if (episodes.length > 1) event.data.episode = [...new Set(episodes)].sort().join(', ')
  }
  return folded
}
