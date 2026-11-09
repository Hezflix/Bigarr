import { useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { FlaskConicalIcon, PencilIcon, PlusIcon, RefreshCwIcon, Trash2Icon } from 'lucide-react'
import type { TFunction } from 'i18next'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ApiError, mediaServers, type MediaServer, type MediaServerKind, type MediaServerRequest, type MediaServerTestResult } from '@/api/client'
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
import { PlayRulesCard } from '@/features/playback/PlayRulesCard'

const kinds: MediaServerKind[] = ['plex', 'jellyfin', 'emby']

const emptyForm = (): MediaServerRequest => ({ name: '', kind: 'plex', url: '', token: '', enabled: true, pathMappings: [] })

const toForm = (server: MediaServer): MediaServerRequest => ({
  name: server.name,
  kind: server.kind,
  url: server.url,
  token: server.token,
  enabled: server.enabled,
  pathMappings: server.pathMappings.map((m) => ({ ...m })),
})

function testSummary(t: TFunction, result: MediaServerTestResult): string {
  return t('mediaServers.testOk', { name: result.name ?? '?', version: result.version ?? '', count: result.movieLibraries })
}

const formatDate = (value: string | null) => (value ? new Date(value).toLocaleString() : null)

/** The Plex, Jellyfin and Emby servers told about library changes (M8). */
export function MediaServersPage() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const list = useQuery({ queryKey: ['mediaServers'], queryFn: mediaServers.list })
  const [editing, setEditing] = useState<MediaServer | 'new' | null>(null)
  const [pendingRemove, setPendingRemove] = useState<MediaServer | null>(null)
  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['mediaServers'] })
  const fail = (e: unknown, fallback: string) => toast.error(e instanceof ApiError ? e.message : fallback)

  const remove = useMutation({
    mutationFn: (id: number) => mediaServers.remove(id),
    onSuccess: async () => {
      setPendingRemove(null)
      await invalidate()
    },
    onError: (e) => {
      setPendingRemove(null)
      fail(e, t('mediaServers.removeFailed'))
    },
  })
  const test = useMutation({
    mutationFn: (id: number) => mediaServers.testSaved(id),
    onSuccess: (result) => toast.success(testSummary(t, result)),
    onError: (e) => fail(e, t('mediaServers.testFailed')),
  })
  const refresh = useMutation({
    mutationFn: (id: number) => mediaServers.refresh(id),
    onSuccess: async (server) => {
      toast.success(t('mediaServers.refreshOk', { name: server.name }))
      await invalidate()
    },
    onError: async (e) => {
      fail(e, t('mediaServers.refreshFailed'))
      await invalidate()
    },
  })

  return (
    <>
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="text-2xl font-semibold tracking-tight">{t('mediaServers.title')}</h1>
        <Button type="button" size="sm" className="ml-auto" onClick={() => setEditing('new')} data-testid="add-media-server">
          <PlusIcon />
          {t('mediaServers.add')}
        </Button>
      </div>
      <p className="max-w-3xl text-sm text-muted-foreground">{t('mediaServers.hint')}</p>

      <div className="overflow-x-auto rounded-lg border">
        <Table data-testid="media-servers-table">
          <TableHeader>
            <TableRow>
              <TableHead>{t('mediaServers.name')}</TableHead>
              <TableHead>{t('mediaServers.kind')}</TableHead>
              <TableHead>{t('mediaServers.state')}</TableHead>
              <TableHead>{t('mediaServers.lastRefresh')}</TableHead>
              <TableHead className="text-right">{t('versions.actions')}</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {list.data?.map((server) => (
              <TableRow key={server.id} data-testid={`media-server-${server.id}`}>
                <TableCell className="align-top">
                  <span className="font-medium">{server.name}</span>
                  <span className="block text-xs text-muted-foreground break-all">{server.url}</span>
                </TableCell>
                <TableCell className="align-top">{t(`mediaServers.kinds.${server.kind}`)}</TableCell>
                <TableCell className="align-top">
                  <span className="flex flex-wrap gap-1">
                    {server.enabled ? (
                      <Badge variant="outline" className="border-emerald-600/30 bg-emerald-500/15 text-emerald-700 dark:border-emerald-400/30 dark:text-emerald-300">
                        {t('mediaServers.enabled')}
                      </Badge>
                    ) : (
                      <Badge variant="outline" className="border-border bg-muted text-muted-foreground">
                        {t('mediaServers.disabled')}
                      </Badge>
                    )}
                    {server.isFailing && (
                      <Badge variant="outline" className="border-red-600/30 bg-red-500/15 text-red-700 dark:border-red-400/30 dark:text-red-300">
                        {t('mediaServers.failing')}
                      </Badge>
                    )}
                  </span>
                </TableCell>
                <TableCell className="align-top">
                  <span className="text-sm">{formatDate(server.lastRefreshAt) ?? t('mediaServers.never')}</span>
                  {server.isFailing && server.lastError && <span className="block max-w-md text-xs text-destructive whitespace-normal">{server.lastError}</span>}
                </TableCell>
                <TableCell className="align-top">
                  <div className="flex items-center justify-end gap-1">
                    <Tooltip>
                      <TooltipTrigger asChild>
                        <Button type="button" variant="ghost" size="icon" onClick={() => test.mutate(server.id)} disabled={test.isPending} aria-label={t('mediaServers.test')}>
                          <FlaskConicalIcon className={test.isPending && test.variables === server.id ? 'animate-pulse' : undefined} />
                        </Button>
                      </TooltipTrigger>
                      <TooltipContent>{t('mediaServers.test')}</TooltipContent>
                    </Tooltip>
                    <Tooltip>
                      <TooltipTrigger asChild>
                        <Button type="button" variant="ghost" size="icon" onClick={() => refresh.mutate(server.id)} disabled={refresh.isPending || !server.enabled} aria-label={t('mediaServers.refresh')}>
                          <RefreshCwIcon className={refresh.isPending && refresh.variables === server.id ? 'animate-spin' : undefined} />
                        </Button>
                      </TooltipTrigger>
                      <TooltipContent>{t('mediaServers.refresh')}</TooltipContent>
                    </Tooltip>
                    <Tooltip>
                      <TooltipTrigger asChild>
                        <Button type="button" variant="ghost" size="icon" onClick={() => setEditing(server)} aria-label={t('mediaServers.edit')}>
                          <PencilIcon />
                        </Button>
                      </TooltipTrigger>
                      <TooltipContent>{t('mediaServers.edit')}</TooltipContent>
                    </Tooltip>
                    <Tooltip>
                      <TooltipTrigger asChild>
                        <Button
                          type="button"
                          variant="ghost"
                          size="icon"
                          className="text-destructive hover:text-destructive"
                          onClick={() => setPendingRemove(server)}
                          aria-label={t('mediaServers.delete')}
                        >
                          <Trash2Icon />
                        </Button>
                      </TooltipTrigger>
                      <TooltipContent>{t('mediaServers.delete')}</TooltipContent>
                    </Tooltip>
                  </div>
                </TableCell>
              </TableRow>
            ))}
            {list.data?.length === 0 && (
              <TableRow>
                <TableCell colSpan={5} className="text-muted-foreground">
                  {t('mediaServers.empty')}
                </TableCell>
              </TableRow>
            )}
          </TableBody>
        </Table>
      </div>

      {editing !== null && (
        <MediaServerDialog
          key={editing === 'new' ? 'new' : editing.id}
          server={editing === 'new' ? null : editing}
          onClose={() => setEditing(null)}
          onSaved={() => void invalidate()}
        />
      )}

      <AlertDialog open={pendingRemove !== null} onOpenChange={(open) => !open && setPendingRemove(null)}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>{t('mediaServers.deleteTitle')}</AlertDialogTitle>
            <AlertDialogDescription>{t('mediaServers.confirmDelete', { name: pendingRemove?.name ?? '' })}</AlertDialogDescription>
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
              {t('mediaServers.delete')}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>

      <PlayRulesCard />
    </>
  )
}

function MediaServerDialog({ server, onClose, onSaved }: { server: MediaServer | null; onClose: () => void; onSaved: () => void }) {
  const { t } = useTranslation()
  const [form, setForm] = useState<MediaServerRequest>(server ? toForm(server) : emptyForm())
  const set = <K extends keyof MediaServerRequest>(key: K, value: MediaServerRequest[K]) => setForm((f) => ({ ...f, [key]: value }))

  const payload = (): MediaServerRequest => ({
    ...form,
    name: form.name.trim(),
    url: form.url.trim(),
    token: form.token.trim(),
    pathMappings: form.pathMappings.map((m) => ({ from: m.from.trim(), to: m.to.trim() })).filter((m) => m.from.length > 0 || m.to.length > 0),
  })

  const save = useMutation({
    mutationFn: () => (server ? mediaServers.update(server.id, payload()) : mediaServers.add(payload())),
    onSuccess: () => {
      onSaved()
      onClose()
    },
  })
  const test = useMutation({
    mutationFn: () => mediaServers.test(payload()),
    onSuccess: (result) => toast.success(testSummary(t, result)),
    onError: (e) => toast.error(e instanceof ApiError ? e.message : t('mediaServers.testFailed')),
  })

  const submit = (e: FormEvent) => {
    e.preventDefault()
    save.mutate()
  }

  const mappingsComplete = form.pathMappings.every((m) => (m.from.trim().length > 0) === (m.to.trim().length > 0))
  const canSubmit = form.name.trim().length > 0 && form.url.trim().length > 0 && form.token.trim().length > 0 && mappingsComplete

  const setMapping = (index: number, key: 'from' | 'to', value: string) =>
    setForm((f) => ({ ...f, pathMappings: f.pathMappings.map((m, i) => (i === index ? { ...m, [key]: value } : m)) }))
  const removeMapping = (index: number) => setForm((f) => ({ ...f, pathMappings: f.pathMappings.filter((_, i) => i !== index) }))

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="sm:max-w-2xl">
        <form onSubmit={submit} data-testid="media-server-form">
          <DialogHeader>
            <DialogTitle>{server ? t('mediaServers.editTitle', { name: server.name }) : t('mediaServers.new')}</DialogTitle>
          </DialogHeader>

          <div className="my-6 grid gap-4 sm:grid-cols-2">
            <div className="grid grid-cols-1 gap-2">
              <Label htmlFor="ms-name">{t('mediaServers.name')}</Label>
              <Input id="ms-name" value={form.name} onChange={(e) => set('name', e.target.value)} placeholder="Plex" autoFocus />
            </div>

            <div className="grid grid-cols-1 gap-2">
              <Label htmlFor="ms-kind">{t('mediaServers.kind')}</Label>
              <Select value={form.kind} onValueChange={(value) => set('kind', value as MediaServerKind)}>
                <SelectTrigger id="ms-kind" className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {kinds.map((kind) => (
                    <SelectItem key={kind} value={kind}>
                      {t(`mediaServers.kinds.${kind}`)}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>

            <div className="grid grid-cols-1 gap-2 sm:col-span-2">
              <Label htmlFor="ms-url">{t('mediaServers.url')}</Label>
              <Input id="ms-url" value={form.url} onChange={(e) => set('url', e.target.value)} placeholder={form.kind === 'plex' ? 'http://plex:32400' : 'http://jellyfin:8096'} />
              <p className="text-xs text-muted-foreground">{t('mediaServers.urlHint')}</p>
            </div>

            <div className="grid grid-cols-1 gap-2 sm:col-span-2">
              <Label htmlFor="ms-token">{t('mediaServers.token')}</Label>
              <Input id="ms-token" type="password" value={form.token} onChange={(e) => set('token', e.target.value)} autoComplete="new-password" />
              <p className="text-xs text-muted-foreground">{form.kind === 'plex' ? t('mediaServers.tokenHintPlex') : t('mediaServers.tokenHintApiKey')}</p>
            </div>

            <div className="flex flex-col gap-2 sm:col-span-2">
              <Label>{t('mediaServers.pathMappings')}</Label>
              <p className="text-xs text-muted-foreground">{t('mediaServers.pathMappingsHint')}</p>
              {form.pathMappings.map((mapping, index) => (
                <div key={index} className="flex flex-wrap items-center gap-2">
                  <Input
                    className="min-w-40 flex-1"
                    value={mapping.from}
                    onChange={(e) => setMapping(index, 'from', e.target.value)}
                    placeholder="/data"
                    aria-label={t('mediaServers.from')}
                  />
                  <span className="text-muted-foreground">→</span>
                  <Input
                    className="min-w-40 flex-1"
                    value={mapping.to}
                    onChange={(e) => setMapping(index, 'to', e.target.value)}
                    placeholder="/mnt/media"
                    aria-label={t('mediaServers.to')}
                  />
                  <Button type="button" variant="ghost" size="icon" onClick={() => removeMapping(index)} aria-label={t('mediaServers.removeMapping')}>
                    <Trash2Icon />
                  </Button>
                </div>
              ))}
              <div>
                <Button type="button" variant="outline" size="sm" onClick={() => set('pathMappings', [...form.pathMappings, { from: '', to: '' }])}>
                  <PlusIcon />
                  {t('mediaServers.addMapping')}
                </Button>
              </div>
            </div>

            <div className="flex items-center gap-2 sm:col-span-2">
              <Checkbox id="ms-enabled" checked={form.enabled} onCheckedChange={(value) => set('enabled', value === true)} />
              <Label htmlFor="ms-enabled" className="font-normal">
                {t('mediaServers.enabledToggle')}
              </Label>
            </div>
          </div>

          {save.error && (
            <Alert variant="destructive" className="mb-4">
              <AlertDescription>{save.error instanceof ApiError ? save.error.message : t('common.unexpectedError')}</AlertDescription>
            </Alert>
          )}

          <DialogFooter className="items-center sm:justify-between">
            <Button type="button" variant="outline" onClick={() => test.mutate()} disabled={test.isPending || !canSubmit} data-testid="media-server-test">
              <FlaskConicalIcon className={test.isPending ? 'animate-pulse' : undefined} />
              {test.isPending ? t('mediaServers.testing') : t('mediaServers.test')}
            </Button>
            <div className="flex gap-2">
              <Button type="button" variant="ghost" onClick={onClose}>
                {t('common.cancel')}
              </Button>
              <Button type="submit" disabled={save.isPending || !canSubmit} data-testid="media-server-save">
                {t('mediaServers.save')}
              </Button>
            </div>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
