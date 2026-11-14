import { useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { cn } from 'cn'
import { CopyIcon, KeyRoundIcon, PlusIcon, Trash2Icon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ApiError, apiKeys, slugify, system, type ApiKey, type ApiKeyCreated } from '@/api/client'
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
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { formatAge, formatDateTime } from '@/i18n'

type ViewKind = 'default' | 'all' | 'version'

const RADARR_VERSION = '5.99.0.0'
const SONARR_VERSION = '4.99.0.0'
/** The Sonarr surface's URL base: Sonarr and Radarr share their routes, so each needs its own base on one Bigarr. */
const SONARR_URL_BASE = '/sonarr'

/** `version:4k` → kind + name, so the table can label a key without parsing in the template. */
function splitView(view: string): { kind: ViewKind; name: string } {
  if (view === 'all') return { kind: 'all', name: '' }
  if (view.startsWith('version:')) return { kind: 'version', name: view.slice('version:'.length) }
  return { kind: 'default', name: '' }
}

async function copy(text: string): Promise<boolean> {
  try {
    await navigator.clipboard.writeText(text)
    return true
  } catch {
    return false
  }
}

/** API keys and the recipes for the apps that speak Radarr: one key per app, the key's view decides what it sees. */
export function ConnectPage() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const keys = useQuery({ queryKey: ['apiKeys'], queryFn: apiKeys.list })
  const status = useQuery({ queryKey: ['system', 'status'], queryFn: system.status, staleTime: 5 * 60_000 })
  const [name, setName] = useState('')
  const [kind, setKind] = useState<ViewKind>('default')
  const [versionName, setVersionName] = useState('')
  const [created, setCreated] = useState<ApiKeyCreated | null>(null)
  const [pendingRevoke, setPendingRevoke] = useState<ApiKey | null>(null)
  const baseUrl = typeof window === 'undefined' ? '' : window.location.origin
  const sonarrUrl = baseUrl + SONARR_URL_BASE

  const compatView = kind === 'version' ? `version:${slugify(versionName)}` : kind
  const canCreate = name.trim().length > 0 && (kind !== 'version' || slugify(versionName).length > 0)

  const create = useMutation({
    mutationFn: () => apiKeys.create(name.trim(), compatView),
    onSuccess: async (key) => {
      setCreated(key)
      setName('')
      setVersionName('')
      setKind('default')
      await queryClient.invalidateQueries({ queryKey: ['apiKeys'] })
    },
    onError: (e) => toast.error(e instanceof ApiError ? e.message : t('connect.createFailed')),
  })
  const revoke = useMutation({
    mutationFn: (id: number) => apiKeys.revoke(id),
    onSuccess: async () => {
      setPendingRevoke(null)
      await queryClient.invalidateQueries({ queryKey: ['apiKeys'] })
    },
    onError: (e) => {
      setPendingRevoke(null)
      toast.error(e instanceof ApiError ? e.message : t('connect.revokeFailed'))
    },
  })

  const submit = (e: FormEvent) => {
    e.preventDefault()
    if (canCreate) create.mutate()
  }

  const copyToClipboard = async (text: string) => {
    const ok = await copy(text)
    if (ok) toast.success(t('connect.copied'))
    else toast.error(t('connect.copyFailed'))
  }

  const active = keys.data?.filter((k) => !k.revokedAt) ?? []
  const revoked = keys.data?.filter((k) => k.revokedAt) ?? []

  return (
    <>
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="text-2xl font-semibold tracking-tight">{t('connect.title')}</h1>
      </div>
      <p className="max-w-3xl text-sm text-muted-foreground">{t('connect.hint')}</p>

      <section className="flex flex-col gap-3">
        <h2 className="flex items-center gap-2 text-xl font-semibold tracking-tight">
          <KeyRoundIcon className="size-5" />
          {t('connect.keys')}
        </h2>

        {created && (
          <Alert data-testid="created-key">
            <AlertDescription className="flex flex-col gap-2">
              <span>{t('connect.created')}</span>
              <div className="flex flex-wrap items-center gap-2">
                <code className="rounded bg-muted px-2 py-1 font-mono text-sm break-all select-all">{created.key}</code>
                <Button type="button" size="sm" variant="outline" onClick={() => void copyToClipboard(created.key)}>
                  <CopyIcon />
                  {t('connect.copy')}
                </Button>
              </div>
            </AlertDescription>
          </Alert>
        )}

        <div className="overflow-x-auto rounded-lg border">
          <Table data-testid="apikeys-table">
            <TableHeader>
              <TableRow>
                <TableHead>{t('connect.name')}</TableHead>
                <TableHead>{t('connect.prefix')}</TableHead>
                <TableHead>{t('connect.view')}</TableHead>
                <TableHead>{t('connect.createdAt')}</TableHead>
                <TableHead>{t('connect.lastUsed')}</TableHead>
                <TableHead className="text-right">{t('versions.actions')}</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {[...active, ...revoked].map((key) => {
                const view = splitView(key.compatView)
                return (
                  <TableRow key={key.id} className={cn(key.revokedAt && 'text-muted-foreground')} data-testid={`apikey-${key.id}`}>
                    <TableCell className="font-medium">
                      {key.name}
                      {key.revokedAt && <Badge variant="outline" className="ml-2 border-border bg-muted text-muted-foreground">{t('connect.revoked')}</Badge>}
                    </TableCell>
                    <TableCell className="font-mono text-xs">{key.keyPrefix}…</TableCell>
                    <TableCell>
                      <Badge variant="outline">{view.kind === 'version' ? t('connect.viewVersionLabel', { name: view.name }) : t(`connect.views.${view.kind}`)}</Badge>
                    </TableCell>
                    <TableCell className="whitespace-nowrap">
                      <Tooltip>
                        <TooltipTrigger asChild>
                          <span>{formatAge(key.createdAt)}</span>
                        </TooltipTrigger>
                        <TooltipContent>{formatDateTime(key.createdAt)}</TooltipContent>
                      </Tooltip>
                    </TableCell>
                    <TableCell className="whitespace-nowrap">
                      {key.lastUsedAt ? (
                        <Tooltip>
                          <TooltipTrigger asChild>
                            <span>{formatAge(key.lastUsedAt)}</span>
                          </TooltipTrigger>
                          <TooltipContent>{formatDateTime(key.lastUsedAt)}</TooltipContent>
                        </Tooltip>
                      ) : (
                        <span className="text-muted-foreground">{t('connect.neverUsed')}</span>
                      )}
                    </TableCell>
                    <TableCell>
                      <div className="flex justify-end">
                        {!key.revokedAt && (
                          <Tooltip>
                            <TooltipTrigger asChild>
                              <Button type="button" variant="ghost" size="icon" onClick={() => setPendingRevoke(key)} aria-label={t('connect.revoke')} data-testid={`revoke-${key.id}`}>
                                <Trash2Icon />
                              </Button>
                            </TooltipTrigger>
                            <TooltipContent>{t('connect.revoke')}</TooltipContent>
                          </Tooltip>
                        )}
                      </div>
                    </TableCell>
                  </TableRow>
                )
              })}
              {keys.data?.length === 0 && (
                <TableRow>
                  <TableCell colSpan={6} className="text-muted-foreground">
                    {t('connect.noKeys')}
                  </TableCell>
                </TableRow>
              )}
            </TableBody>
          </Table>
        </div>

        <form className="flex max-w-3xl flex-col gap-3 rounded-lg border p-4" onSubmit={submit} data-testid="apikey-form">
          <h3 className="font-medium">{t('connect.add')}</h3>
          <div className="grid gap-3 sm:grid-cols-2">
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="key-name">{t('connect.name')}</Label>
              <Input id="key-name" value={name} onChange={(e) => setName(e.target.value)} placeholder={t('connect.namePlaceholder')} maxLength={128} />
            </div>
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="key-view">{t('connect.view')}</Label>
              <Select value={kind} onValueChange={(value) => setKind(value as ViewKind)}>
                <SelectTrigger id="key-view" className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="default">{t('connect.viewDefault')}</SelectItem>
                  <SelectItem value="all">{t('connect.viewAll')}</SelectItem>
                  <SelectItem value="version">{t('connect.viewVersion')}</SelectItem>
                </SelectContent>
              </Select>
            </div>
            {kind === 'version' && (
              <div className="flex flex-col gap-1.5 sm:col-span-2">
                <Label htmlFor="key-version">{t('connect.versionName')}</Label>
                <Input id="key-version" value={versionName} onChange={(e) => setVersionName(e.target.value)} placeholder={t('connect.versionNamePlaceholder')} />
                <p className="text-xs text-muted-foreground">{t('connect.versionNameHint')}</p>
              </div>
            )}
          </div>
          <div className="flex items-center gap-3">
            <Button type="submit" disabled={!canCreate || create.isPending} data-testid="create-apikey">
              <PlusIcon />
              {t('connect.create')}
            </Button>
            <code className="text-xs text-muted-foreground">{compatView}</code>
          </div>
        </form>
      </section>

      <section className="flex flex-col gap-3">
        <h2 className="text-xl font-semibold tracking-tight">{t('connect.howTo')}</h2>
        <div className="flex flex-wrap items-center gap-2 text-sm">
          <span className="text-muted-foreground">{t('connect.url')}</span>
          <code className="rounded bg-muted px-2 py-1 font-mono select-all">{baseUrl}</code>
          <Button type="button" size="sm" variant="ghost" onClick={() => void copyToClipboard(baseUrl)} aria-label={t('connect.copy')}>
            <CopyIcon />
          </Button>
          {status.data && <span className="text-xs text-muted-foreground">{t('connect.legacyHint', { version: RADARR_VERSION })}</span>}
        </div>
        <div className="grid gap-3 md:grid-cols-3">
          <Card>
            <CardHeader>
              <CardTitle>{t('connect.seerr')}</CardTitle>
            </CardHeader>
            <CardContent className="text-sm text-muted-foreground">{t('connect.seerrHint')}</CardContent>
          </Card>
          <Card>
            <CardHeader>
              <CardTitle>{t('connect.bazarr')}</CardTitle>
            </CardHeader>
            <CardContent className="text-sm text-muted-foreground">{t('connect.bazarrHint')}</CardContent>
          </Card>
          <Card>
            <CardHeader>
              <CardTitle>{t('connect.prowlarr')}</CardTitle>
            </CardHeader>
            <CardContent className="text-sm text-muted-foreground">{t('connect.prowlarrHint')}</CardContent>
          </Card>
        </div>
        <div className="flex flex-wrap items-center gap-2 text-sm">
          <span className="text-muted-foreground">{t('connect.sonarrUrl')}</span>
          <code className="rounded bg-muted px-2 py-1 font-mono select-all">{sonarrUrl}</code>
          <Button type="button" size="sm" variant="ghost" onClick={() => void copyToClipboard(sonarrUrl)} aria-label={t('connect.copy')}>
            <CopyIcon />
          </Button>
          {status.data && <span className="text-xs text-muted-foreground">{t('connect.sonarrLegacyHint', { version: SONARR_VERSION, base: SONARR_URL_BASE })}</span>}
        </div>
        <Card>
          <CardHeader>
            <CardTitle>{t('connect.sonarr')}</CardTitle>
          </CardHeader>
          <CardContent className="text-sm text-muted-foreground">{t('connect.sonarrHint', { base: SONARR_URL_BASE })}</CardContent>
        </Card>
      </section>

      <AlertDialog open={pendingRevoke !== null} onOpenChange={(open) => !open && setPendingRevoke(null)}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>{t('connect.revokeTitle')}</AlertDialogTitle>
            <AlertDialogDescription>{pendingRevoke && t('connect.confirmRevoke', { name: pendingRevoke.name })}</AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>{t('common.cancel')}</AlertDialogCancel>
            <AlertDialogAction onClick={() => pendingRevoke && revoke.mutate(pendingRevoke.id)} disabled={revoke.isPending}>
              {t('connect.revoke')}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </>
  )
}
