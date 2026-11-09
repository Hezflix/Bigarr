import { useTranslation } from 'react-i18next'
import type { StreamDetails } from '@/api/client'
import { formatCodec } from './format'

const join = (...parts: (string | number | null | undefined)[]) => parts.filter((p) => p !== null && p !== undefined && p !== '').join(' ')

function channels(count: number | null): string | null {
  if (count === null) return null
  return count === 6 ? '5.1' : count === 8 ? '7.1' : count === 2 ? '2.0' : count === 1 ? '1.0' : `${count} ch`
}

/**
 * What happened to each track, like Tautulli shows it: video and audio from the file to the player, subtitles when
 * one is on. An arrow only appears when the track is converted.
 */
export function StreamTracks({ details }: { details: StreamDetails }) {
  const { t } = useTranslation()
  const decision = (value: string | null) => (value ? t(`playback.trackDecisions.${value.toLowerCase()}`, { defaultValue: value }) : null)
  const converted = (value: string | null) => value?.toLowerCase() === 'transcode'

  const videoSource = join(formatCodec(details.sourceVideoResolution), formatCodec(details.sourceVideoCodec), details.sourceDynamicRange)
  const videoStream = join(formatCodec(details.streamVideoResolution), formatCodec(details.streamVideoCodec))
  const audioSource = join(formatCodec(details.sourceAudioCodec), channels(details.sourceAudioChannels))
  const audioStream = join(formatCodec(details.streamAudioCodec), channels(details.streamAudioChannels))

  const rows = [
    {
      label: t('playback.video'),
      decision: decision(details.videoDecision),
      transcoded: converted(details.videoDecision),
      text: converted(details.videoDecision) && videoStream ? `${videoSource} → ${videoStream}` : videoSource,
      extra: converted(details.videoDecision) && details.hardwareTranscode ? t('playback.hardware') : null,
    },
    {
      label: t('playback.audio'),
      decision: decision(details.audioDecision),
      transcoded: converted(details.audioDecision),
      text: converted(details.audioDecision) && audioStream ? `${audioSource} → ${audioStream}` : audioSource,
      extra: null,
    },
    ...(details.subtitleDecision
      ? [{ label: t('playback.subtitles'), decision: decision(details.subtitleDecision), transcoded: ['burn', 'transcode'].includes(details.subtitleDecision.toLowerCase()), text: '', extra: null }]
      : []),
  ]

  return (
    <dl className="grid grid-cols-[auto_auto_1fr] gap-x-3 gap-y-1 text-xs" data-testid="stream-tracks">
      {rows.map((row) => (
        <div key={row.label} className="contents">
          <dt className="text-muted-foreground">{row.label}</dt>
          <dd className={row.transcoded ? 'font-medium text-amber-700 dark:text-amber-300' : ''}>{row.decision ?? t('common.none')}</dd>
          <dd className="min-w-0">
            {row.text}
            {row.extra && <span className="text-muted-foreground"> · {row.extra}</span>}
          </dd>
        </div>
      ))}
    </dl>
  )
}
