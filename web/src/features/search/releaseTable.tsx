import { useState, type ReactNode } from 'react'
import { cn } from 'cn'
import { ArrowDownIcon, ArrowUpIcon, CheckIcon, ChevronDownIcon, ClockIcon, DownloadIcon, ExternalLinkIcon, XIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import type { GrabRequest, Release, VersionDecision } from '@/api/client'
import { Alert, AlertDescription } from '@/components/ui/alert'
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { DropdownMenu, DropdownMenuContent, DropdownMenuLabel, DropdownMenuTrigger } from '@/components/ui/dropdown-menu'
import { Skeleton } from '@/components/ui/skeleton'
import { TableCell, TableHead } from '@/components/ui/table'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { formatBytes } from '@/i18n'

/** What the film and series searches render the same way: the parts of a release that do not depend on what it is a release of. */

export const approvedStyle = 'border-emerald-600/30 bg-emerald-500/15 text-emerald-700 dark:border-emerald-400/30 dark:text-emerald-300'
export const temporaryStyle = 'border-amber-600/30 bg-amber-500/15 text-amber-700 dark:border-amber-400/30 dark:text-amber-300'
export const permanentStyle = 'border-red-600/30 bg-red-500/10 text-red-700 dark:border-red-400/30 dark:text-red-300'

/** The fields both kinds of release share. */
export type ReleaseCommon = Pick<
  Release,
  | 'guid'
  | 'title'
  | 'indexerId'
  | 'indexer'
  | 'protocol'
  | 'size'
  | 'seeders'
  | 'leechers'
  | 'publishDate'
  | 'downloadUrl'
  | 'magnetUrl'
  | 'infoUrl'
  | 'infoHash'
  | 'flags'
  | 'quality'
  | 'resolution'
  | 'revisionVersion'
  | 'languages'
  | 'customFormats'
>

/** "fit" is the server's ranking (best first, fixed direction, like Radarr's release weight); the others toggle. */
export type Sort<K extends string> = { key: K | 'fit'; direction: 'asc' | 'desc' }

const collator = new Intl.Collator(undefined, { sensitivity: 'base', numeric: true })

/** Ascending comparators; ties keep the server's ranking (the sort is stable). */
export const commonComparators: Record<'age' | 'title' | 'indexer' | 'size' | 'peers', (a: ReleaseCommon, b: ReleaseCommon) => number> = {
  age: (a, b) => Date.parse(b.publishDate) - Date.parse(a.publishDate),
  title: (a, b) => collator.compare(a.title, b.title),
  indexer: (a, b) => collator.compare(a.indexer, b.indexer),
  size: (a, b) => a.size - b.size,
  peers: (a, b) => (a.seeders ?? -1) - (b.seeders ?? -1),
}

export function sortReleases<R, K extends string>(releases: R[], sort: Sort<K>, comparators: Record<K, (a: R, b: R) => number>): R[] {
  if (sort.key === 'fit') return releases
  const compare = comparators[sort.key]
  const sign = sort.direction === 'asc' ? 1 : -1
  return [...releases].sort((a, b) => sign * compare(a, b))
}

/** The server's ranking first; a click on a column sorts by it, a second click reverses it. */
export function useReleaseSort<K extends string>() {
  const [sort, setSort] = useState<Sort<K>>({ key: 'fit', direction: 'asc' })
  const onSort = (key: K | 'fit') =>
    setSort((current) => (key === 'fit' ? { key, direction: 'asc' } : current.key === key ? { key, direction: current.direction === 'asc' ? 'desc' : 'asc' } : { key, direction: 'asc' }))
  return [sort, onSort] as const
}

export function SortableHead<K extends string>({
  column,
  sort,
  onSort,
  title,
  testIdPrefix,
  children,
}: {
  column: K | 'fit'
  sort: Sort<K>
  onSort: (key: K | 'fit') => void
  title?: string
  testIdPrefix: string
  children: ReactNode
}) {
  const active = sort.key === column
  return (
    <TableHead aria-sort={active ? (sort.direction === 'asc' ? 'ascending' : 'descending') : 'none'}>
      <button type="button" className="flex items-center gap-1 text-left hover:text-foreground" onClick={() => onSort(column)} title={title} data-testid={`${testIdPrefix}-${column}`}>
        {children}
        {active && (sort.direction === 'asc' ? <ArrowUpIcon className="size-3" /> : <ArrowDownIcon className="size-3" />)}
      </button>
    </TableHead>
  )
}

/** ✓ accepted, clock when only temporary reasons stand in the way, ✗ otherwise. */
export function VerdictIcon({ decision }: { decision: VersionDecision | undefined }) {
  if (!decision) return <span className="size-3.5" />
  if (decision.approved) return <CheckIcon className="size-3.5 text-emerald-600 dark:text-emerald-400" aria-hidden="true" />
  if (decision.rejections.every((r) => r.temporary)) return <ClockIcon className="size-3.5 text-amber-600 dark:text-amber-400" aria-hidden="true" />
  return <XIcon className="size-3.5 text-red-600 dark:text-red-400" aria-hidden="true" />
}

export function SearchSkeleton() {
  return (
    <div className="flex flex-col gap-2">
      <Skeleton className="h-8 w-full" />
      <Skeleton className="h-8 w-full" />
      <Skeleton className="h-8 w-2/3" />
    </div>
  )
}

export function IndexerErrors({ errors }: { errors: { indexerId: number; indexer: string; message: string }[] }) {
  if (errors.length === 0) return null
  return (
    <Alert>
      <AlertDescription>
        <ul className="flex flex-col gap-1">
          {errors.map((e) => (
            <li key={e.indexerId}>
              <span className="font-medium">{e.indexer}</span>: {e.message}
            </li>
          ))}
        </ul>
      </AlertDescription>
    </Alert>
  )
}

export const isGrabbable = (release: ReleaseCommon) => release.protocol === 'torrent' && (!!release.downloadUrl || !!release.magnetUrl || !!release.infoHash)

/** The release as the search returned it (the search keeps no state), for the version picked. */
export function grabRequest(release: ReleaseCommon, versionId: number): GrabRequest {
  return {
    versionId,
    indexerId: release.indexerId,
    guid: release.guid,
    title: release.title,
    downloadUrl: release.downloadUrl,
    magnetUrl: release.magnetUrl,
    infoHash: release.infoHash,
    size: release.size,
    seeders: release.seeders,
    flags: release.flags,
    protocol: release.protocol,
    indexer: release.indexer,
    publishDate: release.publishDate,
    infoUrl: release.infoUrl,
  }
}

/** Title (linked to the indexer's page when there is one), then quality, flags, custom formats and the caller's details. */
export function ReleaseTitleCell({
  release,
  good,
  details,
  className,
  badges = true,
}: {
  release: ReleaseCommon
  good: boolean
  details: string[]
  className: string
  /** False when quality and custom formats have their own columns. */
  badges?: boolean
}) {
  const { t } = useTranslation()
  const flags = release.flags === 'none' ? [] : release.flags.split(', ')
  return (
    <TableCell className={cn('align-top whitespace-normal', className)}>
      <span className={cn('flex items-start gap-1 break-all', good && 'font-medium text-foreground')}>
        {release.infoUrl ? (
          <a href={release.infoUrl} target="_blank" rel="noreferrer" className="inline-flex items-center gap-1 underline-offset-4 hover:underline">
            {release.title}
            <ExternalLinkIcon className="size-3 shrink-0" />
          </a>
        ) : (
          release.title
        )}
      </span>
      <span className="mt-1 flex flex-wrap items-center gap-1">
        {badges && <Badge variant="secondary">{release.quality}</Badge>}
        {flags.map((flag) => (
          <Badge key={flag} variant="outline">
            {t(`search.flags.${flag}`, { defaultValue: flag })}
          </Badge>
        ))}
        {badges && release.customFormats.map((format) => <FormatBadge key={format} format={format} />)}
        {details.length > 0 && <span className="text-xs text-muted-foreground">{details.join(' · ')}</span>}
      </span>
    </TableCell>
  )
}

export function IndexerSizePeersCells({ release }: { release: ReleaseCommon }) {
  return (
    <>
      <TableCell className="align-top whitespace-nowrap">{release.indexer}</TableCell>
      <TableCell className="align-top whitespace-nowrap">{formatBytes(release.size)}</TableCell>
      <TableCell className="align-top whitespace-nowrap">
        {release.seeders ?? '—'} <span className="text-muted-foreground">/ {release.leechers ?? '—'}</span>
      </TableCell>
    </>
  )
}

/** One line per version: its verdict, its custom format score, then "accepted" or every reason it refuses. */
export function VersionVerdicts({ decisions, matches }: { decisions: VersionDecision[]; matches: boolean }) {
  const { t } = useTranslation()
  return (
    <ul className="flex flex-col gap-1">
      {decisions.map((v) => (
        <li key={v.versionId} className="flex items-start gap-1">
          <span className="flex h-5 shrink-0 items-center gap-1">
            <VerdictIcon decision={v} />
            <span className={cn('text-xs font-medium whitespace-nowrap', v.approved && matches && 'text-foreground')}>
              {v.versionName}
              {v.customFormatScore !== 0 && <span className="font-normal text-muted-foreground"> · {t('search.score', { score: v.customFormatScore })}</span>}
            </span>
          </span>
          <span className="flex flex-wrap gap-1">
            {v.approved ? (
              <Badge variant="outline" className={approvedStyle}>
                {t('search.approved')}
              </Badge>
            ) : (
              v.rejections.map((r) => (
                <Badge key={r.reason} variant="outline" className={cn('h-auto rounded-md whitespace-normal text-left', r.temporary ? temporaryStyle : permanentStyle)}>
                  {r.reason}
                </Badge>
              ))
            )}
          </span>
        </li>
      ))}
    </ul>
  )
}

/** The grab button for the suggested version, and beside it the menu to send the release elsewhere (its items are the caller's). */
export function GrabSplitButton({
  accepted,
  tip,
  targetName,
  grabbing,
  disabled,
  menuDisabled,
  onGrab,
  testId,
  menuTestId,
  menuClassName,
  children,
}: {
  accepted: boolean
  tip: string
  targetName: string | undefined
  grabbing: boolean
  disabled: boolean
  menuDisabled: boolean
  onGrab: () => void
  testId: string
  menuTestId?: string
  menuClassName: string
  children: ReactNode
}) {
  const { t } = useTranslation()
  return (
    <div className="flex justify-end">
      <Tooltip>
        <TooltipTrigger asChild>
          <span>
            <Button type="button" variant={accepted ? 'default' : 'outline'} size="sm" className="max-w-40 rounded-r-none" onClick={onGrab} disabled={disabled} aria-label={tip} data-testid={testId}>
              <DownloadIcon className={grabbing ? 'animate-pulse' : undefined} />
              <span className="truncate">{targetName}</span>
            </Button>
          </span>
        </TooltipTrigger>
        <TooltipContent>{tip}</TooltipContent>
      </Tooltip>
      <DropdownMenu>
        <DropdownMenuTrigger asChild>
          <Button
            type="button"
            variant={accepted ? 'default' : 'outline'}
            size="sm"
            className="rounded-l-none border-l-0 px-1.5"
            disabled={menuDisabled}
            aria-label={t('search.grabTo')}
            data-testid={menuTestId}
          >
            <ChevronDownIcon />
          </Button>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="end" className={menuClassName}>
          <DropdownMenuLabel>{t('search.grabTo')}</DropdownMenuLabel>
          {children}
        </DropdownMenuContent>
      </DropdownMenu>
    </div>
  )
}

export function FormatBadge({ format }: { format: string }) {
  return (
    <Badge variant="outline" className="border-violet-600/30 bg-violet-500/10 text-violet-700 dark:border-violet-400/30 dark:text-violet-300">
      {format}
    </Badge>
  )
}

/** Quality, languages, then the custom formats with the score the target version's profile gives them. */
export function QualityLanguagesFormatsCells({ release, score, scoreVersion }: { release: ReleaseCommon; score: number | undefined; scoreVersion: string | undefined }) {
  const { t } = useTranslation()
  return (
    <>
      <TableCell className="align-top whitespace-nowrap" data-testid="release-quality">
        <Badge variant="secondary">{release.quality}</Badge>
      </TableCell>
      <TableCell className="align-top text-xs" data-testid="release-languages">
        {release.languages.length > 0 ? release.languages.join(', ') : <span className="text-muted-foreground">—</span>}
      </TableCell>
      <TableCell className="min-w-[8rem] align-top whitespace-normal" data-testid="release-formats">
        <span className="flex flex-wrap items-center gap-1">
          {release.customFormats.length === 0 && !score && <span className="text-muted-foreground">—</span>}
          {score !== undefined && (release.customFormats.length > 0 || score !== 0) && (
            <span className="text-xs font-medium tabular-nums" title={scoreVersion ? t('search.scoreFor', { version: scoreVersion }) : undefined}>
              {score > 0 ? `+${score}` : score}
            </span>
          )}
          {release.customFormats.map((format) => (
            <FormatBadge key={format} format={format} />
          ))}
        </span>
      </TableCell>
    </>
  )
}

/** A rejected release grabbed on purpose: the reasons, asked once before it goes. */
export type PendingGrab = { title: string; version: string; reasons: string[]; go: () => void }

/** Why the search would not pick a release, as the confirmations list them. */
export function GrabReasons({ reasons }: { reasons: string[] }) {
  const { t } = useTranslation()
  return (
    <div className="flex flex-col gap-2 text-sm" data-testid="grab-reasons">
      <p>{t('search.confirmReasons')}</p>
      <ul className="flex flex-wrap gap-1">
        {reasons.map((reason) => (
          <li key={reason}>
            <Badge variant="outline" className={cn('h-auto rounded-md whitespace-normal text-left', permanentStyle)}>
              {reason}
            </Badge>
          </li>
        ))}
      </ul>
    </div>
  )
}

export function GrabConfirmDialog({ pending, onClose }: { pending: PendingGrab | null; onClose: () => void }) {
  const { t } = useTranslation()
  return (
    <AlertDialog open={pending !== null} onOpenChange={(open) => !open && onClose()}>
      {pending && (
        <AlertDialogContent data-testid="grab-confirm">
          <AlertDialogHeader>
            <AlertDialogTitle>{t('search.confirmTitle', { version: pending.version })}</AlertDialogTitle>
            <AlertDialogDescription className="break-all">{pending.title}</AlertDialogDescription>
          </AlertDialogHeader>
          <GrabReasons reasons={pending.reasons} />
          <AlertDialogFooter>
            <AlertDialogCancel>{t('common.cancel')}</AlertDialogCancel>
            <AlertDialogAction
              onClick={() => {
                pending.go()
                onClose()
              }}
              data-testid="grab-confirm-go"
            >
              {t('search.confirmGrab')}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      )}
    </AlertDialog>
  )
}
