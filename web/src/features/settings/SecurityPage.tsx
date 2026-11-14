import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { InfoIcon, LinkIcon, LogInIcon, UnlinkIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ApiError, externalLogin, type ExternalLoginSettings } from '@/api/client'
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
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Checkbox } from '@/components/ui/checkbox'
import { Label } from '@/components/ui/label'
import { formatDateTime } from '@/i18n'
import { runExternalSignIn } from '../auth/externalSignIn'
import { externalSignInMessage } from '../auth/externalSignInMessage'

/** How people sign in: the password always, an outside account (Plex) beside it once linked and turned on. */
export function SecurityPage() {
  const { t } = useTranslation()
  const settings = useQuery({ queryKey: ['auth', 'external'], queryFn: externalLogin.settings })

  return (
    <>
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="text-2xl font-semibold tracking-tight">{t('security.title')}</h1>
      </div>
      <p className="max-w-3xl text-sm text-muted-foreground">{t('security.hint')}</p>

      <Alert className="max-w-3xl" data-testid="security-local-login-note">
        <InfoIcon />
        <AlertDescription>{t('security.localLoginAlwaysWorks')}</AlertDescription>
      </Alert>

      {settings.isPending && <p className="text-sm text-muted-foreground">{t('common.loading')}</p>}
      {settings.isError && <p className="text-sm text-destructive">{t('common.unexpectedError')}</p>}
      {settings.data?.map((provider) => <ProviderCard key={provider.key} provider={provider} />)}
    </>
  )
}

function ProviderCard({ provider }: { provider: ExternalLoginSettings }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [confirmUnlink, setConfirmUnlink] = useState(false)
  const [linkError, setLinkError] = useState<string | null>(null)
  const name = provider.name
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['auth'] })

  const toggle = useMutation({
    mutationFn: (enabled: boolean) => externalLogin.setEnabled(provider.key, enabled),
    onSuccess: refresh,
    onError: (e) => toast.error(e instanceof ApiError ? e.message : t('common.unexpectedError')),
  })
  const link = useMutation({
    mutationFn: () => runExternalSignIn(() => externalLogin.startLink(provider.key)),
    onMutate: () => setLinkError(null),
    onSuccess: async (result) => {
      await refresh()
      if (result.status === 'linked') toast.success(t('security.linked', { provider: name, account: result.account ?? name }))
      else setLinkError(externalSignInMessage(t, result, name))
    },
    onError: (e) =>
      setLinkError(e instanceof ApiError && e.status === 502 ? t('externalLogin.unreachable', { provider: name }) : t('common.unexpectedError')),
  })
  const unlink = useMutation({
    mutationFn: () => externalLogin.unlink(provider.key),
    onSuccess: async () => {
      setConfirmUnlink(false)
      await refresh()
    },
    onError: (e) => toast.error(e instanceof ApiError ? e.message : t('common.unexpectedError')),
  })

  const linked = provider.link
  const offered = provider.enabled && linked !== null

  return (
    <Card className="max-w-3xl" data-testid={`security-${provider.key}`}>
      <CardHeader>
        <CardTitle className="flex flex-wrap items-center gap-2">
          <LogInIcon className="size-5" />
          {t('externalLogin.signInWith', { provider: name })}
          <Badge variant={offered ? 'default' : 'secondary'}>
            {offered ? t('security.offered') : t('security.notOffered')}
          </Badge>
        </CardTitle>
        <CardDescription>{t('security.providerHint', { provider: name })}</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-5">
        <div className="flex items-start gap-3">
          <Checkbox
            id={`external-${provider.key}-enabled`}
            checked={provider.enabled}
            disabled={toggle.isPending}
            onCheckedChange={(v) => toggle.mutate(v === true)}
            className="mt-0.5"
            data-testid={`security-${provider.key}-enabled`}
          />
          <div className="grid gap-1">
            <Label htmlFor={`external-${provider.key}-enabled`}>{t('security.enable', { provider: name })}</Label>
            <p className="text-sm text-muted-foreground">
              {linked ? t('security.enableHint', { provider: name }) : t('security.enableNeedsLink', { provider: name })}
            </p>
          </div>
        </div>

        <div className="flex flex-col gap-3 rounded-lg border p-4 sm:flex-row sm:items-center sm:justify-between">
          {linked ? (
            <div className="flex min-w-0 items-center gap-3">
              <span
                aria-hidden
                className="flex size-10 shrink-0 items-center justify-center rounded-full bg-muted text-sm font-semibold uppercase"
              >
                {(linked.externalUsername ?? '?').slice(0, 2)}
              </span>
              <div className="min-w-0">
                <p className="truncate font-medium" data-testid={`security-${provider.key}-account`}>
                  {linked.externalUsername ?? linked.externalId}
                </p>
                <p className="text-sm text-muted-foreground">
                  {t('security.linkedSince', { date: formatDateTime(linked.linkedAt) })}
                  {' · '}
                  {linked.lastLoginAt
                    ? t('security.lastSignIn', { date: formatDateTime(linked.lastLoginAt) })
                    : t('security.neverSignedIn')}
                </p>
              </div>
            </div>
          ) : (
            <p className="text-sm text-muted-foreground">{t('security.notLinked', { provider: name })}</p>
          )}
          <div className="flex shrink-0 flex-wrap gap-2">
            <Button
              type="button"
              variant={linked ? 'outline' : 'default'}
              disabled={link.isPending}
              onClick={() => link.mutate()}
              data-testid={`security-${provider.key}-link`}
            >
              <LinkIcon />
              {link.isPending
                ? t('externalLogin.waiting', { provider: name })
                : linked
                  ? t('security.relink', { provider: name })
                  : t('security.link', { provider: name })}
            </Button>
            {linked && (
              <Button
                type="button"
                variant="outline"
                onClick={() => setConfirmUnlink(true)}
                data-testid={`security-${provider.key}-unlink`}
              >
                <UnlinkIcon />
                {t('security.unlink')}
              </Button>
            )}
          </div>
        </div>

        {linkError && (
          <Alert variant="destructive">
            <AlertDescription>{linkError}</AlertDescription>
          </Alert>
        )}
      </CardContent>

      <AlertDialog open={confirmUnlink} onOpenChange={setConfirmUnlink}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>{t('security.unlinkTitle', { provider: name })}</AlertDialogTitle>
            <AlertDialogDescription>{t('security.unlinkBody', { provider: name })}</AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>{t('common.cancel')}</AlertDialogCancel>
            <AlertDialogAction onClick={() => unlink.mutate()} disabled={unlink.isPending}>
              {t('security.unlink')}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </Card>
  )
}
