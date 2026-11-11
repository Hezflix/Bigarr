import { useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { cn } from 'cn'
import { FlaskConicalIcon, ListIcon, PencilIcon, PlusIcon, RotateCcwIcon, Trash2Icon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import {
  ApiError,
  discordGrabFields,
  discordImportFields,
  discordManualInteractionFields,
  notifications,
  slugify,
  tags as tagsApi,
  type DiscordGrabField,
  type DiscordImportField,
  type DiscordManualInteractionField,
  type NotificationEndpoint,
  type NotificationEndpointRequest,
  type NotificationEventType,
  type WebhookDeliveryState,
  type WebhookFormat,
} from '@/api/client'
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

type ViewKind = 'default' | 'all' | 'version'

type DiscordForm = {
  username: string
  avatar: string
  author: string
  grabFields: DiscordGrabField[]
  importFields: DiscordImportField[]
  manualInteractionFields: DiscordManualInteractionField[]
}

type Form = {
  name: string
  url: string
  secret: string
  enabled: boolean
  events: string[]
  tags: number[]
  format: WebhookFormat
  viewKind: ViewKind
  versionName: string
  discord: DiscordForm
}

/** Radarr's defaults: every field, but no custom formats on imports. */
const defaultDiscord = (): DiscordForm => ({
  username: '',
  avatar: '',
  author: '',
  grabFields: [...discordGrabFields],
  importFields: discordImportFields.filter((f) => f !== 'customFormats' && f !== 'customFormatScore'),
  manualInteractionFields: [...discordManualInteractionFields],
})

const emptyForm = (): Form => ({
  name: '',
  url: '',
  secret: '',
  enabled: true,
  events: [],
  tags: [],
  format: 'bigarr',
  viewKind: 'default',
  versionName: '',
  discord: defaultDiscord(),
})

/** What Discord's "Copy Webhook URL" gives, Slack- and GitHub-compatible suffixes included. */
const discordWebhookUrl = /^https?:\/\/(?:(?:ptb|canary)\.)?discord(?:app)?\.com\/api\/(?:v\d+\/)?webhooks\//i

/** A Discord webhook's token is a secret: the list shows its id only. */
const displayUrl = (endpoint: NotificationEndpoint) => (endpoint.format === 'discord' ? endpoint.url.replace(/(\/webhooks\/\d+\/)[^/?#]+/, '$1••••') : endpoint.url)

/** `version:4k` → kind + name, the same reading as an API key's view. */
function splitView(view: string): { kind: ViewKind; name: string } {
  if (view === 'all') return { kind: 'all', name: '' }
  if (view.startsWith('version:')) return { kind: 'version', name: view.slice('version:'.length) }
  return { kind: 'default', name: '' }
}

const toForm = (endpoint: NotificationEndpoint): Form => {
  const view = splitView(endpoint.view)
  return {
    name: endpoint.name,
    url: endpoint.url,
    secret: '',
    enabled: endpoint.enabled,
    events: endpoint.events,
    tags: endpoint.tags,
    format: endpoint.format,
    viewKind: view.kind,
    versionName: view.name,
    discord: endpoint.discord
      ? {
          username: endpoint.discord.username ?? '',
          avatar: endpoint.discord.avatar ?? '',
          author: endpoint.discord.author ?? '',
          grabFields: endpoint.discord.grabFields ?? defaultDiscord().grabFields,
          importFields: endpoint.discord.importFields ?? defaultDiscord().importFields,
          manualInteractionFields: endpoint.discord.manualInteractionFields ?? defaultDiscord().manualInteractionFields,
        }
      : defaultDiscord(),
  }
}

/** Radarr's and Sonarr's formats only send what they have a word for; Discord's what is worth a message. */
const sentAs = (type: NotificationEventType, format: WebhookFormat) =>
  format === 'bigarr' ||
  (format === 'radarr' ? type.radarrEventType !== null : format === 'sonarr' ? type.sonarrEventType !== null : type.sentToDiscord)

/** The compat formats describe one version per Radarr movie or Sonarr series: they take a view. */
const hasView = (format: WebhookFormat) => format === 'radarr' || format === 'sonarr'

/** Outgoing webhooks (blueprint §3.7, §3.9): where events go, signed, retried until they land. */
export function NotificationsPage() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const list = useQuery({ queryKey: ['notifications'], queryFn: notifications.list, refetchInterval: 30_000 })
  const allTags = useQuery({ queryKey: ['tags'], queryFn: tagsApi.list, staleTime: 60_000 })
  const tagLabel = (id: number) => allTags.data?.find((tag) => tag.id === id)?.label ?? `#${id}`
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
                    <span className="block max-w-[32ch] truncate text-xs text-muted-foreground" title={displayUrl(endpoint)}>
                      {displayUrl(endpoint)}
                    </span>
                    {endpoint.format === 'radarr' && (
                      <Badge variant="outline" className="mt-1" data-testid={`notification-format-${endpoint.id}`}>
                        {t('notifications.radarrBadge', { view: endpoint.view })}
                      </Badge>
                    )}
                    {endpoint.format === 'sonarr' && (
                      <Badge variant="outline" className="mt-1" data-testid={`notification-format-${endpoint.id}`}>
                        {t('notifications.sonarrBadge', { view: endpoint.view })}
                      </Badge>
                    )}
                    {endpoint.format === 'discord' && (
                      <Badge variant="outline" className="mt-1" data-testid={`notification-format-${endpoint.id}`}>
                        {t('notifications.discordBadge')}
                      </Badge>
                    )}
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
                    {endpoint.tags.length > 0 && (
                      <span className="mt-1 flex max-w-[24rem] flex-wrap items-center gap-1 text-xs text-muted-foreground">
                        {t('notifications.onlyTagged')}
                        {endpoint.tags.map((id) => (
                          <Badge key={id} variant="secondary">
                            {tagLabel(id)}
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
  const allTags = useQuery({ queryKey: ['tags'], queryFn: tagsApi.list, staleTime: 60_000 })
  const set = <K extends keyof Form>(key: K, value: Form[K]) => setForm((f) => ({ ...f, [key]: value }))
  const setDiscord = <K extends keyof DiscordForm>(key: K, value: DiscordForm[K]) => setForm((f) => ({ ...f, discord: { ...f.discord, [key]: value } }))
  const view = form.viewKind === 'version' ? `version:${slugify(form.versionName)}` : form.viewKind
  const discord = form.format === 'discord'

  // An empty secret on an edit keeps the endpoint unsigned: the API stores what it is sent, and the secret is never read back.
  // Discord has nothing to check a signature with.
  const payload = (): NotificationEndpointRequest => ({
    name: form.name.trim(),
    url: form.url.trim(),
    secret: discord ? null : form.secret || null,
    enabled: form.enabled,
    events: form.events,
    tags: form.tags,
    format: form.format,
    view: hasView(form.format) ? view : 'default',
    discord: discord
      ? {
          username: form.discord.username.trim() || null,
          avatar: form.discord.avatar.trim() || null,
          author: form.discord.author.trim() || null,
          grabFields: form.discord.grabFields,
          importFields: form.discord.importFields,
          manualInteractionFields: form.discord.manualInteractionFields,
        }
      : null,
  })

  // Switching to Radarr's, Sonarr's or Discord's format drops the events it cannot send, so the selection never says more than what goes out.
  const setFormat = (format: WebhookFormat) =>
    setForm((f) => ({
      ...f,
      format,
      events: f.events.filter((e) => {
        const type = types.data?.find((candidate) => candidate.type === e)
        return type === undefined || sentAs(type, format)
      }),
    }))

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
  const toggle = <T extends string>(list: T[], value: T, order: readonly T[]) => (list.includes(value) ? list.filter((v) => v !== value) : order.filter((v) => v === value || list.includes(v)))
  const pastedDiscordUrl = !discord && discordWebhookUrl.test(form.url.trim())
  const toggleTag = (id: number) => set('tags', form.tags.includes(id) ? form.tags.filter((tag) => tag !== id) : [...form.tags, id])
  const canSubmit =
    form.name.trim().length > 0 && /^https?:\/\//i.test(form.url.trim()) && (form.format === 'bigarr' || form.viewKind !== 'version' || slugify(form.versionName).length > 0)

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="max-h-[90vh] overflow-y-auto sm:max-w-2xl">
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
              <Input
                id="nt-url"
                value={form.url}
                onChange={(e) => set('url', e.target.value)}
                placeholder={discord ? 'https://discord.com/api/webhooks/…' : 'https://example.org/hooks/bigarr'}
                autoComplete="off"
              />
            </div>
            {pastedDiscordUrl && (
              <Alert className="sm:col-span-2" data-testid="notification-discord-url">
                <AlertDescription className="flex flex-wrap items-center gap-2">
                  <span>{t('notifications.discordUrlDetected')}</span>
                  <Button type="button" size="sm" variant="outline" onClick={() => setFormat('discord')}>
                    {t('notifications.useDiscordFormat')}
                  </Button>
                </AlertDescription>
              </Alert>
            )}
            {!discord && (
              <div className="grid grid-cols-1 gap-2 sm:col-span-2">
                <Label htmlFor="nt-secret">{t('notifications.secret')}</Label>
                <Input id="nt-secret" type="password" value={form.secret} onChange={(e) => set('secret', e.target.value)} autoComplete="new-password" />
                <p className="text-xs text-muted-foreground">{endpoint?.hasSecret ? t('notifications.secretKeepHint') : t('notifications.secretHint')}</p>
              </div>
            )}
            <div className="flex items-center gap-2 sm:col-span-2">
              <Checkbox id="nt-enabled" checked={form.enabled} onCheckedChange={(v) => set('enabled', v === true)} />
              <Label htmlFor="nt-enabled" className="font-normal">
                {t('notifications.enabledToggle')}
              </Label>
            </div>

            <div className="grid grid-cols-1 gap-2">
              <Label htmlFor="nt-format">{t('notifications.format')}</Label>
              <Select value={form.format} onValueChange={(value) => setFormat(value as WebhookFormat)}>
                <SelectTrigger id="nt-format" className="w-full" data-testid="notification-format">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="bigarr">{t('notifications.formatBigarr')}</SelectItem>
                  <SelectItem value="radarr">{t('notifications.formatRadarr')}</SelectItem>
                  <SelectItem value="sonarr">{t('notifications.formatSonarr')}</SelectItem>
                  <SelectItem value="discord">{t('notifications.formatDiscord')}</SelectItem>
                </SelectContent>
              </Select>
            </div>
            {hasView(form.format) && (
              <div className="grid grid-cols-1 gap-2">
                <Label htmlFor="nt-view">{t('notifications.view')}</Label>
                <Select value={form.viewKind} onValueChange={(value) => set('viewKind', value as ViewKind)}>
                  <SelectTrigger id="nt-view" className="w-full" data-testid="notification-view">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="default">{t('connect.viewDefault')}</SelectItem>
                    <SelectItem value="all">{t('connect.viewAll')}</SelectItem>
                    <SelectItem value="version">{t('notifications.viewVersion')}</SelectItem>
                  </SelectContent>
                </Select>
              </div>
            )}
            <p className="text-xs text-muted-foreground sm:col-span-2">
              {form.format === 'radarr'
                ? t('notifications.formatRadarrHint')
                : form.format === 'sonarr'
                  ? t('notifications.formatSonarrHint')
                  : discord
                    ? t('notifications.formatDiscordHint')
                    : t('notifications.formatBigarrHint')}
            </p>
            {hasView(form.format) && form.viewKind === 'version' && (
              <div className="grid grid-cols-1 gap-2 sm:col-span-2">
                <Label htmlFor="nt-version">{t('connect.versionName')}</Label>
                <Input id="nt-version" value={form.versionName} onChange={(e) => set('versionName', e.target.value)} placeholder={t('connect.versionNamePlaceholder')} />
                <p className="text-xs text-muted-foreground">
                  {t('notifications.viewVersionHint')} <code>{view}</code>
                </p>
              </div>
            )}

            {discord && (
              <>
                <div className="grid grid-cols-1 gap-2">
                  <Label htmlFor="nt-discord-username">{t('notifications.discordUsername')}</Label>
                  <Input id="nt-discord-username" value={form.discord.username} onChange={(e) => setDiscord('username', e.target.value)} placeholder="Bigarr" />
                  <p className="text-xs text-muted-foreground">{t('notifications.discordUsernameHint')}</p>
                </div>
                <div className="grid grid-cols-1 gap-2">
                  <Label htmlFor="nt-discord-author">{t('notifications.discordAuthor')}</Label>
                  <Input id="nt-discord-author" value={form.discord.author} onChange={(e) => setDiscord('author', e.target.value)} placeholder="Bigarr" />
                  <p className="text-xs text-muted-foreground">{t('notifications.discordAuthorHint')}</p>
                </div>
                <div className="grid grid-cols-1 gap-2 sm:col-span-2">
                  <Label htmlFor="nt-discord-avatar">{t('notifications.discordAvatar')}</Label>
                  <Input id="nt-discord-avatar" value={form.discord.avatar} onChange={(e) => setDiscord('avatar', e.target.value)} placeholder="https://example.org/avatar.png" />
                  <p className="text-xs text-muted-foreground">{t('notifications.discordAvatarHint')}</p>
                </div>
                <details className="sm:col-span-2" data-testid="notification-discord-fields">
                  <summary className="cursor-pointer text-sm font-medium">{t('notifications.discordFields')}</summary>
                  <p className="mt-2 text-xs text-muted-foreground">{t('notifications.discordFieldsHint')}</p>
                  <DiscordFieldSet
                    legend={t('notifications.discordGrabFields')}
                    options={discordGrabFields}
                    chosen={form.discord.grabFields}
                    onToggle={(field) => setDiscord('grabFields', toggle(form.discord.grabFields, field, discordGrabFields))}
                  />
                  <DiscordFieldSet
                    legend={t('notifications.discordImportFields')}
                    options={discordImportFields}
                    chosen={form.discord.importFields}
                    onToggle={(field) => setDiscord('importFields', toggle(form.discord.importFields, field, discordImportFields))}
                  />
                  <DiscordFieldSet
                    legend={t('notifications.discordManualInteractionFields')}
                    options={discordManualInteractionFields}
                    chosen={form.discord.manualInteractionFields}
                    onToggle={(field) => setDiscord('manualInteractionFields', toggle(form.discord.manualInteractionFields, field, discordManualInteractionFields))}
                  />
                </details>
              </>
            )}

            <fieldset className="grid grid-cols-1 gap-2 sm:col-span-2">
              <legend className="text-sm font-medium">{t('notifications.tagsLegend')}</legend>
              <p className="text-xs text-muted-foreground">{form.tags.length === 0 ? t('notifications.allFilmsHint') : t('notifications.someTagsHint')}</p>
              {allTags.data?.length === 0 && <p className="text-xs text-muted-foreground">{t('notifications.noTags')}</p>}
              <div className="flex flex-wrap gap-x-4 gap-y-2" data-testid="notification-tags">
                {allTags.data?.map((tag) => (
                  <label key={tag.id} className="flex items-center gap-2 text-sm font-normal">
                    <Checkbox checked={form.tags.includes(tag.id)} onCheckedChange={() => toggleTag(tag.id)} />
                    {tag.label}
                  </label>
                ))}
              </div>
            </fieldset>

            <fieldset className="grid grid-cols-1 gap-2 sm:col-span-2">
              <legend className="text-sm font-medium">{t('notifications.eventsLegend')}</legend>
              <p className="text-xs text-muted-foreground">
                {form.events.length === 0 ? t('notifications.allEventsHint') : t('notifications.someEventsHint', { count: form.events.length })}
                {form.format === 'radarr' && <> {t('notifications.radarrEventsHint')}</>}
                {form.format === 'sonarr' && <> {t('notifications.sonarrEventsHint')}</>}
                {discord && <> {t('notifications.discordEventsHint')}</>}
              </p>
              <div className="grid gap-x-4 gap-y-2 sm:grid-cols-2" data-testid="notification-events">
                {types.data?.map((type) => {
                  const sent = sentAs(type, form.format)
                  return (
                    <label key={type.type} className={cn('flex items-start gap-2 text-sm font-normal', !sent && 'opacity-50')}>
                      <Checkbox className="mt-0.5" checked={sent && form.events.includes(type.type)} disabled={!sent} onCheckedChange={() => toggleEvent(type.type)} />
                      <span>
                        <span className="font-medium">{type.type}</span>
                        {form.format === 'radarr' && (
                          <span className="ml-1 text-xs text-muted-foreground">
                            {type.radarrEventType ? t('notifications.radarrAs', { type: type.radarrEventType }) : t('notifications.notInRadarr')}
                          </span>
                        )}
                        {form.format === 'sonarr' && (
                          <span className="ml-1 text-xs text-muted-foreground">
                            {type.sonarrEventType ? t('notifications.radarrAs', { type: type.sonarrEventType }) : t('notifications.notInSonarr')}
                          </span>
                        )}
                        {discord && !type.sentToDiscord && <span className="ml-1 text-xs text-muted-foreground">{t('notifications.notInDiscord')}</span>}
                        <span className="block text-xs text-muted-foreground">
                          {type.description}
                          {type.ignoresTags && form.tags.length > 0 && <> · {t('notifications.ignoresTags')}</>}
                        </span>
                      </span>
                    </label>
                  )
                })}
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

/** One kind of Discord message and the fields it carries, in the order Discord shows them. */
function DiscordFieldSet<T extends DiscordGrabField | DiscordImportField | DiscordManualInteractionField>({ legend, options, chosen, onToggle }: { legend: string; options: readonly T[]; chosen: T[]; onToggle: (field: T) => void }) {
  const { t } = useTranslation()
  return (
    <fieldset className="mt-3 grid grid-cols-1 gap-2">
      <legend className="text-sm">{legend}</legend>
      <div className="grid grid-cols-2 gap-x-4 gap-y-2 sm:grid-cols-3">
        {options.map((field) => (
          <label key={field} className="flex items-center gap-2 text-sm font-normal">
            <Checkbox checked={chosen.includes(field)} onCheckedChange={() => onToggle(field)} />
            {t(`notifications.discordField.${field}`)}
          </label>
        ))}
      </div>
    </fieldset>
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
