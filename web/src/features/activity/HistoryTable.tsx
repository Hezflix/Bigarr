import { Link } from '@tanstack/react-router'
import { useTranslation } from 'react-i18next'
import type { HistoryEvent, HistoryEventType } from '@/api/client'
import { Badge } from '@/components/ui/badge'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { formatBytes, formatDateTime } from '@/i18n'

const eventStyles: Record<HistoryEventType, string> = {
  grabbed: 'border-blue-600/30 bg-blue-500/15 text-blue-700 dark:border-blue-400/30 dark:text-blue-300',
  imported: 'border-emerald-600/30 bg-emerald-500/15 text-emerald-700 dark:border-emerald-400/30 dark:text-emerald-300',
  upgraded: 'border-emerald-600/30 bg-emerald-500/15 text-emerald-700 dark:border-emerald-400/30 dark:text-emerald-300',
  renamed: 'border-border bg-muted text-muted-foreground',
  defaultChanged: 'border-border bg-muted text-muted-foreground',
  fileDeleted: 'border-amber-600/30 bg-amber-500/15 text-amber-700 dark:border-amber-400/30 dark:text-amber-300',
  importFailed: 'border-red-600/30 bg-red-500/10 text-red-700 dark:border-red-400/30 dark:text-red-300',
  downloadFailed: 'border-red-600/30 bg-red-500/10 text-red-700 dark:border-red-400/30 dark:text-red-300',
  blocklisted: 'border-red-600/30 bg-red-500/10 text-red-700 dark:border-red-400/30 dark:text-red-300',
}

/** The few data keys worth a glance; the rest sits in the tooltip. */
const headline: Record<HistoryEventType, string[]> = {
  grabbed: ['quality', 'downloadClient'],
  imported: ['quality', 'transfer', 'path'],
  upgraded: ['quality', 'previousQuality', 'transfer'],
  renamed: ['to'],
  defaultChanged: ['previousVersionName'],
  fileDeleted: ['path', 'recycledTo'],
  importFailed: ['reason'],
  downloadFailed: ['reason'],
  blocklisted: ['reason'],
}

/** The journal: what happened to which version, and why a file is there. */
export function HistoryTable({ events, showMovie = true }: { events: HistoryEvent[]; showMovie?: boolean }) {
  const { t } = useTranslation()

  return (
    <div className="overflow-x-auto rounded-lg border">
      <Table data-testid="history-table">
        <TableHeader>
          <TableRow>
            <TableHead>{t('history.date')}</TableHead>
            <TableHead>{t('history.event')}</TableHead>
            {showMovie && <TableHead>{t('history.movie')}</TableHead>}
            <TableHead>{t('history.version')}</TableHead>
            <TableHead>{t('history.source')}</TableHead>
            <TableHead>{t('history.details')}</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {events.map((event) => (
            <TableRow key={event.id} data-event={event.eventType}>
              <TableCell className="align-top whitespace-nowrap text-xs text-muted-foreground">{formatDateTime(event.createdAt)}</TableCell>
              <TableCell className="align-top">
                <span className="flex flex-wrap items-center gap-1">
                  <Badge variant="outline" className={eventStyles[event.eventType]}>
                    {t(`history.events.${event.eventType}`)}
                  </Badge>
                  {/* Copied from Radarr by the import or the follow (shadow mode): Radarr did it, not Bigarr. */}
                  {event.data.importedFrom === 'radarr' && (
                    <Badge variant="outline" className="border-border bg-muted text-muted-foreground" data-testid="history-from-radarr">
                      {t('history.fromRadarr')}
                    </Badge>
                  )}
                </span>
              </TableCell>
              {showMovie && (
                <TableCell className="align-top">
                  {event.movieId ? (
                    <Link to="/movies/$movieId" params={{ movieId: String(event.movieId) }} className="font-medium underline-offset-4 hover:underline">
                      {event.movieTitle}
                    </Link>
                  ) : (
                    <span className="text-muted-foreground">{event.movieTitle ?? t('common.none')}</span>
                  )}
                </TableCell>
              )}
              <TableCell className="align-top">{event.versionName ? <Badge variant="secondary">{event.versionName}</Badge> : <span className="text-muted-foreground">{t('common.none')}</span>}</TableCell>
              <TableCell className="min-w-[16rem] max-w-[28rem] align-top whitespace-normal">
                <span className="block break-all text-xs">{event.sourceTitle ?? t('common.none')}</span>
                {event.indexerName && <span className="text-xs text-muted-foreground">{event.indexerName}</span>}
              </TableCell>
              <TableCell className="min-w-[14rem] max-w-[28rem] align-top whitespace-normal text-xs text-muted-foreground">
                <Details event={event} />
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  )
}

function Details({ event }: { event: HistoryEvent }) {
  const keys = headline[event.eventType].filter((k) => event.data[k])
  const rest = Object.entries(event.data).filter(([k, v]) => v && !keys.includes(k))
  const format = (key: string, value: string) => (key === 'size' ? formatBytes(Number(value)) : value)
  return (
    <Tooltip>
      <TooltipTrigger asChild>
        <span className="block cursor-default">
          {keys.length === 0 && rest.length === 0 && '—'}
          {keys.map((k) => (
            <span key={k} className="block break-all">
              {k === 'reason' ? event.data[k] : `${k}: ${format(k, event.data[k])}`}
            </span>
          ))}
        </span>
      </TooltipTrigger>
      {rest.length > 0 && (
        <TooltipContent className="max-w-md whitespace-normal">
          {rest.map(([k, v]) => (
            <span key={k} className="block break-all">
              {k}: {format(k, v)}
            </span>
          ))}
        </TooltipContent>
      )}
    </Tooltip>
  )
}
