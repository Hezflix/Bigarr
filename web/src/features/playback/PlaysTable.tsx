import type { ReactNode } from 'react'
import { Link } from '@tanstack/react-router'
import { ArrowDownIcon, ArrowUpIcon, CheckIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import type { Play, PlaySortKey } from '@/api/client'
import { Badge } from '@/components/ui/badge'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { formatDateTime } from '@/i18n'
import { DecisionBadge, formatPlayed, itemLabel } from './format'

export type PlaySort = { key: PlaySortKey; direction: 'asc' | 'desc' }

/**
 * Plays, one row each. With a sort the headers sort; a click on a row opens its streams. `showUser` is off on one user's
 * page and `showItem` on one film's: a column that always says the same is left out.
 */
export function PlaysTable({
  plays,
  showItem = true,
  showUser = true,
  sort,
  onSort,
  onSelect,
}: {
  plays: Play[]
  showItem?: boolean
  showUser?: boolean
  sort?: PlaySort
  onSort?: (key: PlaySortKey) => void
  onSelect?: (play: Play) => void
}) {
  const { t } = useTranslation()
  const head = (key: PlaySortKey, label: string, className?: string) =>
    sort && onSort ? (
      <SortableHead column={key} sort={sort} onSort={onSort} className={className}>
        {label}
      </SortableHead>
    ) : (
      <TableHead className={className}>{label}</TableHead>
    )

  return (
    <div className="overflow-x-auto rounded-lg border">
      <Table data-testid="plays-table">
        <TableHeader>
          <TableRow>
            {head('startedAt', t('playback.started'))}
            {showUser && head('user', t('playback.user'))}
            {showItem && head('title', t('playback.title'))}
            {head('version', t('playback.version'))}
            <TableHead>{t('playback.player')}</TableHead>
            <TableHead>{t('playback.decision')}</TableHead>
            {head('played', t('playback.played'), 'text-right')}
            <TableHead className="text-right">{t('playback.progressColumn')}</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {plays.map((play) => {
            const progress = play.durationMs ? Math.min(100, Math.round((play.maxOffsetMs / play.durationMs) * 100)) : null
            return (
              <TableRow
                key={play.id}
                data-play={play.id}
                className={onSelect ? 'cursor-pointer' : undefined}
                onClick={onSelect ? (e) => !(e.target as HTMLElement).closest('a') && onSelect(play) : undefined}
              >
                <TableCell className="whitespace-nowrap text-xs text-muted-foreground">{formatDateTime(play.startedAt)}</TableCell>
                {showUser && <TableCell className="whitespace-nowrap">{play.user.name}</TableCell>}
                {showItem && (
                  <TableCell className="min-w-[14rem] whitespace-normal">
                    {play.movieId ? (
                      <Link to="/movies/$movieId" params={{ movieId: String(play.movieId) }} className="font-medium underline-offset-4 hover:underline">
                        {itemLabel(play.item)}
                      </Link>
                    ) : (
                      <span>{itemLabel(play.item)}</span>
                    )}
                  </TableCell>
                )}
                <TableCell>{play.versionName ? <Badge variant="secondary">{play.versionName}</Badge> : <span className="text-muted-foreground">{t('common.none')}</span>}</TableCell>
                <TableCell className="whitespace-nowrap text-sm">
                  {play.player ?? t('common.none')}
                  {play.platform && <span className="text-muted-foreground"> · {play.platform}</span>}
                </TableCell>
                <TableCell>
                  <DecisionBadge decision={play.decision} />
                </TableCell>
                <TableCell className="whitespace-nowrap text-right tabular-nums">
                  {formatPlayed(play.playedSeconds)}
                  {play.streams > 1 && <span className="text-xs text-muted-foreground"> · {t('playback.sessions', { count: play.streams })}</span>}
                </TableCell>
                <TableCell className="whitespace-nowrap text-right tabular-nums">
                  {play.watched ? (
                    <span className="inline-flex items-center gap-1 text-emerald-700 dark:text-emerald-300">
                      <CheckIcon className="size-3.5" aria-hidden />
                      {t('playback.watched')}
                    </span>
                  ) : progress !== null ? (
                    `${progress} %`
                  ) : (
                    t('common.none')
                  )}
                </TableCell>
              </TableRow>
            )
          })}
        </TableBody>
      </Table>
    </div>
  )
}

function SortableHead({ column, sort, onSort, className, children }: { column: PlaySortKey; sort: PlaySort; onSort: (key: PlaySortKey) => void; className?: string; children: ReactNode }) {
  const active = sort.key === column
  return (
    <TableHead className={className} aria-sort={active ? (sort.direction === 'asc' ? 'ascending' : 'descending') : 'none'}>
      <button type="button" className={`inline-flex items-center gap-1 hover:text-foreground ${className?.includes('text-right') ? 'flex-row-reverse' : ''}`} onClick={() => onSort(column)} data-testid={`sort-${column}`}>
        {children}
        {active && (sort.direction === 'asc' ? <ArrowUpIcon className="size-3" /> : <ArrowDownIcon className="size-3" />)}
      </button>
    </TableHead>
  )
}
