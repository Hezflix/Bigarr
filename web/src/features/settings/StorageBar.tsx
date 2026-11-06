import { useMemo } from 'react'
import { cn } from 'cn'
import { useTranslation } from 'react-i18next'
import type { MovieSummary, RootFolder } from '@/api/client'
import i18n, { formatBytes, formatBytesShort } from '@/i18n'

/** One colour per root folder, away from the status colours (amber, blue, emerald, violet) used elsewhere. */
const tones = ['bg-cyan-500', 'bg-fuchsia-500', 'bg-orange-500', 'bg-lime-500', 'bg-rose-500']

export type FolderUsage = { id: number; path: string; bytes: number; files: number }

/**
 * Bytes and files per root folder, in the order of `roots`, leaving out the folders without a file. Counts the movie
 * files only: extra files (subtitles…) have no stored size.
 */
export function usagePerRootFolder(movies: MovieSummary[], roots: RootFolder[]): FolderUsage[] {
  const usage = new Map(roots.map((r) => [r.id, { id: r.id, path: r.path, bytes: 0, files: 0 }]))
  for (const movie of movies) {
    for (const v of movie.versions) {
      const entry = usage.get(v.rootFolderId)
      if (entry && v.size) {
        entry.bytes += v.size
        entry.files++
      }
    }
  }
  return [...usage.values()].filter((f) => f.files > 0)
}

/**
 * Free space over the root folders, each drive counted once. Folders on the same drive (or bind mounts of one disk) report
 * the same free and total space, so an equal pair is taken for one drive. Null when a root folder cannot tell: a sum
 * without it would read as the whole.
 */
export function freeSpace(roots: RootFolder[]): number | null {
  if (roots.length === 0 || roots.some((r) => r.freeSpace === null || r.totalSpace === null)) return null
  const drives = new Map(roots.map((r) => [`${r.freeSpace}/${r.totalSpace}`, r.freeSpace ?? 0]))
  return [...drives.values()].reduce((sum, free) => sum + free, 0)
}

const share = (part: number, whole: number) => new Intl.NumberFormat(i18n.language, { style: 'percent', maximumSignificantDigits: 2 }).format(part / whole)

/**
 * How much space each root folder holds, then the free space left, shown only when every root folder reports it.
 * Nothing renders before the library has a file.
 */
export function StorageBar({ movies, roots }: { movies: MovieSummary[]; roots: RootFolder[] }) {
  const { t } = useTranslation()
  const usage = useMemo(() => usagePerRootFolder(movies, roots), [movies, roots])
  const free = useMemo(() => freeSpace(roots), [roots])
  const used = usage.reduce((sum, f) => sum + f.bytes, 0)
  const files = usage.reduce((sum, f) => sum + f.files, 0)
  if (files === 0) return null

  const capacity = used + (free ?? 0)
  const tone = (id: number) => tones[Math.max(0, roots.findIndex((r) => r.id === id)) % tones.length]

  return (
    <div className="flex flex-col gap-2.5 rounded-lg border px-4 py-3" data-testid="storage">
      <div className="flex flex-wrap items-baseline gap-x-3 gap-y-1 text-sm">
        <span className="font-medium">{t('settings.storage.title')}</span>
        <span className="text-muted-foreground tabular-nums" data-testid="storage-used">
          {t('settings.storage.used', { size: formatBytesShort(used), count: files })}
        </span>
        <span className="ml-auto text-xs text-muted-foreground tabular-nums" data-testid="storage-free">
          {free !== null ? t('settings.storage.free', { size: formatBytesShort(free) }) : <span title={t('settings.storage.freeUnknownHint')}>{t('settings.storage.freeUnknown')}</span>}
        </span>
      </div>
      <div className="flex h-3.5 w-full overflow-hidden rounded-full bg-muted" role="img" aria-label={t('settings.storage.bar')}>
        {usage.map((f) => (
          <div
            key={f.id}
            className={cn('h-full min-w-1 border-r-2 border-background', free === null && 'last:border-r-0', tone(f.id))}
            style={{ width: `${(f.bytes / capacity) * 100}%` }}
            title={t('settings.storage.segment', { path: f.path, size: formatBytes(f.bytes), count: f.files, share: share(f.bytes, used) })}
            data-testid={`storage-segment:${f.id}`}
          />
        ))}
      </div>
      <div className="flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-muted-foreground">
        {usage.map((f) => (
          <span key={f.id} className="inline-flex items-center gap-1.5">
            <span className={cn('size-2.5 shrink-0 rounded-full', tone(f.id))} aria-hidden="true" />
            <span className="font-mono break-all">{f.path}</span>
            <span className="text-foreground tabular-nums">{formatBytesShort(f.bytes)}</span>
            <span className="tabular-nums">· {t('settings.storage.files', { count: f.files })}</span>
          </span>
        ))}
        {free !== null && (
          <span className="inline-flex items-center gap-1.5">
            <span className="size-2.5 rounded-full bg-muted-foreground/25" aria-hidden="true" />
            {t('settings.storage.freeLabel')}
            <span className="text-foreground tabular-nums">{formatBytesShort(free)}</span>
          </span>
        )}
      </div>
    </div>
  )
}
