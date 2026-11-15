import { useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useNavigate } from '@tanstack/react-router'
import { LogInIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { ApiError, auth, externalLogin, type ExternalProvider } from '@/api/client'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardFooter, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { externalSignInMessage } from './externalSignInMessage'
import { runExternalSignIn } from './externalSignIn'

export function LoginPage() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [externalError, setExternalError] = useState<string | null>(null)

  const signedIn = async () => {
    await queryClient.invalidateQueries({ queryKey: ['me'] })
    await navigate({ to: '/' })
  }

  const login = useMutation({
    mutationFn: () => auth.login(username, password),
    onSuccess: signedIn,
  })

  // Offered only when on and linked; plex.tv being down never hides or blocks the password form.
  const providers = useQuery({ queryKey: ['auth', 'providers'], queryFn: externalLogin.providers, retry: false })
  const external = useMutation({
    mutationFn: (provider: ExternalProvider) => runExternalSignIn(() => externalLogin.startLogin(provider.key)),
    onMutate: () => setExternalError(null),
    onSuccess: async (result, provider) => {
      if (result.status === 'signedIn') {
        await signedIn()
        return
      }
      setExternalError(externalSignInMessage(t, result, provider.name))
    },
    onError: (e, provider) =>
      setExternalError(
        e instanceof ApiError && e.status === 502
          ? t('externalLogin.unreachable', { provider: provider.name })
          : t('auth.unexpectedError'),
      ),
  })

  const onSubmit = (event: FormEvent) => {
    event.preventDefault()
    login.mutate()
  }

  const error =
    login.error instanceof ApiError && login.error.status === 401
      ? t('auth.invalidCredentials')
      : login.error
        ? t('auth.unexpectedError')
        : null

  return (
    <main className="flex min-h-svh items-center justify-center bg-muted/40 p-4">
      <Card className="w-full max-w-sm">
        <CardHeader>
          <CardTitle className="text-2xl">{t('app.name')}</CardTitle>
          <CardDescription>{t('auth.subtitle')}</CardDescription>
        </CardHeader>
        <form onSubmit={onSubmit}>
          <CardContent className="flex flex-col gap-4">
            <div className="grid grid-cols-1 gap-2">
              <Label htmlFor="username">{t('auth.username')}</Label>
              <Input
                id="username"
                data-testid="login-username"
                value={username}
                onChange={(e) => setUsername(e.target.value)}
                autoComplete="username"
                autoFocus
              />
            </div>
            <div className="grid grid-cols-1 gap-2">
              <Label htmlFor="password">{t('auth.password')}</Label>
              <Input
                id="password"
                data-testid="login-password"
                type="password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                autoComplete="current-password"
              />
            </div>
            {error && (
              <Alert variant="destructive">
                <AlertDescription>{error}</AlertDescription>
              </Alert>
            )}
          </CardContent>
          <CardFooter className="flex flex-col gap-3 pt-6">
            <Button
              type="submit"
              className="w-full"
              data-testid="login-submit"
              disabled={login.isPending || !username || !password}
            >
              {t('auth.login')}
            </Button>
            {(providers.data?.length ?? 0) > 0 && (
              <>
                <div className="flex w-full items-center gap-3 text-xs text-muted-foreground">
                  <span className="h-px flex-1 bg-border" />
                  {t('externalLogin.or')}
                  <span className="h-px flex-1 bg-border" />
                </div>
                {providers.data?.map((provider) => (
                  <Button
                    key={provider.key}
                    type="button"
                    variant="outline"
                    className="w-full"
                    data-testid={`login-${provider.key}`}
                    disabled={external.isPending}
                    onClick={() => external.mutate(provider)}
                  >
                    <LogInIcon />
                    {external.isPending && external.variables?.key === provider.key
                      ? t('externalLogin.waiting', { provider: provider.name })
                      : t('externalLogin.signInWith', { provider: provider.name })}
                  </Button>
                ))}
                {externalError && (
                  <Alert variant="destructive" data-testid="login-external-error">
                    <AlertDescription>{externalError}</AlertDescription>
                  </Alert>
                )}
              </>
            )}
          </CardFooter>
        </form>
      </Card>
    </main>
  )
}
