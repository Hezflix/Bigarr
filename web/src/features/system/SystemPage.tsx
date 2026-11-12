import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { cn } from 'cn'
import { ArchiveIcon, CirclePauseIcon, DownloadIcon, HeartPulseIcon, HistoryIcon, PlayIcon, RefreshCwIcon, Trash2Icon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ApiError, backups, health, system, tasks, type Backup, type HealthIssueType, type ScheduledTask } from '@/api/client'
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
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { formatAge, formatBytes, formatDateTime } from '@/i18n'

export const healthStyles: Record<HealthIssueType, string> = {
  error: 'border-red-600/30 bg-red-500/10 text-red-700 dark:border-red-400/30 dark:text-red-300',
  warning: 'border-amber-600/30 bg-amber-500/15 text-amber-700 dark:border-amber-400/30 dark:text-amber-300',
  notice: 'border-border bg-muted text-muted-foreground',
}

/** "1.2s", "340ms". */
function formatDuration(ms: number | null): string {
  if (ms === null) return ''
  return ms >= 1000 ? `${(ms / 1000).toFixed(1)}s` : `${ms}ms`
}

/** Health checks and the scheduler: what would stop the loop, and the jobs that run it. */
export function SystemPage() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const status = useQuery({ queryKey: ['system', 'status'], queryFn: system.status, staleTime: 5 * 60_000 })
  const healthQuery = useQuery({ queryKey: ['health'], queryFn: health.get, refetchInterval: 60_000 })
  const taskList = useQuery({ queryKey: ['tasks'], queryFn: tasks.list, refetchInterval: 15_000 })

  const check = useMutation({
    mutationFn: health.check,
    onSuccess: (result) => {
      queryClient.setQueryData(['health'], result)
      void queryClient.invalidateQueries({ queryKey: ['tasks'] })
    },
    onError: (e) => toast.error(e instanceof ApiError ? e.message : t('common.unexpectedError')),
  })
  const run = useMutation({
    mutationFn: (task: ScheduledTask) => tasks.run(task.name),
    onSuccess: async (result) => {
      if (result.success) toast.success(t('tasks.ran', { name: result.name, summary: result.summary ?? '' }))
      else toast.error(t('tasks.failed', { name: result.name, error: result.error ?? '' }))
      await queryClient.invalidateQueries({ queryKey: ['tasks'] })
      await queryClient.invalidateQueries({ queryKey: ['health'] })
      await queryClient.invalidateQueries({ queryKey: ['rss'] })
      await queryClient.invalidateQueries({ queryKey: ['queue'] })
      await queryClient.invalidateQueries({ queryKey: ['wanted'] })
    },
    onError: (e) => toast.error(e instanceof ApiError ? e.message : t('common.unexpectedError')),
  })

  const issues = healthQuery.data?.issues ?? []
  const schedulerOff = status.data?.schedulerEnabled === false

  return (
    <>
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="text-2xl font-semibold tracking-tight">{t('system.title')}</h1>
      </div>

      <Card data-testid="health-card">
        <CardHeader className="flex flex-row flex-wrap items-center gap-3">
          <CardTitle className="flex items-center gap-2">
            <HeartPulseIcon className="size-4" />
            {t('health.title')}
          </CardTitle>
          <div className="ml-auto flex items-center gap-3">
            {healthQuery.data?.checkedAt && <span className="text-xs text-muted-foreground">{t('health.checked', { age: formatAge(healthQuery.data.checkedAt) })}</span>}
            <Button type="button" variant="outline" size="sm" onClick={() => check.mutate()} disabled={check.isPending} data-testid="health-check">
              <RefreshCwIcon className={check.isPending ? 'animate-spin' : undefined} />
              {t('health.checkNow')}
            </Button>
          </div>
        </CardHeader>
        <CardContent>
          {healthQuery.isPending && <Skeleton className="h-6 w-full" />}
          {healthQuery.data && issues.length === 0 && (
            <p className="text-sm text-emerald-700 dark:text-emerald-300" data-testid="health-ok">
              {t('health.allGood')}
            </p>
          )}
          {issues.length > 0 && (
            <ul className="flex flex-col gap-2" data-testid="health-issues">
              {issues.map((issue) => (
                <li key={`${issue.source}:${issue.message}`} className="flex flex-wrap items-start gap-2 text-sm">
                  <Badge variant="outline" className={cn('shrink-0', healthStyles[issue.type])}>
                    {t(`health.types.${issue.type}`)}
                  </Badge>
                  <span className="shrink-0 text-xs text-muted-foreground uppercase tracking-wide leading-5">{issue.source}</span>
                  <span className="min-w-0 flex-1 break-words">{issue.message}</span>
                </li>
              ))}
            </ul>
          )}
        </CardContent>
      </Card>

      <section className="flex flex-col gap-3">
        <h2 className="text-xl font-semibold tracking-tight">{t('tasks.title')}</h2>
        {schedulerOff ? (
          <Alert className="max-w-3xl" data-testid="scheduler-off">
            <CirclePauseIcon />
            <AlertDescription>{t('tasks.schedulerOff')}</AlertDescription>
          </Alert>
        ) : (
          <p className="max-w-3xl text-sm text-muted-foreground">{t('tasks.hint')}</p>
        )}
        <div className="overflow-x-auto rounded-lg border">
          <Table data-testid="tasks-table">
            <TableHeader>
              <TableRow>
                <TableHead>{t('tasks.task')}</TableHead>
                <TableHead>{t('tasks.interval')}</TableHead>
                <TableHead>{t('tasks.lastRun')}</TableHead>
                <TableHead>{t('tasks.result')}</TableHead>
                <TableHead>{t('tasks.nextRun')}</TableHead>
                <TableHead className="text-right">{t('versions.actions')}</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {taskList.data?.map((task) => (
                <TableRow key={task.name} data-testid={`task-${task.name}`}>
                  <TableCell className="align-top">
                    <span className="font-medium">{task.displayName}</span>
                    <span className="block text-xs text-muted-foreground">{task.name}</span>
                  </TableCell>
                  <TableCell className="align-top whitespace-nowrap">
                    {t('common.minutes', { count: task.intervalMinutes })}
                    {!task.enabled && <span className="block text-xs text-muted-foreground">{t('tasks.disabled')}</span>}
                  </TableCell>
                  <TableCell className="align-top whitespace-nowrap">
                    {task.running ? (
                      <Badge variant="outline" className="border-blue-600/30 bg-blue-500/15 text-blue-700 dark:border-blue-400/30 dark:text-blue-300">
                        {t('tasks.running')}
                      </Badge>
                    ) : task.lastRunAt ? (
                      <Tooltip>
                        <TooltipTrigger asChild>
                          <span>
                            {formatAge(task.lastRunAt)}
                            {task.lastDurationMs !== null && <span className="block text-xs text-muted-foreground">{formatDuration(task.lastDurationMs)}</span>}
                          </span>
                        </TooltipTrigger>
                        <TooltipContent>{formatDateTime(task.lastRunAt)}</TooltipContent>
                      </Tooltip>
                    ) : (
                      <span className="text-muted-foreground">{t('tasks.neverRan')}</span>
                    )}
                  </TableCell>
                  <TableCell className={cn('min-w-[16rem] align-top text-sm whitespace-normal', task.lastSucceeded === false && 'text-red-700 dark:text-red-300')}>
                    {task.lastSucceeded === false ? task.lastError : task.lastSummary ?? <span className="text-muted-foreground">{t('common.none')}</span>}
                  </TableCell>
                  <TableCell className="align-top whitespace-nowrap">{task.nextRunAt ? formatDateTime(task.nextRunAt) : <span className="text-muted-foreground">{t('common.none')}</span>}</TableCell>
                  <TableCell className="align-top">
                    <div className="flex justify-end">
                      <Tooltip>
                        <TooltipTrigger asChild>
                          <Button
                            type="button"
                            variant="ghost"
                            size="icon"
                            onClick={() => run.mutate(task)}
                            disabled={run.isPending || task.running}
                            aria-label={t('tasks.runNow')}
                            data-testid={`run-${task.name}`}
                          >
                            <PlayIcon className={run.isPending && run.variables?.name === task.name ? 'animate-pulse' : undefined} />
                          </Button>
                        </TooltipTrigger>
                        <TooltipContent>{t('tasks.runNow')}</TooltipContent>
                      </Tooltip>
                    </div>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      </section>

      <BackupsSection schedulerOff={schedulerOff} />

      {status.data && (
        <Card data-testid="system-status">
          <CardHeader>
            <CardTitle>{t('system.about')}</CardTitle>
          </CardHeader>
          <CardContent>
            <dl className="grid gap-x-6 gap-y-2 text-sm sm:grid-cols-[auto_1fr]">
              <dt className="text-muted-foreground">{t('system.version')}</dt>
              <dd>{status.data.version}</dd>
              <dt className="text-muted-foreground">{t('system.runtime')}</dt>
              <dd>{status.data.runtimeVersion}</dd>
              <dt className="text-muted-foreground">{t('system.os')}</dt>
              <dd>
                {status.data.os}
                {status.data.isDocker ? ` · ${t('system.docker')}` : ''}
              </dd>
              <dt className="text-muted-foreground">{t('system.configDirectory')}</dt>
              <dd className="break-all">{status.data.configDirectory}</dd>
              <dt className="text-muted-foreground">{t('system.metadata')}</dt>
              <dd>{t(`system.metadataProviders.${status.data.metadataProvider}`)}</dd>
              <dt className="text-muted-foreground">{t('system.startedAt')}</dt>
              <dd>{formatDateTime(status.data.startedAt)}</dd>
            </dl>
          </CardContent>
        </Card>
      )}
    </>
  )
}

/** Backups: a zip of the database, weekly and on demand; restoring stages the file and restarts Bigarr. */
function BackupsSection({ schedulerOff }: { schedulerOff: boolean }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const list = useQuery({ queryKey: ['backups'], queryFn: backups.list })
  const [pendingRestore, setPendingRestore] = useState<Backup | null>(null)
  const [pendingRemove, setPendingRemove] = useState<Backup | null>(null)
  const [restarting, setRestarting] = useState(false)
  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['backups'] })

  const create = useMutation({
    mutationFn: backups.create,
    onSuccess: async (backup) => {
      toast.success(t('backups.created', { name: backup.name }))
      await invalidate()
      await queryClient.invalidateQueries({ queryKey: ['tasks'] })
    },
    onError: (e) => toast.error(e instanceof ApiError ? e.message : t('backups.createFailed')),
  })
  const restore = useMutation({
    mutationFn: (name: string) => backups.restore(name),
    onSuccess: () => {
      setPendingRestore(null)
      setRestarting(true)
    },
    onError: (e) => {
      setPendingRestore(null)
      toast.error(e instanceof ApiError ? e.message : t('backups.restoreFailed'))
    },
  })
  const remove = useMutation({
    mutationFn: (name: string) => backups.remove(name),
    onSuccess: async () => {
      setPendingRemove(null)
      await invalidate()
    },
    onError: (e) => {
      setPendingRemove(null)
      toast.error(e instanceof ApiError ? e.message : t('backups.removeFailed'))
    },
  })

  return (
    <section className="flex flex-col gap-3" data-testid="backups">
      <div className="flex flex-wrap items-center gap-3">
        <h2 className="flex items-center gap-2 text-xl font-semibold tracking-tight">
          <ArchiveIcon className="size-5" />
          {t('backups.title')}
        </h2>
        <Button type="button" variant="outline" size="sm" className="ml-auto" onClick={() => create.mutate()} disabled={create.isPending || restarting} data-testid="backup-now">
          <ArchiveIcon className={create.isPending ? 'animate-pulse' : undefined} />
          {t('backups.create')}
        </Button>
      </div>
      <p className="max-w-3xl text-sm text-muted-foreground">{t(schedulerOff ? 'backups.hintManual' : 'backups.hint')}</p>
      {restarting && (
        <Alert data-testid="restoring">
          <AlertDescription>{t('backups.restoring')}</AlertDescription>
        </Alert>
      )}
      <div className="overflow-x-auto rounded-lg border">
        <Table data-testid="backups-table">
          <TableHeader>
            <TableRow>
              <TableHead>{t('backups.name')}</TableHead>
              <TableHead>{t('backups.type')}</TableHead>
              <TableHead>{t('backups.size')}</TableHead>
              <TableHead>{t('backups.createdAt')}</TableHead>
              <TableHead className="text-right">{t('versions.actions')}</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {list.data?.map((backup) => (
              <TableRow key={backup.name} data-testid={`backup-${backup.name}`}>
                <TableCell className="font-mono text-xs break-all">{backup.name}</TableCell>
                <TableCell>
                  <Badge variant="outline">{t(`backups.types.${backup.type}`)}</Badge>
                </TableCell>
                <TableCell className="whitespace-nowrap tabular-nums">{formatBytes(backup.size)}</TableCell>
                <TableCell className="whitespace-nowrap">
                  <Tooltip>
                    <TooltipTrigger asChild>
                      <span>{formatAge(backup.createdAt)}</span>
                    </TooltipTrigger>
                    <TooltipContent>{formatDateTime(backup.createdAt)}</TooltipContent>
                  </Tooltip>
                </TableCell>
                <TableCell>
                  <div className="flex justify-end gap-1">
                    <Tooltip>
                      <TooltipTrigger asChild>
                        <Button asChild variant="ghost" size="icon" aria-label={t('backups.download')}>
                          <a href={backups.downloadUrl(backup.name)} download>
                            <DownloadIcon />
                          </a>
                        </Button>
                      </TooltipTrigger>
                      <TooltipContent>{t('backups.download')}</TooltipContent>
                    </Tooltip>
                    <Tooltip>
                      <TooltipTrigger asChild>
                        <Button type="button" variant="ghost" size="icon" onClick={() => setPendingRestore(backup)} disabled={restarting} aria-label={t('backups.restore')}>
                          <HistoryIcon />
                        </Button>
                      </TooltipTrigger>
                      <TooltipContent>{t('backups.restore')}</TooltipContent>
                    </Tooltip>
                    <Tooltip>
                      <TooltipTrigger asChild>
                        <Button type="button" variant="ghost" size="icon" onClick={() => setPendingRemove(backup)} aria-label={t('backups.remove')}>
                          <Trash2Icon />
                        </Button>
                      </TooltipTrigger>
                      <TooltipContent>{t('backups.remove')}</TooltipContent>
                    </Tooltip>
                  </div>
                </TableCell>
              </TableRow>
            ))}
            {list.data?.length === 0 && (
              <TableRow>
                <TableCell colSpan={5} className="text-muted-foreground">
                  {t('backups.none')}
                </TableCell>
              </TableRow>
            )}
          </TableBody>
        </Table>
      </div>

      <AlertDialog open={pendingRestore !== null} onOpenChange={(open) => !open && setPendingRestore(null)}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>{t('backups.restoreTitle')}</AlertDialogTitle>
            <AlertDialogDescription>{pendingRestore && t('backups.confirmRestore', { name: pendingRestore.name })}</AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>{t('common.cancel')}</AlertDialogCancel>
            <AlertDialogAction onClick={() => pendingRestore && restore.mutate(pendingRestore.name)} disabled={restore.isPending}>
              {t('backups.restore')}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>

      <AlertDialog open={pendingRemove !== null} onOpenChange={(open) => !open && setPendingRemove(null)}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>{t('backups.removeTitle')}</AlertDialogTitle>
            <AlertDialogDescription>{pendingRemove && t('backups.confirmRemove', { name: pendingRemove.name })}</AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>{t('common.cancel')}</AlertDialogCancel>
            <AlertDialogAction onClick={() => pendingRemove && remove.mutate(pendingRemove.name)} disabled={remove.isPending}>
              {t('backups.remove')}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </section>
  )
}
