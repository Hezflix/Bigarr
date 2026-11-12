import { useEffect, useMemo, useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { CopyIcon, FlaskConicalIcon, PencilIcon, PlusIcon, RefreshCwIcon, Trash2Icon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import {
  ApiError,
  KEY_MASK,
  shadow,
  sonarrMigration,
  type FollowedSonarr,
  type FollowedSonarrRequest,
  type FollowedSonarrTest,
  type SonarrImportPreview,
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
import { useSonarrLabels } from '@/features/settings/ImportSonarrPage'
import { mappingComplete, ProfileMappingTable, RootFolderMappingTable, slugPattern, sonarrMappingPreview, suggestedMapping, Toggle } from '@/features/settings/RadarrMapping'
import { formatDateTime } from '@/i18n'

const okStyle = 'border-emerald-600/30 bg-emerald-500/15 text-emerald-700 dark:border-emerald-400/30 dark:text-emerald-300'
const offStyle = 'border-border bg-muted text-muted-foreground'
const failingStyle = 'border-red-600/30 bg-red-500/15 text-red-700 dark:border-red-400/30 dark:text-red-300'

const emptyForm = (): FollowedSonarrRequest => ({
  name: '',
  url: '',
  apiKey: '',
  versionName: '1080p',
  profileMap: {},
  rootFolderMap: {},
  importTags: true,
  keepMonitored: true,
  enabled: true,
})

const toForm = (sonarr: FollowedSonarr): FollowedSonarrRequest => ({
  name: sonarr.name,
  url: sonarr.url,
  apiKey: sonarr.apiKey,
  versionName: sonarr.versionName,
  profileMap: { ...sonarr.profileMap },
  rootFolderMap: { ...sonarr.rootFolderMap },
  importTags: sonarr.importTags,
  keepMonitored: sonarr.keepMonitored,
  enabled: sonarr.enabled,
})

/**
 * Settings → Followed Sonarrs: the Sonarrs Bigarr runs next to in shadow mode. Each one maps to a version; Bigarr reads
 * its history (its grabs) and follows its library, and Activity → Compare puts its grabs next to Bigarr's picks.
 */
export function SonarrFollowPage() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const status = useQuery({ queryKey: ['shadow'], queryFn: shadow.status, refetchInterval: 60_000 })
  const [editing, setEditing] = useState<FollowedSonarr | 'new' | null>(null)
  const [pendingRemove, setPendingRemove] = useState<FollowedSonarr | null>(null)
  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['shadow'] })
  const fail = (e: unknown, fallback: string) => toast.error(e instanceof ApiError ? e.message : fallback)

  const sync = useMutation({
    mutationFn: (id: number) => shadow.sonarrs.sync(id),
    onSuccess: async (pass) => {
      toast.success(t('sonarrFollow.synced', { name: pass.name, summary: pass.summary }))
      await invalidate()
      await queryClient.invalidateQueries({ queryKey: ['compareSeries'] })
    },
    onError: async (e) => {
      fail(e, t('sonarrFollow.syncFailed'))
      await invalidate()
    },
  })
  const test = useMutation({
    mutationFn: (id: number) => shadow.sonarrs.testSaved(id),
    onSuccess: (result) => toast.success(t('sonarrFollow.testOk', { version: result.version ?? '?', series: result.series })),
    onError: (e) => fail(e, t('sonarrFollow.testFailed')),
  })
  const remove = useMutation({
    mutationFn: (id: number) => shadow.sonarrs.remove(id),
    onSuccess: async () => {
      setPendingRemove(null)
      await invalidate()
    },
    onError: (e) => {
      setPendingRemove(null)
      fail(e, t('sonarrFollow.removeFailed'))
    },
  })

  const s = status.data
  const sonarrs = s?.sonarrs ?? []

  return (
    <>
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="text-2xl font-semibold tracking-tight">{t('sonarrFollow.title')}</h1>
        <Button type="button" size="sm" className="ml-auto" onClick={() => setEditing('new')} data-testid="add-followed-sonarr">
          <PlusIcon />
          {t('sonarrFollow.add')}
        </Button>
      </div>
      <p className="max-w-3xl text-sm text-muted-foreground">
        {t('sonarrFollow.intro')}{' '}
        <Link to="/activity/compare-sonarr" className="underline underline-offset-4">
          {t('nav.compareSonarr')}
        </Link>
      </p>

      {s && (
        <Alert className="max-w-3xl" data-testid="shadow-mode-status">
          <AlertTitle>{s.enabled ? t('sonarrFollow.shadowOn') : t('sonarrFollow.shadowOff')}</AlertTitle>
          <AlertDescription>
            {s.enabled ? t('sonarrFollow.shadowOnHint', { picks: s.seriesPicks, grabs: s.sonarrGrabs }) : t('sonarrFollow.shadowOffHint')}
          </AlertDescription>
        </Alert>
      )}

      <div className="overflow-x-auto rounded-lg border">
        <Table data-testid="followed-sonarrs-table">
          <TableHeader>
            <TableRow>
              <TableHead>{t('sonarrFollow.name')}</TableHead>
              <TableHead>{t('sonarrFollow.version')}</TableHead>
              <TableHead>{t('sonarrFollow.state')}</TableHead>
              <TableHead>{t('sonarrFollow.lastPass')}</TableHead>
              <TableHead className="text-right">{t('sonarrFollow.grabs')}</TableHead>
              <TableHead className="text-right">{t('versions.actions')}</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {sonarrs.map((sonarr) => {
              const failing = sonarr.lastErrorAt !== null && (sonarr.lastSyncAt === null || sonarr.lastErrorAt > sonarr.lastSyncAt)
              return (
                <TableRow key={sonarr.id} data-testid={`followed-sonarr-${sonarr.id}`}>
                  <TableCell className="align-top">
                    <span className="font-medium">{sonarr.name}</span>
                    <span className="block text-xs text-muted-foreground break-all">{sonarr.url}</span>
                  </TableCell>
                  <TableCell className="align-top">
                    <Badge variant="secondary">{sonarr.versionName}</Badge>
                  </TableCell>
                  <TableCell className="align-top">
                    <span className="flex flex-wrap gap-1">
                      <Badge variant="outline" className={sonarr.enabled ? okStyle : offStyle}>
                        {sonarr.enabled ? t('sonarrFollow.enabled') : t('sonarrFollow.disabled')}
                      </Badge>
                      {failing && (
                        <Badge variant="outline" className={failingStyle}>
                          {t('sonarrFollow.failing')}
                        </Badge>
                      )}
                    </span>
                  </TableCell>
                  <TableCell className="max-w-md align-top whitespace-normal">
                    <span className="text-sm">{sonarr.lastSyncAt ? formatDateTime(sonarr.lastSyncAt) : t('sonarrFollow.never')}</span>
                    {failing ? (
                      <span className="block text-xs text-destructive">{sonarr.lastError}</span>
                    ) : (
                      sonarr.lastSummary && <span className="block text-xs text-muted-foreground">{sonarr.lastSummary}</span>
                    )}
                    <span className="block text-xs text-muted-foreground">{t('sonarrFollow.checkpoint', { date: formatDateTime(sonarr.historyCheckpoint) })}</span>
                    <span className="block text-xs text-muted-foreground" data-testid="followed-sonarr-webhook">
                      {sonarr.lastWebhookAt
                        ? t('sonarrFollow.webhookLast', { event: sonarr.lastWebhookEvent ?? '?', date: formatDateTime(sonarr.lastWebhookAt) })
                        : t('sonarrFollow.webhookNever')}
                    </span>
                  </TableCell>
                  <TableCell className="align-top text-right tabular-nums">
                    {sonarr.grabs}
                    {sonarr.inFlight > 0 && <span className="block text-xs text-muted-foreground">{t('sonarrFollow.inFlight', { count: sonarr.inFlight })}</span>}
                  </TableCell>
                  <TableCell className="align-top">
                    <div className="flex items-center justify-end gap-1">
                      <Tooltip>
                        <TooltipTrigger asChild>
                          <Button type="button" variant="ghost" size="icon" onClick={() => sync.mutate(sonarr.id)} disabled={sync.isPending} aria-label={t('sonarrFollow.sync')}>
                            <RefreshCwIcon className={sync.isPending && sync.variables === sonarr.id ? 'animate-spin' : undefined} />
                          </Button>
                        </TooltipTrigger>
                        <TooltipContent>{t('sonarrFollow.sync')}</TooltipContent>
                      </Tooltip>
                      <Tooltip>
                        <TooltipTrigger asChild>
                          <Button type="button" variant="ghost" size="icon" onClick={() => test.mutate(sonarr.id)} disabled={test.isPending} aria-label={t('sonarrFollow.test')}>
                            <FlaskConicalIcon className={test.isPending && test.variables === sonarr.id ? 'animate-pulse' : undefined} />
                          </Button>
                        </TooltipTrigger>
                        <TooltipContent>{t('sonarrFollow.test')}</TooltipContent>
                      </Tooltip>
                      <Tooltip>
                        <TooltipTrigger asChild>
                          <Button type="button" variant="ghost" size="icon" onClick={() => setEditing(sonarr)} aria-label={t('sonarrFollow.edit')}>
                            <PencilIcon />
                          </Button>
                        </TooltipTrigger>
                        <TooltipContent>{t('sonarrFollow.edit')}</TooltipContent>
                      </Tooltip>
                      <Tooltip>
                        <TooltipTrigger asChild>
                          <Button
                            type="button"
                            variant="ghost"
                            size="icon"
                            className="text-destructive hover:text-destructive"
                            onClick={() => setPendingRemove(sonarr)}
                            aria-label={t('sonarrFollow.delete')}
                          >
                            <Trash2Icon />
                          </Button>
                        </TooltipTrigger>
                        <TooltipContent>{t('sonarrFollow.delete')}</TooltipContent>
                      </Tooltip>
                    </div>
                  </TableCell>
                </TableRow>
              )
            })}
            {s && sonarrs.length === 0 && (
              <TableRow>
                <TableCell colSpan={6} className="whitespace-normal text-muted-foreground">
                  {t('sonarrFollow.empty')}{' '}
                  <Link to="/settings/import-sonarr" className="underline underline-offset-4">
                    {t('nav.settingsImportSonarr')}
                  </Link>
                </TableCell>
              </TableRow>
            )}
          </TableBody>
        </Table>
      </div>
      <p className="max-w-3xl text-xs text-muted-foreground">{t('sonarrFollow.readOnlyHint')}</p>

      {editing !== null && (
        <FollowedSonarrDialog key={editing === 'new' ? 'new' : editing.id} sonarr={editing === 'new' ? null : editing} onClose={() => setEditing(null)} onSaved={() => void invalidate()} />
      )}

      <AlertDialog open={pendingRemove !== null} onOpenChange={(open) => !open && setPendingRemove(null)}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>{t('sonarrFollow.deleteTitle')}</AlertDialogTitle>
            <AlertDialogDescription>{t('sonarrFollow.confirmDelete', { name: pendingRemove?.name ?? '' })}</AlertDialogDescription>
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
              {t('sonarrFollow.delete')}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </>
  )
}

function FollowedSonarrDialog({ sonarr, onClose, onSaved }: { sonarr: FollowedSonarr | null; onClose: () => void; onSaved: () => void }) {
  const { t } = useTranslation()
  const labels = useSonarrLabels()
  const [form, setForm] = useState<FollowedSonarrRequest>(sonarr ? toForm(sonarr) : emptyForm())
  const [preview, setPreview] = useState<SonarrImportPreview | null>(null)
  const set = <K extends keyof FollowedSonarrRequest>(key: K, value: FollowedSonarrRequest[K]) => setForm((f) => ({ ...f, [key]: value }))

  const keyTyped = (form.apiKey ?? '').trim().length > 0 && form.apiKey !== KEY_MASK
  const payload = (): FollowedSonarrRequest => ({ ...form, name: form.name.trim(), url: form.url.trim(), apiKey: keyTyped ? (form.apiKey ?? '').trim() : KEY_MASK, versionName: form.versionName.trim() })

  // A saved Sonarr whose URL and key are untouched is read with its stored key: the key never comes back to the browser.
  const load = useMutation({
    mutationFn: () =>
      sonarr && !keyTyped && form.url.trim() === sonarr.url ? shadow.sonarrs.preview(sonarr.id) : sonarrMigration.preview({ url: form.url.trim(), apiKey: (form.apiKey ?? '').trim() }),
    onSuccess: (result) => {
      setPreview(result)
      // A new form takes the suggestions; an edited one keeps its mapping and only fills the gaps.
      const suggested = suggestedMapping(sonarrMappingPreview(result))
      // The follow maps onto existing profiles only: "create" (0) is the import's.
      suggested.profileMap = Object.fromEntries(Object.entries(suggested.profileMap).filter(([, id]) => id !== 0))
      setForm((f) => ({
        ...f,
        name: f.name.trim() ? f.name : `Sonarr ${result.suggestedVersionName}`,
        versionName: sonarr ? f.versionName : result.suggestedVersionName,
        profileMap: { ...suggested.profileMap, ...f.profileMap },
        rootFolderMap: { ...suggested.rootFolderMap, ...f.rootFolderMap },
      }))
    },
  })
  // An existing Sonarr loads its mapping data once, when the dialog opens.
  // biome-ignore lint/correctness/useExhaustiveDependencies: runs once, when the dialog opens
  useEffect(() => {
    if (sonarr) load.mutate()
  }, [])

  const save = useMutation({
    mutationFn: () => (sonarr ? shadow.sonarrs.update(sonarr.id, payload()) : shadow.sonarrs.add(payload())),
    onSuccess: () => {
      onSaved()
      onClose()
    },
  })
  const test = useMutation({
    mutationFn: () => (sonarr ? shadow.sonarrs.testSaved(sonarr.id, payload()) : shadow.sonarrs.test(payload())),
    onSuccess: (result: FollowedSonarrTest) => toast.success(t('sonarrFollow.testOk', { version: result.version ?? '?', series: result.series })),
    onError: (e) => toast.error(e instanceof ApiError ? e.message : t('sonarrFollow.testFailed')),
  })

  const versionValid = slugPattern.test(form.versionName.trim())
  const mapped = useMemo(() => (preview ? mappingComplete(sonarrMappingPreview(preview), form) : Object.keys(form.profileMap).length > 0 && Object.keys(form.rootFolderMap).length > 0), [preview, form])
  const connectable = form.url.trim().length > 0 && (keyTyped || sonarr !== null)
  const canSubmit = form.name.trim().length > 0 && connectable && versionValid && mapped

  const submit = (e: FormEvent) => {
    e.preventDefault()
    save.mutate()
  }

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="max-h-[90vh] overflow-y-auto sm:max-w-3xl">
        <form onSubmit={submit} data-testid="followed-sonarr-form">
          <DialogHeader>
            <DialogTitle>{sonarr ? t('sonarrFollow.editTitle', { name: sonarr.name }) : t('sonarrFollow.new')}</DialogTitle>
            <DialogDescription>{t('sonarrFollow.dialogHint')}</DialogDescription>
          </DialogHeader>

          <div className="my-6 grid gap-4 sm:grid-cols-2">
            <div className="grid grid-cols-1 content-start gap-2">
              <Label htmlFor="fs-name">{t('sonarrFollow.name')}</Label>
              <Input id="fs-name" value={form.name} onChange={(e) => set('name', e.target.value)} placeholder="Sonarr 4k" autoFocus />
            </div>
            <div className="grid grid-cols-1 content-start gap-2">
              <Label htmlFor="fs-version">{t('sonarrFollow.version')}</Label>
              <Input id="fs-version" value={form.versionName} onChange={(e) => set('versionName', e.target.value)} aria-invalid={!versionValid} />
              <p className="text-xs text-muted-foreground">{versionValid ? t('sonarrFollow.versionHint') : t('importRadarr.versionNameInvalid')}</p>
            </div>
            <div className="grid grid-cols-1 gap-2 sm:col-span-2">
              <Label htmlFor="fs-url">{t('importSonarr.url')}</Label>
              <Input id="fs-url" value={form.url} onChange={(e) => set('url', e.target.value)} placeholder="http://sonarr:8989" />
            </div>
            <div className="grid grid-cols-1 gap-2 sm:col-span-2">
              <Label htmlFor="fs-key">{t('importRadarr.apiKey')}</Label>
              <Input
                id="fs-key"
                type="password"
                value={form.apiKey ?? ''}
                onFocus={() => form.apiKey === KEY_MASK && set('apiKey', '')}
                onChange={(e) => set('apiKey', e.target.value)}
                placeholder={sonarr ? t('sonarrFollow.keyKept') : undefined}
                autoComplete="new-password"
              />
              <p className="text-xs text-muted-foreground">{sonarr ? t('sonarrFollow.keyHintSaved') : t('importSonarr.apiKeyHint')}</p>
            </div>

            <div className="flex flex-col gap-3 sm:col-span-2">
              <div className="flex flex-wrap items-center gap-3">
                <h3 className="font-medium">{t('sonarrFollow.mapping')}</h3>
                <Button type="button" variant="outline" size="sm" onClick={() => load.mutate()} disabled={load.isPending || !connectable} data-testid="followed-sonarr-load">
                  <RefreshCwIcon className={load.isPending ? 'animate-spin' : undefined} />
                  {preview ? t('sonarrFollow.reload') : t('sonarrFollow.load')}
                </Button>
              </div>
              {load.error && (
                <Alert variant="destructive">
                  <AlertDescription>{load.error instanceof ApiError ? load.error.message : t('importSonarr.previewFailed')}</AlertDescription>
                </Alert>
              )}
              {preview ? (
                <>
                  <ProfileMappingTable preview={sonarrMappingPreview(preview)} labels={labels} kind="series" value={form.profileMap} onChange={(value) => set('profileMap', value)} />
                  <RootFolderMappingTable preview={sonarrMappingPreview(preview)} labels={labels} kind="series" value={form.rootFolderMap} onChange={(value) => set('rootFolderMap', value)} />
                  {!mapped && <p className="text-xs text-muted-foreground">{t('importSonarr.mappingIncomplete')}</p>}
                </>
              ) : (
                <p className="text-xs text-muted-foreground">{t('sonarrFollow.mappingHint')}</p>
              )}
            </div>

            <WebhookHelp sonarr={sonarr} />

            <div className="flex flex-col gap-3 sm:col-span-2">
              <Toggle id="fs-tags" label={t('importRadarr.importTags')} checked={form.importTags} onChange={(value) => set('importTags', value)} />
              <Toggle id="fs-monitored" label={t('importSonarr.keepMonitored')} checked={form.keepMonitored} onChange={(value) => set('keepMonitored', value)} />
              <Toggle id="fs-enabled" label={t('sonarrFollow.enabledToggle')} hint={t('sonarrFollow.enabledHint')} checked={form.enabled} onChange={(value) => set('enabled', value)} />
            </div>
          </div>

          {save.error && (
            <Alert variant="destructive" className="mb-4">
              <AlertDescription>{save.error instanceof ApiError ? save.error.message : t('common.unexpectedError')}</AlertDescription>
            </Alert>
          )}

          <DialogFooter className="items-center sm:justify-between">
            <Button type="button" variant="outline" onClick={() => test.mutate()} disabled={test.isPending || !connectable} data-testid="followed-sonarr-test">
              <FlaskConicalIcon className={test.isPending ? 'animate-pulse' : undefined} />
              {test.isPending ? t('sonarrFollow.testing') : t('sonarrFollow.test')}
            </Button>
            <div className="flex gap-2">
              <Button type="button" variant="ghost" onClick={onClose}>
                {t('common.cancel')}
              </Button>
              <Button type="submit" disabled={save.isPending || !canSubmit} data-testid="followed-sonarr-save">
                {t('sonarrFollow.save')}
              </Button>
            </div>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}

/**
 * What to paste in Sonarr → Settings → Connect → Webhook so a pass runs within seconds of an event instead of at the next
 * scheduled one. The address is Bigarr's as Sonarr reaches it; the key is any Bigarr API key (Settings → Connect).
 */
function WebhookHelp({ sonarr }: { sonarr: FollowedSonarr | null }) {
  const { t } = useTranslation()
  const url = sonarr ? `${window.location.origin}${shadow.sonarrs.webhookPath(sonarr.id)}?apikey=YOUR_API_KEY` : null
  return (
    <div className="flex flex-col gap-2 sm:col-span-2" data-testid="followed-sonarr-webhook-help">
      <h3 className="font-medium">{t('sonarrFollow.webhookTitle')}</h3>
      {url ? (
        <>
          <div className="flex items-center gap-2">
            <code className="min-w-0 flex-1 rounded bg-muted px-2 py-1.5 text-xs break-all">{url}</code>
            <Button
              type="button"
              variant="outline"
              size="icon"
              aria-label={t('sonarrFollow.webhookCopy')}
              onClick={() => {
                void navigator.clipboard?.writeText(url).then(() => toast.success(t('sonarrFollow.webhookCopied')))
              }}
            >
              <CopyIcon />
            </Button>
          </div>
          <p className="text-xs text-muted-foreground">
            {t('sonarrFollow.webhookHint')}{' '}
            <Link to="/settings/connect" className="underline underline-offset-4">
              {t('nav.settingsConnect')}
            </Link>
          </p>
        </>
      ) : (
        <p className="text-xs text-muted-foreground">{t('sonarrFollow.webhookAfterSave')}</p>
      )}
    </div>
  )
}
