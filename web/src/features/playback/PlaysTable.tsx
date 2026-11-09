import { Link } from '@tanstack/react-router'
import { CheckIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import type { Play } from '@/api/client'
import { Badge } from '@/components/ui/badge'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { formatDateTime } from '@/i18n'
import { DecisionBadge, formatPlayed, itemLabel } from './format'

export function PlaysTable({ plays, showItem = true }: { plays: Play[]; showItem?: boolean }) {
  const { t } = useTranslation()

  return (
    <div className="overflow-x-auto rounded-lg border">
      <Table data-testid="plays-table">
        <TableHeader>
          <TableRow>
            <TableHead>{t('playback.started')}</TableHead>
            <TableHead>{t('playback.user')}</TableHead>
            {showItem && <TableHead>{t('playback.title')}</TableHead>}
            <TableHead>{t('playback.version')}</TableHead>
            <TableHead>{t('playback.player')}</TableHead>
            <TableHead>{t('playback.decision')}</TableHead>
            <TableHead className="text-right">{t('playback.played')}</TableHead>
            <TableHead className="text-right">{t('playback.progressColumn')}</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {plays.map((play) => {
            const progress = play.durationMs ? Math.min(100, Math.round((play.maxOffsetMs / play.durationMs) * 100)) : null
            return (
              <TableRow key={play.id}>
                <TableCell className="whitespace-nowrap text-xs text-muted-foreground">{formatDateTime(play.startedAt)}</TableCell>
                <TableCell className="whitespace-nowrap">{play.user.name}</TableCell>
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
