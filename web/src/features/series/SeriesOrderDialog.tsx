import { useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { cn } from 'cn'
import { ArrowRightIcon, TriangleAlertIcon, Undo2Icon } from 'lucide-react'
import type { TFunction } from 'i18next'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import {
  ApiError,
  series as seriesApi,
  seriesFiles,
  type EpisodeFile,
  type OrderChange,
  type OrderChoice,
  type OrderChoiceRequest,
  type OrderFileStatus,
  type OrderPreview,
  type Series,
} from '@/api/client'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectGroup, SelectItem, SelectLabel, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { EpisodePicker } from '@/features/import/SeriesManualImportDialog'

const errorMessage = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback)

/** "TVDB", "TMDB (default)", or the episode group's name. */
export function orderLabel(t: TFunction, choice: Pick<OrderChoice, 'ordering' | 'episodeGroupName' | 'episodeGroupType'>) {
  if (choice.ordering === 'tvdb') return t('seriesOrder.tvdb')
  if (choice.ordering === 'tmdbDefault') return t('seriesOrder.tmdbDefault')
  return t('seriesOrder.group', { name: choice.episodeGroupName ?? '?', type: choice.episodeGroupType ? t(`seriesOrder.groupTypes.${choice.episodeGroupType}`) : '' })
}

const statusStyle: Record<OrderFileStatus, string> = {
  move: 'border-sky-600/30 bg-sky-500/15 text-sky-700 dark:border-sky-400/30 dark:text-sky-300',
  unchanged: '',
  unmatched: 'border-amber-600/30 bg-amber-500/15 text-amber-700 dark:border-amber-400/30 dark:text-amber-300',
  conflict: 'border-red-600/30 bg-red-500/15 text-red-700 dark:border-red-400/30 dark:text-red-300',
}

const valueOf = (request: OrderChoiceRequest) => (request.ordering === 'tmdbEpisodeGroup' ? `group:${request.episodeGroupId}` : request.ordering)
const requestOf = (value: string): OrderChoiceRequest =>
  value.startsWith('group:') ? { ordering: 'tmdbEpisodeGroup', episodeGroupId: value.slice('group:'.length) } : { ordering: value as OrderChoiceRequest['ordering'] }

/**
 * The per-show order: pick TVDB, TMDB's default order or a TMDB episode group, preview every file's
 * rename, confirm, undo. The episodes keep their identity: only numbers, season grouping and file names change.
 */
export function SeriesOrderDialog({ series: s, onClose, onDone }: { series: Series; onClose: () => void; onDone: () => void }) {
  const { t } = useTranslation()
  const orders = useQuery({ queryKey: ['seriesOrders', s.id], queryFn: () => seriesApi.orders(s.id) })
  const [choice, setChoice] = useState<OrderChoiceRequest | null>(null)
  const [understood, setUnderstood] = useState(false)
  const preview = useMutation({ mutationFn: (request: OrderChoiceRequest) => seriesApi.previewOrder(s.id, request) })
  const apply = useMutation({
    mutationFn: (request: OrderChoiceRequest) => seriesApi.applyOrder(s.id, request),
    onSuccess: (result) => {
      const failed = result.files.filter((f) => !f.moved)
      toast.success(t('seriesOrder.applied', { order: orderLabel(t, result.change.to), count: result.change.filesMoved }), {
        description: failed.length > 0 ? t('seriesOrder.leftInPlace', { count: result.change.unmatchedFiles }) : undefined,
        action: { label: t('seriesOrder.undo'), onClick: () => undo.mutate() },
        duration: 15000,
      })
      onDone()
      onClose()
    },
  })
  const undo = useUndo(s.id, onDone)

  const current = orders.data?.current
  const selected = choice ?? (current ? { ordering: current.ordering, episodeGroupId: current.episodeGroupId } : null)
  const isCurrent = !!current && !!selected && valueOf(selected) === valueOf({ ordering: current.ordering, episodeGroupId: current.episodeGroupId })
  const data = preview.data
  const pick = (value: string) => {
    const next = requestOf(value)
    setChoice(next)
    setUnderstood(false)
    preview.reset()
    if (!current || valueOf(next) !== valueOf({ ordering: current.ordering, episodeGroupId: current.episodeGroupId })) preview.mutate(next)
  }

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="flex max-h-[90vh] flex-col sm:max-w-4xl" data-testid="series-order">
        <DialogHeader>
          <DialogTitle>{t('seriesOrder.title', { series: s.title })}</DialogTitle>
          <DialogDescription>{t('seriesOrder.description')}</DialogDescription>
        </DialogHeader>

        {orders.isPending ? (
          <Skeleton className="h-9 w-80" />
        ) : orders.error ? (
          <Alert variant="destructive">
            <AlertDescription>{errorMessage(orders.error, t('common.unexpectedError'))}</AlertDescription>
          </Alert>
        ) : (
          <div className="grid gap-1.5">
            <Label htmlFor="series-order-pick">{t('seriesOrder.pick')}</Label>
            <Select value={selected ? valueOf(selected) : undefined} onValueChange={pick}>
              <SelectTrigger id="series-order-pick" className="w-full sm:w-[28rem]" data-testid="series-order-pick">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="tvdb">{t('seriesOrder.tvdb')}</SelectItem>
                <SelectItem value="tmdbDefault" disabled={!s.tmdbId}>
                  {t('seriesOrder.tmdbDefault')}
                </SelectItem>
                {orders.data.groups.length > 0 && (
                  <SelectGroup>
                    <SelectLabel>{t('seriesOrder.groups')}</SelectLabel>
                    {orders.data.groups.map((g) => (
                      <SelectItem key={g.id} value={`group:${g.id}`}>
                        {g.name}{' '}
                        <span className="text-muted-foreground">
                          · {t(`seriesOrder.groupTypes.${g.type}`)} · {t('seriesOrder.groupSize', { groups: g.groupCount, episodes: g.episodeCount })}
                        </span>
                      </SelectItem>
                    ))}
                  </SelectGroup>
                )}
              </SelectContent>
            </Select>
            {orders.data.unavailable && <p className="text-xs text-muted-foreground">{orders.data.unavailable}</p>}
            {orders.data.lastChange?.canUndo && <LastChange change={orders.data.lastChange} onUndo={() => undo.mutate()} pending={undo.isPending} />}
          </div>
        )}

        {preview.isPending && (
          <div className="flex flex-col gap-2">
            <Skeleton className="h-16 w-full" />
            <Skeleton className="h-8 w-full" />
          </div>
        )}
        {preview.error && (
          <Alert variant="destructive">
            <AlertDescription>{errorMessage(preview.error, t('common.unexpectedError'))}</AlertDescription>
          </Alert>
        )}
        {isCurrent && !preview.isPending && <p className="text-sm text-muted-foreground">{t('seriesOrder.isCurrent')}</p>}
        {data && !isCurrent && <PreviewBody preview={data} understood={understood} onUnderstood={setUnderstood} />}

        {apply.error && (
          <Alert variant="destructive">
            <AlertDescription>{errorMessage(apply.error, t('common.unexpectedError'))}</AlertDescription>
          </Alert>
        )}
        <DialogFooter>
          <Button type="button" variant="ghost" onClick={onClose}>
            {t('common.cancel')}
          </Button>
          <Button
            type="button"
            variant={data?.largeChange ? 'destructive' : 'default'}
            disabled={!data || isCurrent || apply.isPending || (data.largeChange && !understood)}
            onClick={() => selected && apply.mutate(selected)}
            data-testid="series-order-apply"
          >
            {data && data.filesMoving > 0 ? t('seriesOrder.applyRename', { count: data.filesMoving }) : t('seriesOrder.apply')}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

function PreviewBody({ preview: p, understood, onUnderstood }: { preview: OrderPreview; understood: boolean; onUnderstood: (value: boolean) => void }) {
  const { t } = useTranslation()
  const [showEpisodes, setShowEpisodes] = useState(false)
  const files = p.fileChanges.filter((f) => f.status !== 'unchanged')

  return (
    <div className="flex min-h-0 flex-1 flex-col gap-3 overflow-y-auto" data-testid="series-order-preview">
      <Alert variant={p.largeChange ? 'destructive' : 'default'} data-testid="series-order-warning">
        <TriangleAlertIcon />
        <AlertTitle>{p.filesMoving > 0 ? t('seriesOrder.warningTitle', { count: p.filesMoving }) : t('seriesOrder.noRenameTitle')}</AlertTitle>
        <AlertDescription>
          <p>{p.largeChange ? t('seriesOrder.warningLarge', { count: p.filesMoving, total: p.files }) : t('seriesOrder.warning')}</p>
          <p>{t('seriesOrder.undoHint')}</p>
        </AlertDescription>
      </Alert>

      <dl className="grid grid-cols-2 gap-x-6 gap-y-1 text-sm sm:grid-cols-4">
        <Stat label={t('seriesOrder.stats.episodesMoving')} value={p.episodesMoving} total={p.episodes} />
        <Stat label={t('seriesOrder.stats.notInOrder')} value={p.episodesNotInOrder} />
        <Stat label={t('seriesOrder.stats.filesMoving')} value={p.filesMoving} total={p.files} />
        <Stat label={t('seriesOrder.stats.filesLeft')} value={p.filesUnmatched + p.filesConflicting} />
      </dl>

      {p.largeChange && (
        <label className="flex items-start gap-2 text-sm">
          <Checkbox checked={understood} onCheckedChange={(v) => onUnderstood(v === true)} className="mt-0.5" data-testid="series-order-understood" />
          <span>{t('seriesOrder.understood', { count: p.filesMoving })}</span>
        </label>
      )}

      {files.length > 0 && (
        <div className="overflow-x-auto rounded-lg border">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead className="w-28 pl-3">{t('seriesOrder.columns.status')}</TableHead>
                <TableHead>{t('seriesOrder.columns.file')}</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {files.map((f) => (
                <TableRow key={f.episodeFileId} data-testid="series-order-file">
                  <TableCell className="pl-3 align-top">
                    <Badge variant="outline" className={statusStyle[f.status]}>
                      {t(`seriesOrder.status.${f.status}`)}
                    </Badge>
                  </TableCell>
                  <TableCell className="max-w-0 align-top">
                    <span className="block truncate font-mono text-xs text-muted-foreground" title={f.from}>
                      {f.from}
                    </span>
                    {f.status === 'move' || f.status === 'conflict' ? (
                      <span className="flex items-center gap-1 font-mono text-xs" title={f.to}>
                        <ArrowRightIcon className="size-3 shrink-0" />
                        <span className="truncate">{f.to}</span>
                      </span>
                    ) : null}
                    {f.detail && <span className="block text-xs text-amber-700 dark:text-amber-300">{f.detail}</span>}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}

      {p.episodeChanges.length > 0 && (
        <div className="text-sm">
          <Button type="button" variant="link" className="h-auto p-0" onClick={() => setShowEpisodes((v) => !v)}>
            {showEpisodes ? t('seriesOrder.hideEpisodes') : t('seriesOrder.showEpisodes', { count: p.episodeChanges.length })}
          </Button>
          {showEpisodes && (
            <ul className="mt-2 grid max-h-56 gap-0.5 overflow-y-auto text-xs sm:grid-cols-2">
              {p.episodeChanges.map((c) => (
                <li key={c.episodeId} className="flex items-center gap-1.5 tabular-nums">
                  <span className="text-muted-foreground">{c.from}</span>
                  <ArrowRightIcon className="size-3 shrink-0" />
                  <span className={cn(!c.to && 'text-amber-700 dark:text-amber-300')}>{c.to ?? t('seriesOrder.notInOrderShort', { tvdb: c.tvdb })}</span>
                  <span className="truncate text-muted-foreground">{c.title}</span>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}

      {p.orderOnly.length > 0 && <p className="text-xs text-muted-foreground">{t('seriesOrder.orderOnly', { count: p.orderOnly.length })}</p>}
    </div>
  )
}

function Stat({ label, value, total }: { label: string; value: number; total?: number }) {
  return (
    <div>
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className="font-medium tabular-nums">
        {value}
        {total !== undefined && <span className="text-muted-foreground"> / {total}</span>}
      </dd>
    </div>
  )
}

function LastChange({ change, onUndo, pending }: { change: OrderChange; onUndo: () => void; pending: boolean }) {
  const { t } = useTranslation()
  return (
    <div className="flex flex-wrap items-center gap-2 text-xs text-muted-foreground" data-testid="series-order-last">
      <span>{t('seriesOrder.lastChange', { from: orderLabel(t, change.from), to: orderLabel(t, change.to), count: change.filesMoved })}</span>
      <Button type="button" size="sm" variant="outline" onClick={onUndo} disabled={pending} data-testid="series-order-undo">
        <Undo2Icon />
        {t('seriesOrder.undo')}
      </Button>
    </div>
  )
}

export function useUndo(seriesId: number, onDone: () => void) {
  const { t } = useTranslation()
  return useMutation({
    mutationFn: () => seriesApi.undoOrder(seriesId),
    onSuccess: (result) => {
      const left = result.files.filter((f) => !f.moved).length
      toast.success(t('seriesOrder.undone', { order: orderLabel(t, result.change.from), count: result.filesRestored }), { description: left > 0 ? t('seriesOrder.undoLeft', { count: left }) : undefined })
      onDone()
    },
    onError: (error) => toast.error(errorMessage(error, t('common.unexpectedError'))),
  })
}

/** The page's reminder after a switch: what changed, undo until the next change. */
export function OrderUndoBanner({ series: s, onDone }: { series: Series; onDone: () => void }) {
  const orders = useQuery({ queryKey: ['seriesOrders', s.id], queryFn: () => seriesApi.orders(s.id) })
  const undo = useUndo(s.id, onDone)
  const change = orders.data?.lastChange
  if (!change?.canUndo) return null
  return <LastChange change={change} onUndo={() => undo.mutate()} pending={undo.isPending} />
}

/** The manual fix of a file the order could not place (S5's assignment): the episodes it holds, renamed after them. */
export function ReassignFileDialog({ series: s, file, onClose, onDone }: { series: Series; file: EpisodeFile; onClose: () => void; onDone: () => void }) {
  const { t } = useTranslation()
  const [episodeIds, setEpisodeIds] = useState<number[]>([])
  const [rename, setRename] = useState(true)
  const reassign = useMutation({
    mutationFn: () => seriesFiles.reassign(s.id, file.id, { episodeIds, rename }),
    onSuccess: (result) => {
      toast.success(t('seriesFiles.assigned', { path: result.to }))
      onDone()
      onClose()
    },
  })

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="sm:max-w-xl" data-testid="reassign-file">
        <DialogHeader>
          <DialogTitle>{t('seriesOrder.fixTitle')}</DialogTitle>
          <DialogDescription className="break-all font-mono text-xs">{file.relativePath}</DialogDescription>
        </DialogHeader>
        <p className="text-sm text-muted-foreground">{t('seriesOrder.fixHint')}</p>
        <div className="grid gap-3">
          <div className="grid gap-1.5">
            <Label>{t('seriesImport.episodes')}</Label>
            <EpisodePicker seriesId={s.id} value={episodeIds} onChange={setEpisodeIds} ordered />
          </div>
          <label className="flex items-start gap-2 text-sm">
            <Checkbox checked={rename} onCheckedChange={(v) => setRename(v === true)} className="mt-0.5" />
            <span>{t('seriesFiles.assignRename')}</span>
          </label>
          {reassign.error && (
            <Alert variant="destructive">
              <AlertDescription>{errorMessage(reassign.error, t('common.unexpectedError'))}</AlertDescription>
            </Alert>
          )}
        </div>
        <DialogFooter>
          <Button type="button" variant="ghost" onClick={onClose}>
            {t('common.cancel')}
          </Button>
          <Button type="button" onClick={() => reassign.mutate()} disabled={episodeIds.length === 0 || reassign.isPending} data-testid="reassign-file-run">
            {t('seriesFiles.assign')}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
