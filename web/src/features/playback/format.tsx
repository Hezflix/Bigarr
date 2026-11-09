import { useTranslation } from 'react-i18next'
import type { PlaybackItemRef, StreamDecision } from '@/api/client'
import { Badge } from '@/components/ui/badge'

/** 1 h 05 min, 42 min, 30 s. */
export function formatPlayed(seconds: number): string {
  const minutes = Math.floor(seconds / 60)
  if (minutes < 1) return `${seconds} s`
  const hours = Math.floor(minutes / 60)
  return hours > 0 ? `${hours} h ${String(minutes % 60).padStart(2, '0')} min` : `${minutes} min`
}

/** A position in a media: 1:05:09 or 42:10. */
export function formatOffset(ms: number): string {
  const total = Math.floor(ms / 1000)
  const hours = Math.floor(total / 3600)
  const minutes = Math.floor((total % 3600) / 60)
  const seconds = String(total % 60).padStart(2, '0')
  return hours > 0 ? `${hours}:${String(minutes).padStart(2, '0')}:${seconds}` : `${minutes}:${seconds}`
}

/** "Show — S01E02 · Episode" for an episode, "Title (year)" for a film. */
export function itemLabel(item: PlaybackItemRef): string {
  if (item.kind === 'episode' && item.showTitle) {
    const season = String(item.seasonNumber ?? 0).padStart(2, '0')
    const episode = String(item.episodeNumber ?? 0).padStart(2, '0')
    return `${item.showTitle} — S${season}E${episode} · ${item.title}`
  }
  return item.year ? `${item.title} (${item.year})` : item.title
}

const decisionStyles: Record<StreamDecision, string> = {
  directPlay: 'border-emerald-600/30 bg-emerald-500/15 text-emerald-700 dark:border-emerald-400/30 dark:text-emerald-300',
  directStream: 'border-blue-600/30 bg-blue-500/15 text-blue-700 dark:border-blue-400/30 dark:text-blue-300',
  transcode: 'border-amber-600/30 bg-amber-500/15 text-amber-700 dark:border-amber-400/30 dark:text-amber-300',
}

export function DecisionBadge({ decision }: { decision: StreamDecision | null }) {
  const { t } = useTranslation()
  if (!decision) return null
  return (
    <Badge variant="outline" className={decisionStyles[decision]}>
      {t(`playback.decisions.${decision}`)}
    </Badge>
  )
}
