import { useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { cn } from 'cn'
import { FlaskConicalIcon, ListIcon, PencilIcon, PlusIcon, RotateCcwIcon, Trash2Icon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ApiError, notifications, type NotificationEndpoint, type NotificationEndpointRequest, type WebhookDeliveryState } from '@/api/client'
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
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { formatAge, formatDateTime } from '@/i18n'

const enabledStyle = 'border-emerald-600/30 bg-emerald-500/15 text-emerald-700 dark:border-emerald-400/30 dark:text-emerald-300'
const mutedStyle = 'border-border bg-muted text-muted-foreground'
const stateStyles: Record<WebhookDeliveryState, string> = {
  delivered: enabledStyle,
  pending: 'border-amber-600/30 bg-amber-500/15 text-amber-700 dark:border-amber-400/30 dark:text-amber-300',
  givenUp: 'border-red-600/30 bg-red-500/10 text-red-700 dark:border-red-400/30 dark:text-red-300',
}

type Form = { name: string; url: string; secret: string; enabled: boolean; events: string[] }

const emptyForm = (): Form => ({ name: '', url: '', secret: '', enabled: true, events: [] })

const toForm = (endpoint: NotificationEndpoint): Form => ({ name: endpoint.name, url: endpoint.url, secret: '', enabled: endpoint.enabled, events: endpoint.events })

/** Outgoing webhooks (blueprint §3.7, §3.9): where events go, signed, retried until they land. */
export function NotificationsPage() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const list = useQuery({ queryKey: ['notifications'], queryFn: notifications.list, refetchInterval: 30_000 })
  const [editing, setEditing] = useState<NotificationEndpoint | 'new' | null>(null)
  const [deliveriesOf, setDeliveriesOf] = useState<NotificationEndpoint | null>(null)
  const [pendingRemove, setPendingRemove] = useState<NotificationEndpoint | null>(null)
  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['notifications'] })
  const fail = (e: unknown, fallback: string) => toast.error(e instanceof ApiError ? e.message : fallback)

  const remove = useMutation({
    mutationFn: (id: number) => notifications.remove(id),
    onSuccess: async () => {
      setPendingRemove(null)
      await invalidate()
    },
    onError: (e) => {
      setPendingRemove(null)
      fail(e, t('notifications.removeFailed'))
    },
  })
  const test = useMutation({
    mutationFn: (id: number) => notifications.testSaved(id),
    onSuccess: (result) => toast.success(t('notifications.testOk', { status: result.statusCode ?? '', ms: result.elapsedMs })),
    onError: (e) => fail(e, t('notifications.testFailed')),
  })

  return (
    <>
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="text-2xl font-semibold tracking-tight">{t('notifications.title')}</h1>
        <Button type="button" size="sm" className="ml-auto" onClick={() => setEditing('new')} data-testid="add-notification">
          <PlusIcon />
          {t('notifications.add')}
        </Button>
      </div>
      <p className="max-w-3xl text-sm text-muted-foreground">{t('notifications.hint')}</p>

      {list.isPending && (
        <div className="flex flex-col gap-2">
          <Skeleton className="h-8 w-full" />
          <Skeleton className="h-8 w-full" />
        </div>
      )}

      {list.data && (
        <div className="overflow-x-auto rounded-lg border">
          <Table data-testid="notifications-table">
            <TableHeader>
              <TableRow>
                <TableHead>{t('notifications.name')}</TableHead>
                <TableHead>{t('notifications.events')}</TableHead>
                <TableHead>{t('notifications.state')}</TableHead>
                <TableHead>{t('notifications.deliveries')}</TableHead>
                <TableHead className="text-right">{t('versions.actions')}</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {list.data.map((endpoint) => (
                <TableRow key={endpoint.id} data-testid={`notification-${endpoint.id}`}>
                  <TableCell className="align-top">
                    <span className="font-medium">{endpoint.name}</span>
                    <span className="block max-w-[32ch] truncate text-xs text-muted-foreground" title={endpoint.url}>
                      {endpoint.url}
                    </span>
                  </TableCell>
                  <TableCell className="align-top">
                    {endpoint.events.length === 0 ? (
                      <span className="text-sm">{t('notifications.allEvents')}</span>
                    ) : (
                      <span className="flex max-w-[24rem] flex-wrap gap-1">
                        {endpoint.events.map((e) => (
                          <Badge key={e} variant="outline">
                            {e}
                          </Badge>
                        ))}
                      </span>
                    )}
                  </TableCell>
                  <TableCell className="align-top">
                    <span className="flex flex-wrap gap-1">
                      <Badge variant="outline" className={endpoint.enabled ? enabledStyle : mutedStyle}>
                        {endpoint.enabled ? t('notifications.enabled') : t('notifications.disabled')}
                      </Badge>
                      {endpoint.hasSecret && <Badge variant="outline">{t('notifications.signed')}</Badge>}
                    </span>
                  </TableCell>
                  <TableCell className="min-w-[14rem] align-top text-sm whitespace-normal">
                    {endpoint.lastDeliveredAt ? (
                      <span className="block">{t('notifications.lastDelivered', { age: formatAge(endpoint.lastDeliveredAt) })}</span>
                    ) : (
                      <span className="block text-muted-foreground">{t('notifications.neverDelivered')}</span>
                    )}
                    {(endpoint.pendingDeliveries > 0 || endpoint.failedDeliveries > 0) && (
                      <span className="block text-xs text-muted-foreground">
                        {t('notifications.pendingFailed', { pending: endpoint.pendingDeliveries, failed: endpoint.failedDeliveries })}
                      </span>
                    )}
                    {endpoint.lastError && <span className="block text-xs text-red-700 dark:text-red-300">{endpoint.lastError}</span>}
                  </TableCell>
                  <TableCell className="align-top">
                    <div className="flex items-center justify-end gap-1">
                      <Tooltip>
                        <TooltipTrigger asChild>
                          <Button type="button" variant="ghost" size="icon" onClick={() => setDeliveriesOf(endpoint)} aria-label={t('notifications.showDeliveries')}>
                            <ListIcon />
                          </Button>
                        </TooltipTrigger>
                        <TooltipContent>{t('notifications.showDeliveries')}</TooltipContent>
                      </Tooltip>
                      <Tooltip>
                        <TooltipTrigger asChild>
                          <Button type="button" variant="ghost" size="icon" onClick={() => test.mutate(endpoint.id)} disabled={test.isPending} aria-label={t('notifications.test')}>
                            <FlaskConicalIcon className={test.isPending && test.variables === endpoint.id ? 'animate-pulse' : undefined} />
                          </Button>
                        </TooltipTrigger>
                        <TooltipContent>{t('notifications.test')}</TooltipContent>
                      </Tooltip>
                      <Tooltip>
                        <TooltipTrigger asChild>
                          <Button type="button" variant="ghost" size="icon" onClick={() => setEditing(endpoint)} aria-label={t('notifications.edit')}>
                            <PencilIcon />
                          </Button>
                        </TooltipTrigger>
                        <TooltipContent>{t('notifications.edit')}</TooltipContent>
                      </Tooltip>
                      <Tooltip>
                        <TooltipTrigger asChild>
                          <Button
                            type="button"
                            variant="ghost"
                            size="icon"
                            className="text-destructive hover:text-destructive"
                            onClick={() => setPendingRemove(endpoint)}
                            aria-label={t('notifications.delete')}
                          >
                            <Trash2Icon />
                          </Button>
                        </TooltipTrigger>
                        <TooltipContent>{t('notifications.delete')}</TooltipContent>
                      </Tooltip>
                    </div>
                  </TableCell>
                </TableRow>
              ))}
              {list.data.length === 0 && (
                <TableRow>
                  <TableCell colSpan={5} className="text-muted-foreground">
                    {t('notifications.empty')}
                  </TableCell>
                </TableRow>
              )}
            </TableBody>
          </Table>
        </div>
      )}

      {editing !== null && (
        <NotificationDialog key={editing === 'new' ? 'new' : editing.id} endpoint={editing === 'new' ? null : editing} onClose={() => setEditing(null)} onSaved={() => void invalidate()} />
      )}

      {deliveriesOf !== null && <DeliveriesDialog endpoint={deliveriesOf} onClose={() => setDeliveriesOf(null)} />}

      <AlertDialog open={pendingRemove !== null} onOpenChange={(open) => !open && setPendingRemove(null)}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>{t('notifications.deleteTitle')}</AlertDialogTitle>
            <AlertDialogDescription>{t('notifications.confirmDelete', { name: pendingRemove?.name ?? '' })}</AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>{t('common.cancel')}</AlertDialogCancel>
            <AlertDialogAction
              onClick={(event) => {
                event.preventDefault()
                if (pendingRemove) remove.mutate(pendingRemove.id)
              }}
              disabled={remove.isPending}
            >
              {t('notifications.delete')}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </>
  )
}

function NotificationDialog({ endpoint, onClose, onSaved }: { endpoint: NotificationEndpoint | null; onClose: () => void; onSaved: () => void }) {
  const { t } = useTranslation()
  const [form, setForm] = useState<Form>(endpoint ? toForm(endpoint) : emptyForm())
  const types = useQuery({ queryKey: ['notifications', 'events'], queryFn: notifications.eventTypes, staleTime: Infinity })
  const set = <K extends keyof Form>(key: K, value: Form[K]) => setForm((f) => ({ ...f, [key]: value }))

  // An empty secret on an edit keeps the endpoint unsigned: the API stores what it is sent, and the secret is never read back.
  const payload = (): NotificationEndpointRequest => ({
    name: form.name.trim(),
    url: form.url.trim(),
    secret: form.secret || null,
    enabled: form.enabled,
    events: form.events,
  })

  const save = useMutation({
    mutationFn: () => (endpoint ? notifications.update(endpoint.id, payload()) : notifications.add(payload())),
    onSuccess: () => {
      onSaved()
      onClose()
    },
  })
  const test = useMutation({
    mutationFn: () => notifications.test(payload()),
    onSuccess: (result) => toast.success(t('notifications.testOk', { status: result.statusCode ?? '', ms: result.elapsedMs })),
    onError: (e) => toast.error(e instanceof ApiError ? e.message : t('notifications.testFailed')),
  })

  const submit = (e: FormEvent) => {
    e.preventDefault()
    save.mutate()
  }

  const toggleEvent = (type: string) => set('events', form.events.includes(type) ? form.events.filter((e) => e !== type) : [...form.events, type])
  const canSubmit = form.name.trim().length > 0 && /^https?:\/\//i.test(form.url.trim())

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="sm:max-w-2xl">
        <form onSubmit={submit} data-testid="notification-form">
          <DialogHeader>
            <DialogTitle>{endpoint ? t('notifications.editTitle', { name: endpoint.name }) : t('notifications.new')}</DialogTitle>
          </DialogHeader>

          <div className="my-6 grid gap-4 sm:grid-cols-2">
            <div className="grid grid-cols-1 gap-2">
              <Label htmlFor="nt-name">{t('notifications.name')}</Label>
              <Input id="nt-name" value={form.name} onChange={(e) => set('name', e.target.value)} placeholder="Home Assistant" autoFocus />
            </div>
            <div className="grid grid-cols-1 gap-2">
              <Label htmlFor="nt-url">{t('notifications.url')}</Label>
              <Input id="nt-url" value={form.url} onChange={(e) => set('url', e.target.value)} placeholder="https://example.org/hooks/bigarr" />
            </div>
            <div className="grid grid-cols-1 gap-2 sm:col-span-2">
              <Label htmlFor="nt-secret">{t('notifications.secret')}</Label>
              <Input id="nt-secret" type="password" value={form.secret} onChange={(e) => set('secret', e.target.value)} autoComplete="new-password" />
              <p className="text-xs text-muted-foreground">{endpoint?.hasSecret ? t('notifications.secretKeepHint') : t('notifications.secretHint')}</p>
            </div>
            <div className="flex items-center gap-2 sm:col-span-2">
              <Checkbox id="nt-enabled" checked={form.enabled} onCheckedChange={(v) => set('enabled', v === true)} />
              <Label htmlFor="nt-enabled" className="font-normal">
                {t('notifications.enabledToggle')}
              </Label>
            </div>

            <fieldset className="grid grid-cols-1 gap-2 sm:col-span-2">
              <legend className="text-sm font-medium">{t('notifications.eventsLegend')}</legend>
              <p className="text-xs text-muted-foreground">{form.events.length === 0 ? t('notifications.allEventsHint') : t('notifications.someEventsHint', { count: form.events.length })}</p>
              <div className="grid gap-x-4 gap-y-2 sm:grid-cols-2" data-testid="notification-events">
                {types.data?.map((type) => (
                  <label key={type.type} className="flex items-start gap-2 text-sm font-normal">
                    <Checkbox className="mt-0.5" checked={form.events.includes(type.type)} onCheckedChange={() => toggleEvent(type.type)} />
                    <span>
                      <span className="font-medium">{type.type}</span>
                      <span className="block text-xs text-muted-foreground">{type.description}</span>
                    </span>
                  </label>
                ))}
              </div>
            </fieldset>
          </div>

          {save.error && (
            <Alert variant="destructive" className="mb-4">
              <AlertDescription>{save.error instanceof ApiError ? save.error.message : t('common.unexpectedError')}</AlertDescription>
            </Alert>
          )}

          <DialogFooter className="items-center sm:justify-between">
            <Button type="button" variant="outline" onClick={() => test.mutate()} disabled={test.isPending || !canSubmit} data-testid="notification-test">
              <FlaskConicalIcon className={test.isPending ? 'animate-pulse' : undefined} />
              {test.isPending ? t('notifications.testing') : t('notifications.test')}
            </Button>
            <div className="flex gap-2">
              <Button type="button" variant="ghost" onClick={onClose}>
                {t('common.cancel')}
              </Button>
              <Button type="submit" disabled={save.isPending || !canSubmit} data-testid="notification-save">
                {t('common.save')}
              </Button>
            </div>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}

function DeliveriesDialog({ endpoint, onClose }: { endpoint: NotificationEndpoint; onClose: () => void }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const deliveries = useQuery({ queryKey: ['notifications', endpoint.id, 'deliveries'], queryFn: () => notifications.deliveries(endpoint.id), refetchInterval: 5_000 })
  const retry = useMutation({
    mutationFn: (id: number) => notifications.retryDelivery(id),
    onSuccess: async () => {
      toast.success(t('notifications.retried'))
      await queryClient.invalidateQueries({ queryKey: ['notifications'] })
    },
    onError: (e) => toast.error(e instanceof ApiError ? e.message : t('common.unexpectedError')),
  })

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="sm:max-w-3xl">
        <DialogHeader>
          <DialogTitle>{t('notifications.deliveriesTitle', { name: endpoint.name })}</DialogTitle>
        </DialogHeader>
        <div className="max-h-[60vh] overflow-auto rounded-lg border">
          <Table data-testid="deliveries-table">
            <TableHeader>
              <TableRow>
                <TableHead>{t('notifications.event')}</TableHead>
                <TableHead>{t('notifications.state')}</TableHead>
                <TableHead>{t('notifications.attempts')}</TableHead>
                <TableHead>{t('notifications.lastAttempt')}</TableHead>
                <TableHead>{t('notifications.nextAttempt')}</TableHead>
                <TableHead className="text-right">{t('versions.actions')}</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {deliveries.data?.map((d) => (
                <TableRow key={d.id}>
                  <TableCell className="align-top">
                    <span className="font-medium">{d.eventType}</span>
                    <span className="block text-xs text-muted-foreground">#{d.eventId}</span>
                  </TableCell>
                  <TableCell className="align-top">
                    <Badge variant="outline" className={cn(stateStyles[d.state])}>
                      {t(`notifications.states.${d.state}`)}
                    </Badge>
                    {d.lastError && <span className="mt-1 block max-w-[20rem] text-xs text-red-700 dark:text-red-300">{d.lastError}</span>}
                  </TableCell>
                  <TableCell className="align-top whitespace-nowrap">
                    {d.attempts}
                    {d.lastStatusCode !== null && <span className="block text-xs text-muted-foreground">HTTP {d.lastStatusCode}</span>}
                  </TableCell>
                  <TableCell className="align-top whitespace-nowrap">{d.lastAttemptAt ? formatDateTime(d.lastAttemptAt) : t('common.none')}</TableCell>
                  <TableCell className="align-top whitespace-nowrap">{d.nextAttemptAt ? formatDateTime(d.nextAttemptAt) : t('common.none')}</TableCell>
                  <TableCell className="align-top">
                    <div className="flex justify-end">
                      {d.state !== 'delivered' && (
                        <Tooltip>
                          <TooltipTrigger asChild>
                            <Button type="button" variant="ghost" size="icon" onClick={() => retry.mutate(d.id)} disabled={retry.isPending} aria-label={t('notifications.retry')}>
                              <RotateCcwIcon />
                            </Button>
                          </TooltipTrigger>
                          <TooltipContent>{t('notifications.retry')}</TooltipContent>
                        </Tooltip>
                      )}
                    </div>
                  </TableCell>
                </TableRow>
              ))}
              {deliveries.data?.length === 0 && (
                <TableRow>
                  <TableCell colSpan={6} className="text-muted-foreground">
                    {t('notifications.noDeliveries')}
                  </TableCell>
                </TableRow>
              )}
            </TableBody>
          </Table>
        </div>
        <DialogFooter>
          <Button type="button" variant="ghost" onClick={onClose}>
            {t('common.close')}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
