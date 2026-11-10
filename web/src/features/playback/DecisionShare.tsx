import { useTranslation } from 'react-i18next'
import type { DecisionCounts } from '@/api/client'

/** The colours of the decision badges, as fills. */
export const decisionFills = {
  directPlay: 'bg-emerald-500',
  directStream: 'bg-blue-500',
  transcode: 'bg-amber-500',
  unknown: 'bg-muted-foreground/30',
} as const

/**
 * How the plays were served: one thin bar split by direct play, direct stream and transcode, with the shares below.
 * Plays known only from Plex's history have no decision: shown as "unknown" when there are any.
 */
export function DecisionShare({ decisions }: { decisions: DecisionCounts }) {
  const { t } = useTranslation()
  const keys = (['directPlay', 'directStream', 'transcode', 'unknown'] as const).filter((k) => k !== 'unknown' || decisions.unknown > 0)
  const total = keys.reduce((sum, k) => sum + decisions[k], 0)
  if (total === 0) return <p className="text-sm text-muted-foreground">{t('playback.noPlays')}</p>
  const label = (k: (typeof keys)[number]) => (k === 'unknown' ? t('playback.unknownDecision') : t(`playback.decisions.${k}`))
  return (
    <div className="flex flex-col gap-2" data-testid="decision-share">
      <div className="flex h-2.5 overflow-hidden rounded-full bg-muted">
        {keys.map((k) => (
          <div key={k} className={decisionFills[k]} style={{ width: `${(decisions[k] / total) * 100}%` }} title={`${label(k)}: ${decisions[k]}`} />
        ))}
      </div>
      <div className="flex flex-wrap gap-x-4 gap-y-1 text-xs">
        {keys.map((k) => (
          <span key={k} className="flex items-center gap-1.5">
            <span className={`size-2.5 rounded-sm ${decisionFills[k]}`} aria-hidden="true" />
            {label(k)}
            <span className="text-muted-foreground tabular-nums">
              {Math.round((decisions[k] / total) * 100)} % · {decisions[k]}
            </span>
          </span>
        ))}
      </div>
    </div>
  )
}
