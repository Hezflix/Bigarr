import { useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { FlaskConicalIcon, PencilIcon, PlusIcon, Trash2Icon } from 'lucide-react'
import type { TFunction } from 'i18next'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ApiError, downloadClients, type DownloadClient, type DownloadClientImplementation, type DownloadClientRequest, type DownloadClientTestResult } from '@/api/client'
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
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'

const implementations: DownloadClientImplementation[] = ['qBittorrent', 'mock']

const emptyForm = (): DownloadClientRequest => ({
  name: '',
  implementation: 'qBittorrent',
  enabled: true,
  host: '',
  port: 8080,
  useSsl: false,
  urlBase: '',
  username: '',
  password: '',
  category: 'bigarr-movies',
  addPaused: false,
  priority: 1,
})

const toForm = (client: DownloadClient): DownloadClientRequest => ({
  name: client.name,
  implementation: client.implementation,
  enabled: client.enabled,
  host: client.host ?? '',
  port: client.port || 8080,
  useSsl: client.useSsl,
  urlBase: client.urlBase ?? '',
  username: client.username ?? '',
  password: client.password ?? '',
  category: client.category,
  addPaused: client.addPaused,
  priority: client.priority,
})

function testSummary(t: TFunction, result: DownloadClientTestResult): string {
  return t(result.categoryCreated ? 'downloadClients.testOkCreated' : 'downloadClients.testOk', { version: result.version, category: result.category })
}

/** Where grabs go: qBittorrent instances and the category Bigarr owns in each (blueprint §3.7). */
export function DownloadClientsPage() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const list = useQuery({ queryKey: ['downloadClients'], queryFn: downloadClients.list })
  const [editing, setEditing] = useState<DownloadClient | 'new' | null>(null)
  const [pendingRemove, setPendingRemove] = useState<DownloadClient | null>(null)
  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['downloadClients'] })
  const fail = (e: unknown, fallback: string) => toast.error(e instanceof ApiError ? e.message : fallback)

  const remove = useMutation({
    mutationFn: (id: number) => downloadClients.remove(id),
    onSuccess: async () => {
      setPendingRemove(null)
      await invalidate()
    },
    onError: (e) => {
      setPendingRemove(null)
      fail(e, t('downloadClients.removeFailed'))
    },
  })
  const test = useMutation({
    mutationFn: (id: number) => downloadClients.testSaved(id),
    onSuccess: (result) => toast.success(testSummary(t, result)),
    onError: (e) => fail(e, t('downloadClients.testFailed')),
  })

  return (
    <>
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="text-2xl font-semibold tracking-tight">{t('downloadClients.title')}</h1>
        <Button type="button" size="sm" className="ml-auto" onClick={() => setEditing('new')} data-testid="add-download-client">
          <PlusIcon />
          {t('downloadClients.add')}
        </Button>
      </div>
      <p className="max-w-3xl text-sm text-muted-foreground">{t('downloadClients.hint')}</p>

      <div className="overflow-x-auto rounded-lg border">
        <Table data-testid="download-clients-table">
          <TableHeader>
            <TableRow>
              <TableHead>{t('downloadClients.name')}</TableHead>
              <TableHead>{t('downloadClients.implementation')}</TableHead>
              <TableHead>{t('downloadClients.category')}</TableHead>
              <TableHead>{t('downloadClients.priority')}</TableHead>
              <TableHead>{t('downloadClients.state')}</TableHead>
              <TableHead className="text-right">{t('versions.actions')}</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {list.data?.map((client) => (
              <TableRow key={client.id} data-testid={`download-client-${client.id}`}>
                <TableCell className="align-top">
                  <span className="font-medium">{client.name}</span>
                  {client.host && (
                    <span className="block text-xs text-muted-foreground">
                      {client.useSsl ? 'https' : 'http'}://{client.host}:{client.port}
                      {client.urlBase ?? ''}
                    </span>
                  )}
                </TableCell>
                <TableCell className="align-top">{t(`downloadClients.implementations.${client.implementation}`)}</TableCell>
                <TableCell className="align-top">
                  <Badge variant="secondary">{client.category}</Badge>
                </TableCell>
                <TableCell className="align-top">{client.priority}</TableCell>
                <TableCell className="align-top">
                  {client.enabled ? (
                    <Badge variant="outline" className="border-emerald-600/30 bg-emerald-500/15 text-emerald-700 dark:border-emerald-400/30 dark:text-emerald-300">
                      {t('downloadClients.enabled')}
                    </Badge>
                  ) : (
                    <Badge variant="outline" className="border-border bg-muted text-muted-foreground">
                      {t('downloadClients.disabled')}
                    </Badge>
                  )}
                </TableCell>
                <TableCell className="align-top">
                  <div className="flex items-center justify-end gap-1">
                    <Tooltip>
                      <TooltipTrigger asChild>
                        <Button type="button" variant="ghost" size="icon" onClick={() => test.mutate(client.id)} disabled={test.isPending} aria-label={t('downloadClients.test')}>
                          <FlaskConicalIcon className={test.isPending && test.variables === client.id ? 'animate-pulse' : undefined} />
                        </Button>
                      </TooltipTrigger>
                      <TooltipContent>{t('downloadClients.test')}</TooltipContent>
                    </Tooltip>
                    <Tooltip>
                      <TooltipTrigger asChild>
                        <Button type="button" variant="ghost" size="icon" onClick={() => setEditing(client)} aria-label={t('downloadClients.edit')}>
                          <PencilIcon />
                        </Button>
                      </TooltipTrigger>
                      <TooltipContent>{t('downloadClients.edit')}</TooltipContent>
                    </Tooltip>
                    <Tooltip>
                      <TooltipTrigger asChild>
                        <Button
                          type="button"
                          variant="ghost"
                          size="icon"
                          className="text-destructive hover:text-destructive"
                          onClick={() => setPendingRemove(client)}
                          aria-label={t('downloadClients.delete')}
                        >
                          <Trash2Icon />
                        </Button>
                      </TooltipTrigger>
                      <TooltipContent>{t('downloadClients.delete')}</TooltipContent>
                    </Tooltip>
                  </div>
                </TableCell>
              </TableRow>
            ))}
            {list.data?.length === 0 && (
              <TableRow>
                <TableCell colSpan={6} className="text-muted-foreground">
                  {t('downloadClients.empty')}
                </TableCell>
              </TableRow>
            )}
          </TableBody>
        </Table>
      </div>

      {editing !== null && (
        <DownloadClientDialog
          key={editing === 'new' ? 'new' : editing.id}
          client={editing === 'new' ? null : editing}
          onClose={() => setEditing(null)}
          onSaved={() => void invalidate()}
        />
      )}

      <AlertDialog open={pendingRemove !== null} onOpenChange={(open) => !open && setPendingRemove(null)}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>{t('downloadClients.deleteTitle')}</AlertDialogTitle>
            <AlertDialogDescription>{t('downloadClients.confirmDelete', { name: pendingRemove?.name ?? '' })}</AlertDialogDescription>
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
              {t('downloadClients.delete')}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </>
  )
}

function DownloadClientDialog({ client, onClose, onSaved }: { client: DownloadClient | null; onClose: () => void; onSaved: () => void }) {
  const { t } = useTranslation()
  const [form, setForm] = useState<DownloadClientRequest>(client ? toForm(client) : emptyForm())
  const set = <K extends keyof DownloadClientRequest>(key: K, value: DownloadClientRequest[K]) => setForm((f) => ({ ...f, [key]: value }))
  const isQbt = form.implementation === 'qBittorrent'

  const payload = (): DownloadClientRequest => ({
    ...form,
    name: form.name.trim(),
    host: isQbt ? form.host?.trim() || null : null,
    urlBase: isQbt ? form.urlBase?.trim() || null : null,
    username: isQbt ? form.username?.trim() || null : null,
    password: isQbt ? form.password || null : null,
    category: form.category.trim(),
  })

  const save = useMutation({
    mutationFn: () => (client ? downloadClients.update(client.id, payload()) : downloadClients.add(payload())),
    onSuccess: () => {
      onSaved()
      onClose()
    },
  })
  const test = useMutation({
    mutationFn: () => downloadClients.test(payload()),
    onSuccess: (result) => toast.success(testSummary(t, result)),
    onError: (e) => toast.error(e instanceof ApiError ? e.message : t('downloadClients.testFailed')),
  })

  const submit = (e: FormEvent) => {
    e.preventDefault()
    save.mutate()
  }

  const canSubmit = form.name.trim().length > 0 && form.category.trim().length > 0 && (!isQbt || (form.host ?? '').trim().length > 0)
  const number = (value: string, fallback: number) => (value === '' ? fallback : Number(value))

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="sm:max-w-2xl">
        <form onSubmit={submit} data-testid="download-client-form">
          <DialogHeader>
            <DialogTitle>{client ? t('downloadClients.editTitle', { name: client.name }) : t('downloadClients.new')}</DialogTitle>
          </DialogHeader>

          <div className="my-6 grid gap-4 sm:grid-cols-2">
            <div className="grid grid-cols-1 gap-2">
              <Label htmlFor="dc-name">{t('downloadClients.name')}</Label>
              <Input id="dc-name" value={form.name} onChange={(e) => set('name', e.target.value)} placeholder="qBittorrent" autoFocus />
            </div>

            <div className="grid grid-cols-1 gap-2">
              <Label htmlFor="dc-implementation">{t('downloadClients.implementation')}</Label>
              <Select value={form.implementation} onValueChange={(value) => set('implementation', value as DownloadClientImplementation)}>
                <SelectTrigger id="dc-implementation" className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {implementations.map((i) => (
                    <SelectItem key={i} value={i}>
                      {t(`downloadClients.implementations.${i}`)}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              {!isQbt && <p className="text-xs text-muted-foreground">{t('downloadClients.mockHint')}</p>}
            </div>

            {isQbt && (
              <>
                <div className="grid grid-cols-1 gap-2">
                  <Label htmlFor="dc-host">{t('downloadClients.host')}</Label>
                  <Input id="dc-host" value={form.host ?? ''} onChange={(e) => set('host', e.target.value)} placeholder="qbittorrent" />
                  <p className="text-xs text-muted-foreground">{t('downloadClients.hostHint')}</p>
                </div>
                <div className="grid grid-cols-2 gap-4">
                  <div className="grid grid-cols-1 gap-2">
                    <Label htmlFor="dc-port">{t('downloadClients.port')}</Label>
                    <Input id="dc-port" type="number" min={1} max={65535} value={form.port} onChange={(e) => set('port', number(e.target.value, 8080))} />
                  </div>
                  <div className="grid grid-cols-1 gap-2">
                    <Label htmlFor="dc-urlbase">{t('downloadClients.urlBase')}</Label>
                    <Input id="dc-urlbase" value={form.urlBase ?? ''} onChange={(e) => set('urlBase', e.target.value)} placeholder="/qbittorrent" />
                  </div>
                </div>
                <div className="grid grid-cols-1 gap-2">
                  <Label htmlFor="dc-username">{t('downloadClients.username')}</Label>
                  <Input id="dc-username" value={form.username ?? ''} onChange={(e) => set('username', e.target.value)} autoComplete="off" />
                </div>
                <div className="grid grid-cols-1 gap-2">
                  <Label htmlFor="dc-password">{t('downloadClients.password')}</Label>
                  <Input id="dc-password" type="password" value={form.password ?? ''} onChange={(e) => set('password', e.target.value)} autoComplete="new-password" />
                  <p className="text-xs text-muted-foreground">{t('downloadClients.passwordHint')}</p>
                </div>
              </>
            )}

            <div className="grid grid-cols-1 gap-2">
              <Label htmlFor="dc-category">{t('downloadClients.category')}</Label>
              <Input id="dc-category" value={form.category} onChange={(e) => set('category', e.target.value)} placeholder="bigarr-movies" />
              <p className="text-xs text-muted-foreground">{t('downloadClients.categoryHint')}</p>
            </div>

            <div className="grid grid-cols-1 gap-2">
              <Label htmlFor="dc-priority">{t('downloadClients.priority')}</Label>
              <Input id="dc-priority" type="number" min={1} max={50} value={form.priority} onChange={(e) => set('priority', number(e.target.value, 1))} />
              <p className="text-xs text-muted-foreground">{t('downloadClients.priorityHint')}</p>
            </div>

            <div className="flex flex-col gap-3 sm:col-span-2">
              <Toggle id="dc-enabled" label={t('downloadClients.enabledToggle')} checked={form.enabled} onChange={(v) => set('enabled', v)} />
              {isQbt && <Toggle id="dc-ssl" label={t('downloadClients.useSsl')} checked={form.useSsl} onChange={(v) => set('useSsl', v)} />}
              <Toggle id="dc-paused" label={t('downloadClients.addPaused')} checked={form.addPaused} onChange={(v) => set('addPaused', v)} />
            </div>
          </div>

          {save.error && (
            <Alert variant="destructive" className="mb-4">
              <AlertDescription>{save.error instanceof ApiError ? save.error.message : t('common.unexpectedError')}</AlertDescription>
            </Alert>
          )}

          <DialogFooter className="items-center sm:justify-between">
            <Button type="button" variant="outline" onClick={() => test.mutate()} disabled={test.isPending || !canSubmit} data-testid="download-client-test">
              <FlaskConicalIcon className={test.isPending ? 'animate-pulse' : undefined} />
              {test.isPending ? t('downloadClients.testing') : t('downloadClients.test')}
            </Button>
            <div className="flex gap-2">
              <Button type="button" variant="ghost" onClick={onClose}>
                {t('common.cancel')}
              </Button>
              <Button type="submit" disabled={save.isPending || !canSubmit} data-testid="download-client-save">
                {t('downloadClients.save')}
              </Button>
            </div>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}

function Toggle({ id, label, checked, onChange }: { id: string; label: string; checked: boolean; onChange: (value: boolean) => void }) {
  return (
    <div className="flex items-center gap-2">
      <Checkbox id={id} checked={checked} onCheckedChange={(value) => onChange(value === true)} />
      <Label htmlFor={id} className="font-normal">
        {label}
      </Label>
    </div>
  )
}
