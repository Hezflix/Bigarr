import { Fragment } from 'react'
import { cn } from 'cn'
import { useTranslation } from 'react-i18next'
import type { MovieSummary, VersionSummary } from '@/api/client'
import { formatBytesShort } from '@/i18n'
import { sortedVersion, type sortKeys, type SortKey } from './libraryView'

type FiledVersion = VersionSummary & { size: number }

/**
 * "1080p 39 GB · 4k 82 GB": where a film's size goes, the default version first. Shown only when more than one of its
 * versions has a file; `emphasis` is the version the list is sorted by.
 */
export function SizeSplit({ movie, emphasis, className }: { movie: MovieSummary; emphasis: string | null; className?: string }) {
  const parts = movie.versions.filter((v): v is FiledVersion => v.size !== null && v.size > 0).sort((a, b) => Number(b.isDefault) - Number(a.isDefault))
  if (parts.length < 2) return null
  return (
    <span className={cn('block truncate text-xs text-muted-foreground tabular-nums', className)} data-testid="size-split">
      {parts.map((v, i) => (
        <Fragment key={v.id}>
          {i > 0 && ' · '}
          <span className={cn(v.name === emphasis && 'font-medium text-foreground')}>
            {v.name} <span className={cn(v.name !== emphasis && 'text-foreground/80')}>{formatBytesShort(v.size)}</span>
          </span>
        </Fragment>
      ))}
    </span>
  )
}

/** The label of a sort: "Year", "Size"… and "Size · 4k" for a version's size. */
export function useSortLabel() {
  const { t } = useTranslation()
  return (key: SortKey) => {
    const version = sortedVersion(key)
    return version === null ? t(`library.sort.${key as (typeof sortKeys)[number]}`) : t('library.sort.versionSize', { version })
  }
}
