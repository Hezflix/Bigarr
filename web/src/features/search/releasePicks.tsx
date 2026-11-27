import { useEffect, useState, type RefObject } from 'react'
import { cn } from 'cn'
import { ChevronRightIcon, CircleSlashIcon, CircleXIcon, DownloadIcon, GhostIcon, HardDriveIcon, HistoryIcon, type LucideIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import type { components } from '@/api/generated/schema'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { formatDateTime } from '@/i18n'

export type ReleasePick = components['schemas']['ReleasePickResponse']
export type PickKind = ReleasePick['kind']

/** What a release row needs to show its picks: films and series alike. */
type Picked = { indexerId: number; guid: string; title: string; indexer: string; picks?: ReleasePick[] | null }

export const releaseKey = (release: { indexerId: number; guid: string }) => `${release.indexerId}:${release.guid}`

const icons: Record<PickKind, LucideIcon> = {
  downloading: DownloadIcon,
  onDisk: HardDriveIcon,
  imported: HistoryIcon,
  failed: CircleXIcon,
  grabbed: CircleSlashIcon,
  shadow: GhostIcon,
}

const styles: Record<PickKind, string> = {
  downloading: 'border-blue-600/30 bg-blue-500/15 text-blue-700 dark:border-blue-400/30 dark:text-blue-300',
  onDisk: 'border-emerald-600/30 bg-emerald-500/15 text-emerald-700 dark:border-emerald-400/30 dark:text-emerald-300',
  imported: 'text-muted-foreground',
  failed: 'border-red-600/30 bg-red-500/15 text-red-700 dark:border-red-400/30 dark:text-red-300',
  grabbed: 'text-muted-foreground',
  shadow: 'border-violet-600/30 bg-violet-500/15 text-violet-700 dark:border-violet-400/30 dark:text-violet-300',
}

const rank: Record<PickKind, number> = { downloading: 0, onDisk: 1, shadow: 2, failed: 3, imported: 4, grabbed: 5 }

/** The picks of a row, what a version holds or is downloading first. */
export function picksOf(release: Picked): ReleasePick[] {
  return [...(release.picks ?? [])].sort((a, b) => rank[a.kind] - rank[b.kind])
}

/** Downloading, on disk or Bigarr's shadow pick: the row is a current choice and is tinted. Older grabs only get a badge. */
function isCurrent(release: Picked): boolean {
  return (release.picks ?? []).some((p) => p.kind === 'downloading' || p.kind === 'onDisk' || p.kind === 'shadow')
}

/** Classes for a release row: tinted when it is a current pick, lit for a moment when just grabbed or asked for. */
export function pickedRowClass(release: Picked, flash: string | null): string | false {
  return cn(isCurrent(release) && 'bg-primary/[0.07] hover:bg-primary/10', flash === releaseKey(release) && 'bg-primary/20 transition-colors duration-700')
}

export function PickBadge({ pick }: { pick: ReleasePick }) {
  const { t } = useTranslation()
  const Icon = icons[pick.kind]
  return (
    <Badge variant="outline" className={styles[pick.kind]} title={t('search.pick.at', { date: formatDateTime(pick.at) })} data-testid="release-pick">
      <Icon aria-hidden="true" />
      {t('search.pick.badge', { version: pick.versionName, kind: t(`search.pick.kind.${pick.kind}`) })}
    </Badge>
  )
}

/** The badges of a row, first in its badge line. */
export function PickBadges({ release }: { release: Picked }) {
  const picks = picksOf(release)
  return picks.length === 0 ? null : (
    <>
      {picks.map((pick) => (
        <PickBadge key={pick.versionId} pick={pick} />
      ))}
    </>
  )
}

/** Whether the banner is folded, per browser: a long list of picks can push the results off the screen. */
export const picksFoldedKey = 'bigarr.search.picksFolded'

function readFolded(): boolean {
  try {
    return globalThis.localStorage?.getItem(picksFoldedKey) === '1'
  } catch {
    return false
  }
}

function writeFolded(folded: boolean): void {
  try {
    globalThis.localStorage?.setItem(picksFoldedKey, folded ? '1' : '0')
  } catch {
    // Not remembered: the toggle still works for this page.
  }
}

/** One line per pick above the table, each with a link to its row. The title folds the list down to a count. */
export function PicksBanner({ releases, onShow }: { releases: Picked[]; onShow: (key: string) => void }) {
  const { t } = useTranslation()
  const [folded, setFolded] = useState(readFolded)
  const picked = releases.filter((r) => (r.picks ?? []).length > 0)
  if (picked.length === 0) return null
  const count = picked.reduce((sum, r) => sum + (r.picks ?? []).length, 0)
  const toggle = () => {
    setFolded(!folded)
    writeFolded(!folded)
  }
  return (
    <div className="flex flex-col gap-1.5 rounded-lg border bg-muted/40 p-3" data-testid="search-picks">
      <button type="button" className="flex items-center gap-1 text-left text-xs font-medium text-muted-foreground" onClick={toggle} aria-expanded={!folded} data-testid="search-picks-toggle">
        <ChevronRightIcon className={cn('size-3.5 transition-transform', !folded && 'rotate-90')} aria-hidden="true" />
        {t('search.pick.bannerTitle')}
        <span className="tabular-nums">({count})</span>
      </button>
      {!folded &&
        picked.flatMap((release) =>
          picksOf(release).map((pick) => (
            <div key={`${releaseKey(release)}:${pick.versionId}`} className="flex flex-wrap items-center gap-2 text-sm">
              <PickBadge pick={pick} />
              <span className="min-w-0 flex-1 truncate" title={release.title}>
                {release.title}
              </span>
              <span className="text-xs text-muted-foreground">{release.indexer}</span>
              <Button type="button" variant="link" size="sm" className="h-auto p-0" onClick={() => onShow(releaseKey(release))} data-testid="search-pick-show">
                {t('search.pick.showInList')}
              </Button>
            </div>
          )),
        )}
    </div>
  )
}

/** The row to light: set it after a grab or from the banner; it scrolls into view and fades after a moment. */
export function useFlashRow(section: RefObject<HTMLElement | null>): [string | null, (key: string) => void] {
  const [flash, setFlash] = useState<string | null>(null)
  useEffect(() => {
    if (flash === null) return
    section.current?.querySelector(`[data-release-key="${CSS.escape(flash)}"]`)?.scrollIntoView({ behavior: 'smooth', block: 'nearest' })
    const timer = setTimeout(() => setFlash(null), 2400)
    return () => clearTimeout(timer)
  }, [flash, section])
  return [flash, setFlash]
}
