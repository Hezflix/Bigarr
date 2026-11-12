import { useEffect, useMemo, useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { CopyIcon, FlaskConicalIcon, PencilIcon, PlusIcon, RefreshCwIcon, Trash2Icon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import {
  ApiError,
  KEY_MASK,
  migration,
  shadow,
  type FollowedRadarr,
  type FollowedRadarrRequest,
  type FollowedRadarrTest,
  type RadarrImportPreview,
} from '@/api/client'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
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
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { mappingComplete, ProfileMappingTable, RootFolderMappingTable, slugPattern, suggestedMapping, Toggle } from '@/features/settings/RadarrMapping'
import { formatDateTime } from '@/i18n'

const okStyle = 'border-emerald-600/30 bg-emerald-500/15 text-emerald-700 dark:border-emerald-400/30 dark:text-emerald-300'
const offStyle = 'border-border bg-muted text-muted-foreground'
const failingStyle = 'border-red-600/30 bg-red-500/15 text-red-700 dark:border-red-400/30 dark:text-red-300'

const emptyForm = (): FollowedRadarrRequest => ({
  name: '',
  url: '',
  apiKey: '',
  versionName: '1080p',
  profileMap: {},
  rootFolderMap: {},
  importTags: true,
  keepMonitored: true,
  includeMoviesWithoutFile: true,
  enabled: true,
})

const toForm = (radarr: FollowedRadarr): FollowedRadarrRequest => ({
  name: radarr.name,
  url: radarr.url,
  apiKey: radarr.apiKey,
  versionName: radarr.versionName,
  profileMap: { ...radarr.profileMap },
  rootFolderMap: { ...radarr.rootFolderMap },
  importTags: radarr.importTags,
  keepMonitored: radarr.keepMonitored,
  includeMoviesWithoutFile: radarr.includeMoviesWithoutFile,
  enabled: radarr.enabled,
})

/**
 * Settings → Followed Radarrs: the Radarrs Bigarr runs next to in shadow mode. Each one maps to a version; Bigarr reads
 * its history (its grabs) and follows its library, and Activity → Compare puts its grabs next to Bigarr's picks.
 */
export function RadarrFollowPage() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const status = useQuery({ queryKey: ['shadow'], queryFn: shadow.status, refetchInterval: 60_000 })
  const [editing, setEditing] = useState<FollowedRadarr | 'new' | null>(null)
  const [pendingRemove, setPendingRemove] = useState<FollowedRadarr | null>(null)
  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['shadow'] })
  const fail = (e: unknown, fallback: string) => toast.error(e instanceof ApiError ? e.message : fallback)

  const sync = useMutation({
    mutationFn: (id: number) => shadow.radarrs.sync(id),
    onSuccess: async (pass) => {
      toast.success(t('radarrFollow.synced', { name: pass.name, summary: pass.summary }))
      await invalidate()
      await queryClient.invalidateQueries({ queryKey: ['compare'] })
    },
    onError: async (e) => {
      fail(e, t('radarrFollow.syncFailed'))
      await invalidate()
    },
  })
  const test = useMutation({
    mutationFn: (id: number) => shadow.radarrs.testSaved(id),
    onSuccess: (result) => toast.success(t('radarrFollow.testOk', { version: result.version ?? '?', movies: result.movies })),
    onError: (e) => fail(e, t('radarrFollow.testFailed')),
  })
  const remove = useMutation({
    mutationFn: (id: number) => shadow.radarrs.remove(id),
    onSuccess: async () => {
      setPendingRemove(null)
      await invalidate()
    },
    onError: (e) => {
      setPendingRemove(null)
      fail(e, t('radarrFollow.removeFailed'))
    },
  })

  const s = status.data
  const radarrs = s?.radarrs ?? []

  return (
    <>
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="text-2xl font-semibold tracking-tight">{t('radarrFollow.title')}</h1>
        <Button type="button" size="sm" className="ml-auto" onClick={() => setEditing('new')} data-testid="add-followed-radarr">
          <PlusIcon />
          {t('radarrFollow.add')}
        </Button>
      </div>
      <p className="max-w-3xl text-sm text-muted-foreground">
        {t('radarrFollow.intro')}{' '}
        <Link to="/activity/compare" className="underline underline-offset-4">
          {t('nav.compare')}
        </Link>
      </p>

      {s && (
        <Alert className="max-w-3xl" data-testid="shadow-mode-status">
          <AlertTitle>{s.enabled ? t('radarrFollow.shadowOn') : t('radarrFollow.shadowOff')}</AlertTitle>
          <AlertDescription>
            {s.enabled ? t('radarrFollow.shadowOnHint', { picks: s.picks, grabs: s.radarrGrabs }) : t('radarrFollow.shadowOffHint')}
          </AlertDescription>
        </Alert>
      )}

      <div className="overflow-x-auto rounded-lg border">
        <Table data-testid="followed-radarrs-table">
          <TableHeader>
            <TableRow>
              <TableHead>{t('radarrFollow.name')}</TableHead>
              <TableHead>{t('radarrFollow.version')}</TableHead>
              <TableHead>{t('radarrFollow.state')}</TableHead>
              <TableHead>{t('radarrFollow.lastPass')}</TableHead>
              <TableHead className="text-right">{t('radarrFollow.grabs')}</TableHead>
              <TableHead className="text-right">{t('versions.actions')}</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {radarrs.map((radarr) => {
              const failing = radarr.lastErrorAt !== null && (radarr.lastSyncAt === null || radarr.lastErrorAt > radarr.lastSyncAt)
              return (
                <TableRow key={radarr.id} data-testid={`followed-radarr-${radarr.id}`}>
                  <TableCell className="align-top">
                    <span className="font-medium">{radarr.name}</span>
                    <span className="block text-xs text-muted-foreground break-all">{radarr.url}</span>
                  </TableCell>
                  <TableCell className="align-top">
                    <Badge variant="secondary">{radarr.versionName}</Badge>
                  </TableCell>
                  <TableCell className="align-top">
                    <span className="flex flex-wrap gap-1">
                      <Badge variant="outline" className={radarr.enabled ? okStyle : offStyle}>
                        {radarr.enabled ? t('radarrFollow.enabled') : t('radarrFollow.disabled')}
                      </Badge>
                      {failing && (
                        <Badge variant="outline" className={failingStyle}>
                          {t('radarrFollow.failing')}
                        </Badge>
                      )}
                    </span>
                  </TableCell>
                  <TableCell className="max-w-md align-top whitespace-normal">
                    <span className="text-sm">{radarr.lastSyncAt ? formatDateTime(radarr.lastSyncAt) : t('radarrFollow.never')}</span>
                    {failing ? (
                      <span className="block text-xs text-destructive">{radarr.lastError}</span>
                    ) : (
                      radarr.lastSummary && <span className="block text-xs text-muted-foreground">{radarr.lastSummary}</span>
                    )}
                    <span className="block text-xs text-muted-foreground">{t('radarrFollow.checkpoint', { date: formatDateTime(radarr.historyCheckpoint) })}</span>
                    <span className="block text-xs text-muted-foreground" data-testid="followed-radarr-webhook">
                      {radarr.lastWebhookAt
                        ? t('radarrFollow.webhookLast', { event: radarr.lastWebhookEvent ?? '?', date: formatDateTime(radarr.lastWebhookAt) })
                        : t('radarrFollow.webhookNever')}
                    </span>
                  </TableCell>
                  <TableCell className="align-top text-right tabular-nums">
                    {radarr.grabs}
                    {radarr.inFlight > 0 && <span className="block text-xs text-muted-foreground">{t('radarrFollow.inFlight', { count: radarr.inFlight })}</span>}
                  </TableCell>
                  <TableCell className="align-top">
                    <div className="flex items-center justify-end gap-1">
                      <Tooltip>
                        <TooltipTrigger asChild>
                          <Button type="button" variant="ghost" size="icon" onClick={() => sync.mutate(radarr.id)} disabled={sync.isPending} aria-label={t('radarrFollow.sync')}>
                            <RefreshCwIcon className={sync.isPending && sync.variables === radarr.id ? 'animate-spin' : undefined} />
                          </Button>
                        </TooltipTrigger>
                        <TooltipContent>{t('radarrFollow.sync')}</TooltipContent>
                      </Tooltip>
                      <Tooltip>
                        <TooltipTrigger asChild>
                          <Button type="button" variant="ghost" size="icon" onClick={() => test.mutate(radarr.id)} disabled={test.isPending} aria-label={t('radarrFollow.test')}>
                            <FlaskConicalIcon className={test.isPending && test.variables === radarr.id ? 'animate-pulse' : undefined} />
                          </Button>
                        </TooltipTrigger>
                        <TooltipContent>{t('radarrFollow.test')}</TooltipContent>
                      </Tooltip>
                      <Tooltip>
                        <TooltipTrigger asChild>
                          <Button type="button" variant="ghost" size="icon" onClick={() => setEditing(radarr)} aria-label={t('radarrFollow.edit')}>
                            <PencilIcon />
                          </Button>
                        </TooltipTrigger>
                        <TooltipContent>{t('radarrFollow.edit')}</TooltipContent>
                      </Tooltip>
                      <Tooltip>
                        <TooltipTrigger asChild>
                          <Button
                            type="button"
                            variant="ghost"
                            size="icon"
                            className="text-destructive hover:text-destructive"
                            onClick={() => setPendingRemove(radarr)}
                            aria-label={t('radarrFollow.delete')}
                          >
                            <Trash2Icon />
                          </Button>
                        </TooltipTrigger>
                        <TooltipContent>{t('radarrFollow.delete')}</TooltipContent>
                      </Tooltip>
                    </div>
                  </TableCell>
                </TableRow>
              )
            })}
            {s && radarrs.length === 0 && (
              <TableRow>
                <TableCell colSpan={6} className="whitespace-normal text-muted-foreground">
                  {t('radarrFollow.empty')}{' '}
                  <Link to="/settings/import-radarr" className="underline underline-offset-4">
                    {t('nav.settingsImportRadarr')}
                  </Link>
                </TableCell>
              </TableRow>
            )}
          </TableBody>
        </Table>
      </div>
      <p className="max-w-3xl text-xs text-muted-foreground">{t('radarrFollow.readOnlyHint')}</p>

      {editing !== null && (
        <FollowedRadarrDialog key={editing === 'new' ? 'new' : editing.id} radarr={editing === 'new' ? null : editing} onClose={() => setEditing(null)} onSaved={() => void invalidate()} />
      )}

      <AlertDialog open={pendingRemove !== null} onOpenChange={(open) => !open && setPendingRemove(null)}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>{t('radarrFollow.deleteTitle')}</AlertDialogTitle>
            <AlertDialogDescription>{t('radarrFollow.confirmDelete', { name: pendingRemove?.name ?? '' })}</AlertDialogDescription>
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
              {t('radarrFollow.delete')}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </>
  )
}

function FollowedRadarrDialog({ radarr, onClose, onSaved }: { radarr: FollowedRadarr | null; onClose: () => void; onSaved: () => void }) {
  const { t } = useTranslation()
  const [form, setForm] = useState<FollowedRadarrRequest>(radarr ? toForm(radarr) : emptyForm())
  const [preview, setPreview] = useState<RadarrImportPreview | null>(null)
  const set = <K extends keyof FollowedRadarrRequest>(key: K, value: FollowedRadarrRequest[K]) => setForm((f) => ({ ...f, [key]: value }))

  const keyTyped = (form.apiKey ?? '').trim().length > 0 && form.apiKey !== KEY_MASK
  const payload = (): FollowedRadarrRequest => ({ ...form, name: form.name.trim(), url: form.url.trim(), apiKey: keyTyped ? (form.apiKey ?? '').trim() : KEY_MASK, versionName: form.versionName.trim() })

  // A saved Radarr whose URL and key are untouched is read with its stored key: the key never comes back to the browser.
  const load = useMutation({
    mutationFn: () =>
      radarr && !keyTyped && form.url.trim() === radarr.url ? shadow.radarrs.preview(radarr.id) : migration.preview({ url: form.url.trim(), apiKey: (form.apiKey ?? '').trim() }),
    onSuccess: (result) => {
      setPreview(result)
      // A new form takes the suggestions; an edited one keeps its mapping and only fills the gaps.
      const suggested = suggestedMapping(result)
      setForm((f) => ({
        ...f,
        name: f.name.trim() ? f.name : `Radarr ${result.suggestedVersionName}`,
        versionName: radarr ? f.versionName : result.suggestedVersionName,
        profileMap: { ...suggested.profileMap, ...f.profileMap },
        rootFolderMap: { ...suggested.rootFolderMap, ...f.rootFolderMap },
      }))
    },
  })
  // An existing Radarr loads its mapping data once, when the dialog opens.
  // biome-ignore lint/correctness/useExhaustiveDependencies: runs once, when the dialog opens
  useEffect(() => {
    if (radarr) load.mutate()
  }, [])

  const save = useMutation({
    mutationFn: () => (radarr ? shadow.radarrs.update(radarr.id, payload()) : shadow.radarrs.add(payload())),
    onSuccess: () => {
      onSaved()
      onClose()
    },
  })
  const test = useMutation({
    mutationFn: () => (radarr ? shadow.radarrs.testSaved(radarr.id, payload()) : shadow.radarrs.test(payload())),
    onSuccess: (result: FollowedRadarrTest) => toast.success(t('radarrFollow.testOk', { version: result.version ?? '?', movies: result.movies })),
    onError: (e) => toast.error(e instanceof ApiError ? e.message : t('radarrFollow.testFailed')),
  })

  const versionValid = slugPattern.test(form.versionName.trim())
  const mapped = useMemo(() => (preview ? mappingComplete(preview, form) : Object.keys(form.profileMap).length > 0 && Object.keys(form.rootFolderMap).length > 0), [preview, form])
  const connectable = form.url.trim().length > 0 && (keyTyped || radarr !== null)
  const canSubmit = form.name.trim().length > 0 && connectable && versionValid && mapped

  const submit = (e: FormEvent) => {
    e.preventDefault()
    save.mutate()
  }

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="max-h-[90vh] overflow-y-auto sm:max-w-3xl">
        <form onSubmit={submit} data-testid="followed-radarr-form">
          <DialogHeader>
            <DialogTitle>{radarr ? t('radarrFollow.editTitle', { name: radarr.name }) : t('radarrFollow.new')}</DialogTitle>
            <DialogDescription>{t('radarrFollow.dialogHint')}</DialogDescription>
          </DialogHeader>

          <div className="my-6 grid gap-4 sm:grid-cols-2">
            <div className="grid grid-cols-1 content-start gap-2">
              <Label htmlFor="fr-name">{t('radarrFollow.name')}</Label>
              <Input id="fr-name" value={form.name} onChange={(e) => set('name', e.target.value)} placeholder="Radarr 4k" autoFocus />
            </div>
            <div className="grid grid-cols-1 content-start gap-2">
              <Label htmlFor="fr-version">{t('radarrFollow.version')}</Label>
              <Input id="fr-version" value={form.versionName} onChange={(e) => set('versionName', e.target.value)} aria-invalid={!versionValid} />
              <p className="text-xs text-muted-foreground">{versionValid ? t('radarrFollow.versionHint') : t('importRadarr.versionNameInvalid')}</p>
            </div>
            <div className="grid grid-cols-1 gap-2 sm:col-span-2">
              <Label htmlFor="fr-url">{t('importRadarr.url')}</Label>
              <Input id="fr-url" value={form.url} onChange={(e) => set('url', e.target.value)} placeholder="http://radarr:7878" />
            </div>
            <div className="grid grid-cols-1 gap-2 sm:col-span-2">
              <Label htmlFor="fr-key">{t('importRadarr.apiKey')}</Label>
              <Input
                id="fr-key"
                type="password"
                value={form.apiKey ?? ''}
                onFocus={() => form.apiKey === KEY_MASK && set('apiKey', '')}
                onChange={(e) => set('apiKey', e.target.value)}
                placeholder={radarr ? t('radarrFollow.keyKept') : undefined}
                autoComplete="new-password"
              />
              <p className="text-xs text-muted-foreground">{radarr ? t('radarrFollow.keyHintSaved') : t('importRadarr.apiKeyHint')}</p>
            </div>

            <div className="flex flex-col gap-3 sm:col-span-2">
              <div className="flex flex-wrap items-center gap-3">
                <h3 className="font-medium">{t('radarrFollow.mapping')}</h3>
                <Button type="button" variant="outline" size="sm" onClick={() => load.mutate()} disabled={load.isPending || !connectable} data-testid="followed-radarr-load">
                  <RefreshCwIcon className={load.isPending ? 'animate-spin' : undefined} />
                  {preview ? t('radarrFollow.reload') : t('radarrFollow.load')}
                </Button>
              </div>
              {load.error && (
                <Alert variant="destructive">
                  <AlertDescription>{load.error instanceof ApiError ? load.error.message : t('importRadarr.previewFailed')}</AlertDescription>
                </Alert>
              )}
              {preview ? (
                <>
                  <ProfileMappingTable preview={preview} value={form.profileMap} onChange={(value) => set('profileMap', value)} />
                  <RootFolderMappingTable preview={preview} value={form.rootFolderMap} onChange={(value) => set('rootFolderMap', value)} />
                  {!mapped && <p className="text-xs text-muted-foreground">{t('importRadarr.mappingIncomplete')}</p>}
                </>
              ) : (
                <p className="text-xs text-muted-foreground">{t('radarrFollow.mappingHint')}</p>
              )}
            </div>

            <WebhookHelp radarr={radarr} />

            <div className="flex flex-col gap-3 sm:col-span-2">
              <Toggle id="fr-tags" label={t('importRadarr.importTags')} checked={form.importTags} onChange={(value) => set('importTags', value)} />
              <Toggle id="fr-monitored" label={t('importRadarr.keepMonitored')} checked={form.keepMonitored} onChange={(value) => set('keepMonitored', value)} />
              <Toggle id="fr-without-file" label={t('importRadarr.includeWithoutFile')} checked={form.includeMoviesWithoutFile} onChange={(value) => set('includeMoviesWithoutFile', value)} />
              <Toggle id="fr-enabled" label={t('radarrFollow.enabledToggle')} hint={t('radarrFollow.enabledHint')} checked={form.enabled} onChange={(value) => set('enabled', value)} />
            </div>
          </div>

          {save.error && (
            <Alert variant="destructive" className="mb-4">
              <AlertDescription>{save.error instanceof ApiError ? save.error.message : t('common.unexpectedError')}</AlertDescription>
            </Alert>
          )}

          <DialogFooter className="items-center sm:justify-between">
            <Button type="button" variant="outline" onClick={() => test.mutate()} disabled={test.isPending || !connectable} data-testid="followed-radarr-test">
              <FlaskConicalIcon className={test.isPending ? 'animate-pulse' : undefined} />
              {test.isPending ? t('radarrFollow.testing') : t('radarrFollow.test')}
            </Button>
            <div className="flex gap-2">
              <Button type="button" variant="ghost" onClick={onClose}>
                {t('common.cancel')}
              </Button>
              <Button type="submit" disabled={save.isPending || !canSubmit} data-testid="followed-radarr-save">
                {t('radarrFollow.save')}
              </Button>
            </div>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}

/**
 * What to paste in Radarr → Settings → Connect → Webhook so a pass runs within seconds of an event instead of at the next
 * scheduled one. The address is Bigarr's as Radarr reaches it; the key is any Bigarr API key (Settings → Connect).
 */
function WebhookHelp({ radarr }: { radarr: FollowedRadarr | null }) {
  const { t } = useTranslation()
  const url = radarr ? `${window.location.origin}${shadow.radarrs.webhookPath(radarr.id)}?apikey=YOUR_API_KEY` : null
  return (
    <div className="flex flex-col gap-2 sm:col-span-2" data-testid="followed-radarr-webhook-help">
      <h3 className="font-medium">{t('radarrFollow.webhookTitle')}</h3>
      {url ? (
        <>
          <div className="flex items-center gap-2">
            <code className="min-w-0 flex-1 rounded bg-muted px-2 py-1.5 text-xs break-all">{url}</code>
            <Button
              type="button"
              variant="outline"
              size="icon"
              aria-label={t('radarrFollow.webhookCopy')}
              onClick={() => {
                void navigator.clipboard?.writeText(url).then(() => toast.success(t('radarrFollow.webhookCopied')))
              }}
            >
              <CopyIcon />
            </Button>
          </div>
          <p className="text-xs text-muted-foreground">
            {t('radarrFollow.webhookHint')}{' '}
            <Link to="/settings/connect" className="underline underline-offset-4">
              {t('nav.settingsConnect')}
            </Link>
          </p>
        </>
      ) : (
        <p className="text-xs text-muted-foreground">{t('radarrFollow.webhookAfterSave')}</p>
      )}
    </div>
  )
}
