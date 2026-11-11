import { useEffect, useRef, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { cn } from 'cn'
import { FolderInputIcon, RefreshCwIcon, RotateCcwIcon, Trash2Icon, WifiIcon, WifiOffIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ApiError, queue as queueApi, type Queue, type QueueEpisode, type QueueItem, type StatusMessage } from '@/api/client'
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
import { Checkbox } from '@/components/ui/checkbox'
import { Label } from '@/components/ui/label'
import { Progress } from '@/components/ui/progress'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { ManualImportDialog } from '@/features/import/ManualImportDialog'
import { SeriesManualImportDialog } from '@/features/import/SeriesManualImportDialog'
import { formatBytes, formatDateTime } from '@/i18n'

const statusStyles: Record<QueueItem['status'], string> = {
  downloading: 'border-blue-600/30 bg-blue-500/15 text-blue-700 dark:border-blue-400/30 dark:text-blue-300',
  queued: 'border-border bg-muted text-muted-foreground',
  paused: 'border-amber-600/30 bg-amber-500/15 text-amber-700 dark:border-amber-400/30 dark:text-amber-300',
  stalled: 'border-amber-600/30 bg-amber-500/15 text-amber-700 dark:border-amber-400/30 dark:text-amber-300',
  completed: 'border-emerald-600/30 bg-emerald-500/15 text-emerald-700 dark:border-emerald-400/30 dark:text-emerald-300',
  failed: 'border-red-600/30 bg-red-500/10 text-red-700 dark:border-red-400/30 dark:text-red-300',
  unknown: 'border-border bg-muted text-muted-foreground',
}

const severityStyles: Record<StatusMessage['severity'], string> = {
  info: 'border-border bg-muted text-muted-foreground',
  warning: 'border-amber-600/30 bg-amber-500/15 text-amber-700 dark:border-amber-400/30 dark:text-amber-300',
  error: 'border-red-600/30 bg-red-500/10 text-red-700 dark:border-red-400/30 dark:text-red-300',
}

/** "2h 05m", "45s": what is left of a download. */
function formatEta(seconds: number | null): string {
  if (seconds === null) return ''
  if (seconds < 60) return `${seconds}s`
  const hours = Math.floor(seconds / 3600)
  const minutes = Math.floor((seconds % 3600) / 60)
  return hours > 0 ? `${hours}h ${String(minutes).padStart(2, '0')}m` : `${minutes}m`
}

/**
 * Keeps the queue in sync over the SSE stream (the server pushes a snapshot after every poll); while the stream is
 * down the query falls back to polling. Returns whether the stream is currently open.
 */
function useQueueStream(): boolean {
  const queryClient = useQueryClient()
  const [live, setLive] = useState(false)
  const liveRef = useRef(false)

  useEffect(() => {
    let source: EventSource | null = null
    let retry: ReturnType<typeof setTimeout> | null = null
    let closed = false

    const connect = () => {
      source = new EventSource(queueApi.eventsUrl)
      source.addEventListener('queue', (event) => {
        const snapshot = JSON.parse((event as MessageEvent<string>).data) as Queue
        queryClient.setQueryData(['queue'], snapshot)
        if (!liveRef.current) {
          liveRef.current = true
          setLive(true)
        }
      })
      source.onerror = () => {
        source?.close()
        liveRef.current = false
        setLive(false)
        if (!closed) retry = setTimeout(connect, 5_000)
      }
    }

    connect()
    return () => {
      closed = true
      if (retry) clearTimeout(retry)
      source?.close()
    }
  }, [queryClient])

  return live
}

/** The download clients' state joined to the grabs (blueprint §3.5): a projection, refreshed by every poll. */
export function QueuePage() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const live = useQueueStream()
  const query = useQuery({ queryKey: ['queue'], queryFn: queueApi.get, refetchInterval: live ? false : 10_000 })
  const [pendingRemove, setPendingRemove] = useState<QueueItem | null>(null)
  const [manualImportFor, setManualImportFor] = useState<QueueItem | null>(null)

  const retry = useMutation({
    mutationFn: (grabId: number) => queueApi.retryImport(grabId),
    onSuccess: async () => {
      toast.success(t('queue.importQueued'))
      await queryClient.invalidateQueries({ queryKey: ['queue'] })
    },
    onError: (e) => toast.error(e instanceof ApiError ? e.message : t('queue.retryImportFailed')),
  })

  const refresh = useMutation({
    mutationFn: queueApi.refresh,
    onSuccess: (snapshot) => queryClient.setQueryData(['queue'], snapshot),
    onError: (e) => toast.error(e instanceof ApiError ? e.message : t('queue.refreshFailed')),
  })

  const q = query.data
  const failing = q?.clients.filter((c) => !c.healthy) ?? []

  return (
    <>
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="text-2xl font-semibold tracking-tight">{t('queue.title')}</h1>
        <div className="ml-auto flex flex-wrap items-center gap-3">
          <Tooltip>
            <TooltipTrigger asChild>
              <span className={cn('inline-flex items-center gap-1 text-xs', live ? 'text-emerald-600 dark:text-emerald-400' : 'text-muted-foreground')} data-testid="queue-live">
                {live ? <WifiIcon className="size-3.5" /> : <WifiOffIcon className="size-3.5" />}
                {live ? t('queue.live') : t('queue.polling')}
              </span>
            </TooltipTrigger>
            <TooltipContent>{live ? t('queue.liveHint') : t('queue.pollingHint')}</TooltipContent>
          </Tooltip>
          <Button type="button" size="sm" onClick={() => refresh.mutate()} disabled={refresh.isPending} data-testid="queue-refresh">
            <RefreshCwIcon className={refresh.isPending ? 'animate-spin' : undefined} />
            {refresh.isPending ? t('queue.refreshing') : t('queue.refresh')}
          </Button>
        </div>
      </div>

      {q && (
        <p className="text-sm text-muted-foreground" data-testid="queue-status">
          {t('queue.schedule', { seconds: q.pollSeconds })}
          {q.lastPollAt ? ` · ${t('queue.lastPoll', { date: formatDateTime(q.lastPollAt) })}` : ` · ${t('queue.neverPolled')}`}
          {` · ${t('queue.count', { count: q.items.length })}`}
        </p>
      )}

      {failing.length > 0 && (
        <Alert variant="destructive">
          <AlertDescription>
            <ul className="flex flex-col gap-1">
              {failing.map((c) => (
                <li key={c.downloadClientId}>
                  <span className="font-medium">{c.name}</span>: {c.lastError}
                </li>
              ))}
            </ul>
          </AlertDescription>
        </Alert>
      )}

      {query.isPending && (
        <div className="flex flex-col gap-2">
          <Skeleton className="h-8 w-full" />
          <Skeleton className="h-8 w-full" />
        </div>
      )}

      {query.error && (
        <Alert variant="destructive">
          <AlertDescription>{query.error instanceof ApiError ? query.error.message : t('common.unexpectedError')}</AlertDescription>
        </Alert>
      )}

      {q && q.items.length === 0 && (
        <p className="text-sm text-muted-foreground" data-testid="queue-empty">
          {q.clients.length === 0 ? t('queue.noClients') : t('queue.empty')}
        </p>
      )}

      {q && q.items.length > 0 && (
        <div className="overflow-x-auto rounded-lg border">
          <Table data-testid="queue-table">
            <TableHeader>
              <TableRow>
                <TableHead>{t('queue.movie')}</TableHead>
                <TableHead>{t('queue.release')}</TableHead>
                <TableHead>{t('queue.status')}</TableHead>
                <TableHead className="min-w-[12rem]">{t('queue.progress')}</TableHead>
                <TableHead>{t('queue.size')}</TableHead>
                <TableHead>{t('queue.client')}</TableHead>
                <TableHead className="text-right">{t('versions.actions')}</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {q.items.map((item) => (
                <QueueRow
                  key={`${item.downloadClientId}:${item.downloadId}`}
                  item={item}
                  onRemove={() => setPendingRemove(item)}
                  onRetryImport={() => item.grabId !== null && retry.mutate(item.grabId)}
                  onManualImport={() => setManualImportFor(item)}
                />
              ))}
            </TableBody>
          </Table>
        </div>
      )}

      {pendingRemove && <RemoveDialog item={pendingRemove} onClose={() => setPendingRemove(null)} />}
      {manualImportFor?.grabId != null && manualImportFor.series && (
        <SeriesManualImportDialog grabId={manualImportFor.grabId} title={manualImportFor.title} onClose={() => setManualImportFor(null)} />
      )}
      {manualImportFor?.grabId != null && !manualImportFor.series && (
        <ManualImportDialog grabId={manualImportFor.grabId} title={manualImportFor.title} onClose={() => setManualImportFor(null)} />
      )}
    </>
  )
}

const episodeCode = (e: QueueEpisode) => `S${String(e.seasonNumber).padStart(2, '0')}E${String(e.episodeNumber).padStart(2, '0')}`

/** "S01E05", "S01E01–S01E07 (7)": what a grab covers, compact. */
function episodesLabel(episodes: QueueEpisode[]) {
  if (episodes.length === 1) return episodeCode(episodes[0])
  return `${episodeCode(episodes[0])}–${episodeCode(episodes[episodes.length - 1])} (${episodes.length})`
}

function QueueRow({ item, onRemove, onRetryImport, onManualImport }: { item: QueueItem; onRemove: () => void; onRetryImport: () => void; onManualImport: () => void }) {
  const { t } = useTranslation()
  const percent = Math.round(item.progress * 100)
  const done = item.status === 'completed'
  // The import verdict is the row's state: shown once, in red, with its reason under it rather than as a second badge.
  const failedImport = item.state === 'importFailed' ? item.statusMessages.find((m) => m.title === 'Import failed') : undefined
  const messages = failedImport ? item.statusMessages.filter((m) => m !== failedImport) : item.statusMessages

  return (
    <TableRow data-testid={`queue-item-${item.downloadId}`} data-state={item.state ?? 'untracked'}>
      <TableCell className="align-top">
        {item.seriesId ? (
          <>
            <Link to="/series/$seriesId" params={{ seriesId: String(item.seriesId) }} className="font-medium underline-offset-4 hover:underline">
              {item.seriesTitle}
              {item.seriesYear ? <span className="text-muted-foreground"> ({item.seriesYear})</span> : null}
            </Link>
            {item.episodes.length > 0 && (
              <Tooltip>
                <TooltipTrigger asChild>
                  <span className="mt-1 block cursor-default text-xs text-muted-foreground tabular-nums" data-testid="queue-episodes">
                    {episodesLabel(item.episodes)}
                  </span>
                </TooltipTrigger>
                <TooltipContent className="max-w-sm whitespace-normal">
                  {item.episodes.map((e) => `${episodeCode(e)} ${e.title}`).join(' · ')}
                </TooltipContent>
              </Tooltip>
            )}
          </>
        ) : item.movieId ? (
          <Link to="/movies/$movieId" params={{ movieId: String(item.movieId) }} className="font-medium underline-offset-4 hover:underline">
            {item.movieTitle}
            {item.movieYear ? <span className="text-muted-foreground"> ({item.movieYear})</span> : null}
          </Link>
        ) : (
          <span className="text-muted-foreground">{item.tracked ? t('common.none') : t('queue.untracked')}</span>
        )}
        {item.versionName && (
          <span className="mt-1 block">
            <Badge variant="secondary">{item.versionName}</Badge>
          </span>
        )}
      </TableCell>
      <TableCell className="min-w-[20rem] max-w-[32rem] align-top whitespace-normal">
        <span className="block break-all text-sm">{item.title}</span>
        <span className="mt-1 flex flex-wrap items-center gap-1 text-xs text-muted-foreground">
          {item.quality && <Badge variant="outline">{item.quality}</Badge>}
          {item.indexer && <span>{item.indexer}</span>}
        </span>
      </TableCell>
      <TableCell className="align-top">
        <span className="flex flex-col items-start gap-1">
          <Badge variant="outline" className={failedImport ? statusStyles.failed : statusStyles[item.status]}>
            {item.state ? t(`queue.states.${item.state}`) : t(`queue.statuses.${item.status}`)}
          </Badge>
          {failedImport && (
            <span className="flex max-w-[22rem] flex-col gap-0.5 whitespace-normal text-xs text-red-700 dark:text-red-300" data-testid="queue-import-error">
              {failedImport.messages.map((line, i) => (
                <span key={i} className={cn(i > 0 && 'break-all text-muted-foreground')}>
                  {line}
                </span>
              ))}
            </span>
          )}
          {messages.map((message) => (
            <Tooltip key={message.title}>
              <TooltipTrigger asChild>
                <Badge variant="outline" className={cn('max-w-[14rem] cursor-default truncate', severityStyles[message.severity])}>
                  {message.title}
                </Badge>
              </TooltipTrigger>
              {message.messages.length > 0 && <TooltipContent className="max-w-sm whitespace-normal">{message.messages.join(' ')}</TooltipContent>}
            </Tooltip>
          ))}
        </span>
      </TableCell>
      <TableCell className="align-top">
        <div className="flex flex-col gap-1">
          <Progress value={percent} className={done ? '[&>[data-slot=progress-indicator]]:bg-emerald-600' : undefined} />
          <span className="text-xs tabular-nums text-muted-foreground">
            {percent}%
            {!done && item.downloadRate > 0 ? ` · ${formatBytes(item.downloadRate)}/s` : ''}
            {!done && item.etaSeconds !== null ? ` · ${formatEta(item.etaSeconds)}` : ''}
          </span>
        </div>
      </TableCell>
      <TableCell className="align-top whitespace-nowrap tabular-nums">
        {formatBytes(item.size)}
        {!done && item.sizeLeft > 0 && <span className="block text-xs text-muted-foreground">{t('queue.left', { size: formatBytes(item.sizeLeft) })}</span>}
      </TableCell>
      <TableCell className="align-top whitespace-nowrap">{item.downloadClient}</TableCell>
      <TableCell className="align-top">
        <div className="flex items-center justify-end gap-1">
          {item.state === 'importFailed' && (
            <>
              <Tooltip>
                <TooltipTrigger asChild>
                  <Button type="button" variant="ghost" size="icon" onClick={onRetryImport} aria-label={t('queue.retryImport')} data-testid="queue-retry-import">
                    <RotateCcwIcon />
                  </Button>
                </TooltipTrigger>
                <TooltipContent>{t('queue.retryImport')}</TooltipContent>
              </Tooltip>
              <Tooltip>
                <TooltipTrigger asChild>
                  <Button type="button" variant="ghost" size="icon" onClick={onManualImport} aria-label={t('queue.manualImport')} data-testid="queue-manual-import">
                    <FolderInputIcon />
                  </Button>
                </TooltipTrigger>
                <TooltipContent>{t('queue.manualImport')}</TooltipContent>
              </Tooltip>
            </>
          )}
          <Tooltip>
            <TooltipTrigger asChild>
              <Button type="button" variant="ghost" size="icon" className="text-destructive hover:text-destructive" onClick={onRemove} aria-label={t('queue.remove')}>
                <Trash2Icon />
              </Button>
            </TooltipTrigger>
            <TooltipContent>{t('queue.remove')}</TooltipContent>
          </Tooltip>
        </div>
      </TableCell>
    </TableRow>
  )
}

function RemoveDialog({ item, onClose }: { item: QueueItem; onClose: () => void }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [removeFromClient, setRemoveFromClient] = useState(true)
  const [deleteFiles, setDeleteFiles] = useState(true)
  const [blocklist, setBlocklist] = useState(false)

  const remove = useMutation({
    mutationFn: () =>
      item.grabId !== null
        ? queueApi.remove(item.grabId, { removeFromClient, deleteFiles: removeFromClient && deleteFiles, blocklist })
        : queueApi.removeUntracked(item.downloadClientId, item.downloadId, deleteFiles),
    onSuccess: async () => {
      onClose()
      await queryClient.invalidateQueries({ queryKey: ['queue'] })
      if (item.movieId) await queryClient.invalidateQueries({ queryKey: ['movie', item.movieId] })
      await queryClient.invalidateQueries({ queryKey: ['movies'] })
    },
    onError: (e) => toast.error(e instanceof ApiError ? e.message : t('queue.removeFailed')),
  })

  return (
    <AlertDialog open onOpenChange={(open) => !open && onClose()}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{t('queue.removeTitle')}</AlertDialogTitle>
          <AlertDialogDescription className="break-all">{item.title}</AlertDialogDescription>
        </AlertDialogHeader>
        <div className="flex flex-col gap-3 py-2" data-testid="queue-remove-options">
          {item.grabId !== null && (
            <Option id="rm-client" label={t('queue.removeFromClient')} hint={t('queue.removeFromClientHint')} checked={removeFromClient} onChange={setRemoveFromClient} />
          )}
          <Option
            id="rm-files"
            label={t('queue.deleteFiles')}
            hint={t('queue.deleteFilesHint')}
            checked={deleteFiles}
            onChange={setDeleteFiles}
            disabled={item.grabId !== null && !removeFromClient}
          />
          {item.grabId !== null && <Option id="rm-blocklist" label={t('queue.blocklist')} hint={t('queue.blocklistHint')} checked={blocklist} onChange={setBlocklist} />}
        </div>
        <AlertDialogFooter>
          <AlertDialogCancel>{t('common.cancel')}</AlertDialogCancel>
          <AlertDialogAction
            onClick={(event) => {
              event.preventDefault()
              remove.mutate()
            }}
            disabled={remove.isPending}
            data-testid="queue-remove-confirm"
          >
            {t('queue.remove')}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  )
}

function Option({ id, label, hint, checked, onChange, disabled }: { id: string; label: string; hint: string; checked: boolean; onChange: (value: boolean) => void; disabled?: boolean }) {
  return (
    <div className="flex items-start gap-2">
      <Checkbox id={id} checked={checked} onCheckedChange={(value) => onChange(value === true)} disabled={disabled} className="mt-0.5" />
      <div className="grid gap-0.5">
        <Label htmlFor={id} className={cn('font-normal', disabled && 'text-muted-foreground')}>
          {label}
        </Label>
        <span className="text-xs text-muted-foreground">{hint}</span>
      </div>
    </div>
  )
}
