import { cn } from 'cn'
import { CircleSlashIcon, CircleXIcon, DownloadIcon, GhostIcon, HardDriveIcon, HistoryIcon, type LucideIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import type { Release } from '@/api/client'
import { Badge } from '@/components/ui/badge'
import { formatDateTime } from '@/i18n'

export type ReleasePick = NonNullable<Release['picks']>[number]
export type PickKind = ReleasePick['kind']

/** Proposals for showing the release picked (?picked=1..5). Throwaway: the owner picks one. */
export function pickedVariant(): number {
  const n = Number(new URLSearchParams(window.location.search).get('picked') ?? '1')
  return n >= 1 && n <= 5 ? n : 1
}

export const pickIcons: Record<PickKind, LucideIcon> = {
  downloading: DownloadIcon,
  onDisk: HardDriveIcon,
  imported: HistoryIcon,
  failed: CircleXIcon,
  grabbed: CircleSlashIcon,
  shadow: GhostIcon,
}

export const pickStyles: Record<PickKind, string> = {
  downloading: 'border-blue-600/30 bg-blue-500/15 text-blue-700 dark:border-blue-400/30 dark:text-blue-300',
  onDisk: 'border-emerald-600/30 bg-emerald-500/15 text-emerald-700 dark:border-emerald-400/30 dark:text-emerald-300',
  imported: 'text-muted-foreground',
  failed: 'border-red-600/30 bg-red-500/15 text-red-700 dark:border-red-400/30 dark:text-red-300',
  grabbed: 'text-muted-foreground',
  shadow: 'border-violet-600/30 bg-violet-500/15 text-violet-700 dark:border-violet-400/30 dark:text-violet-300',
}

/** The picks that matter on a row: what a version holds or is downloading come first. */
export function picksOf(release: Release): ReleasePick[] {
  const rank: Record<PickKind, number> = { downloading: 0, onDisk: 1, shadow: 2, failed: 3, imported: 4, grabbed: 5 }
  return [...(release.picks ?? [])].sort((a, b) => rank[a.kind] - rank[b.kind])
}

/** The row is the user's current choice: downloading, on disk, or the shadow pick. Older grabs only get the badge. */
export function isCurrentPick(release: Release): boolean {
  return (release.picks ?? []).some((p) => p.kind === 'downloading' || p.kind === 'onDisk' || p.kind === 'shadow')
}

export function PickBadge({ pick, className }: { pick: ReleasePick; className?: string }) {
  const { t } = useTranslation()
  const Icon = pickIcons[pick.kind]
  return (
    <Badge variant="outline" className={cn(pickStyles[pick.kind], className)} title={t('search.pick.at', { date: formatDateTime(pick.at) })} data-testid="release-pick">
      <Icon aria-hidden="true" />
      {t('search.pick.badge', { version: pick.versionName, kind: t(`search.pick.kind.${pick.kind}`) })}
    </Badge>
  )
}
