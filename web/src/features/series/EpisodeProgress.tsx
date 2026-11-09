import { cn } from 'cn'
import { useTranslation } from 'react-i18next'
import type { EpisodeStatistics } from '@/api/client'

/**
 * Sonarr's bar: files over the episodes wanted so far (monitored and aired, or already on disk). Green when complete,
 * the primary colour while monitored, grey when the version is not.
 */
export function EpisodeProgress({ statistics, monitored, className }: { statistics: EpisodeStatistics; monitored: boolean; className?: string }) {
  const { t } = useTranslation()
  const { episodeFileCount: files, episodeCount: count } = statistics
  const ratio = count === 0 ? 0 : Math.min(1, files / count)
  const complete = count > 0 && files >= count
  return (
    <div className={cn('flex flex-col gap-1', className)}>
      <div
        className="h-1.5 w-full overflow-hidden rounded-full bg-muted"
        role="progressbar"
        aria-valuemin={0}
        aria-valuemax={count}
        aria-valuenow={files}
        aria-label={t('seriesLibrary.episodes', { files, count })}
      >
        <div
          className={cn('h-full rounded-full', complete ? 'bg-emerald-500' : monitored ? 'bg-primary' : 'bg-muted-foreground/50')}
          style={{ width: `${ratio * 100}%` }}
        />
      </div>
      <span className="text-xs text-muted-foreground tabular-nums">{t('seriesLibrary.episodes', { files, count })}</span>
    </div>
  )
}
