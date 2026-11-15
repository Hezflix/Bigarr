import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useNavigate } from '@tanstack/react-router'
import { CheckIcon, LogOutIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { auth } from '@/api/client'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardFooter, CardHeader, CardTitle } from '@/components/ui/card'

/**
 * Where a signed-in user who is not the admin lands: Bigarr's own pages are the admin's, and the request pages are
 * not there yet. Says who they are signed in as and what their account will let them do.
 */
export function SignedInPage() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const me = useQuery({ queryKey: ['me'], queryFn: auth.me, staleTime: 60_000 })
  const logout = useMutation({
    mutationFn: auth.logout,
    onSettled: async () => {
      queryClient.clear()
      await navigate({ to: '/login' })
    },
  })
  const permissions = me.data?.permissions ?? []

  return (
    <main className="flex min-h-svh items-center justify-center bg-muted/40 p-4">
      <Card className="w-full max-w-sm" data-testid="signed-in">
        <CardHeader>
          <CardTitle className="text-2xl">{t('app.name')}</CardTitle>
          <CardDescription>{t('signedIn.as', { name: me.data?.username ?? '' })}</CardDescription>
        </CardHeader>
        <CardContent className="flex flex-col gap-4 text-sm">
          <p>{t('signedIn.soon')}</p>
          {permissions.length > 0 && (
            <div className="grid gap-2">
              <p className="font-medium">{t('signedIn.youMay')}</p>
              <ul className="grid gap-1.5">
                {permissions.map((p) => (
                  <li key={p} className="flex items-center gap-2 text-muted-foreground">
                    <CheckIcon className="size-4 shrink-0 text-primary" />
                    {t(`users.permissions.${p.charAt(0).toLowerCase()}${p.slice(1)}`, { defaultValue: p })}
                  </li>
                ))}
              </ul>
            </div>
          )}
        </CardContent>
        <CardFooter>
          <Button type="button" variant="outline" className="w-full" disabled={logout.isPending} onClick={() => logout.mutate()}>
            <LogOutIcon />
            {t('signedIn.signOut')}
          </Button>
        </CardFooter>
      </Card>
    </main>
  )
}
