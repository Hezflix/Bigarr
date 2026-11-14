import { useTranslation } from 'react-i18next'
import type { CompareTimingNote } from '@/api/client'

/** "10 min", "2 h", "3 d": how far apart two moments are. */
export function formatGap(seconds: number): string {
  const s = Math.abs(seconds)
  if (s < 90) return `${Math.round(s)} s`
  if (s < 3600) return `${Math.round(s / 60)} min`
  if (s < 48 * 3600) return `${Math.round(s / 3600)} h`
  return `${Math.round(s / 86400)} d`
}

const lowerFirst = (text: string) => text.charAt(0).toLowerCase() + text.slice(1)

/**
 * Why Bigarr picked nothing when the followed app grabbed, when timing explains it: Bigarr saw the release earlier and
 * turned it down, or only saw it after the app's grab (and maybe its import, which the follow brought into Bigarr).
 */
export function TimingNote({ note, app, className }: { note: CompareTimingNote | null; app: string; className?: string }) {
  const { t } = useTranslation()
  if (!note) return null
  const gap = formatGap(note.gapSeconds)
  const parts: string[] = []
  switch (note.kind) {
    case 'seenBefore':
      parts.push(note.rejection ? t('compare.timing.seenBeforeRejected', { gap, reason: lowerFirst(note.rejection) }) : t('compare.timing.seenBeforeApproved', { gap }))
      break
    case 'seenAfterGrab':
      parts.push(t('compare.timing.seenAfterGrab', { gap, app }))
      break
    case 'seenAfterImport':
      parts.push(t('compare.timing.seenAfterImport', { gap, app }))
      break
    case 'neverSeen':
      parts.push(t('compare.timing.neverSeen'))
      break
  }
  if (note.kind !== 'seenBefore' && note.engineApproved) parts.push(t('compare.timing.engineApproved'))
  return (
    <span className={className ?? 'block text-xs text-muted-foreground'} data-testid="compare-timing" data-kind={note.kind}>
      {parts.join(' — ')}
    </span>
  )
}
