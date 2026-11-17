import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, Outlet, useNavigate } from '@tanstack/react-router'
import { CompassIcon, FlagIcon, InboxIcon, LogOutIcon, UserIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { auth, canManageRequests } from '@/api/client'
import { ThemeToggle } from '@/components/ThemeToggle'
import { Button } from '@/components/ui/button'
import { Toaster } from '@/components/ui/sonner'
import { OpenIssuesBadge } from '@/features/issues/issueBits'
import { PendingBadge } from '@/features/requests/PendingBadge'
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from '@/components/ui/tooltip'

/**
 * The frame of someone who is not the admin: Bigarr's own pages are not theirs, so no sidebar, only what they may
 * see (Discover, their requests, everyone's for a request manager), who they are, the theme and sign out.
 */
export function DiscoverShell() {
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

  return (
    <TooltipProvider>
    <div className="flex min-h-svh flex-col bg-background">
      <header className="sticky top-0 z-10 flex h-14 shrink-0 items-center gap-3 border-b bg-background/95 px-4 backdrop-blur md:px-6" data-testid="discover-shell">
        <Link to="/discover" className="text-lg font-semibold tracking-tight">
          {t('app.name')}
        </Link>
        <nav className="flex items-center">
          <Link
            to="/discover"
            activeOptions={{ exact: false }}
            className="inline-flex items-center gap-1.5 rounded-md px-2 py-1 text-sm text-muted-foreground hover:text-foreground data-[status=active]:font-medium data-[status=active]:text-foreground"
          >
            <CompassIcon className="size-4" aria-hidden="true" />
            {t('nav.discover')}
          </Link>
          <Link
            to="/requests"
            className="inline-flex items-center gap-1.5 rounded-md px-2 py-1 text-sm text-muted-foreground hover:text-foreground data-[status=active]:font-medium data-[status=active]:text-foreground"
            data-testid="nav-requests"
          >
            <InboxIcon className="size-4" aria-hidden="true" />
            <span className="max-sm:sr-only">{canManageRequests(me.data) ? t('nav.requests') : t('requests.mine')}</span>
            {canManageRequests(me.data) && <PendingBadge className="ml-0" />}
          </Link>
          <Link
            to="/issues"
            className="inline-flex items-center gap-1.5 rounded-md px-2 py-1 text-sm text-muted-foreground hover:text-foreground data-[status=active]:font-medium data-[status=active]:text-foreground"
            data-testid="nav-issues"
          >
            <FlagIcon className="size-4" aria-hidden="true" />
            <span className="max-sm:sr-only">{canManageRequests(me.data) ? t('nav.issues') : t('issues.mine')}</span>
            {canManageRequests(me.data) && <OpenIssuesBadge className="ml-0" />}
          </Link>
        </nav>
        <div className="ml-auto flex items-center gap-1">
          <Link
            to="/profile"
            className="mr-1 inline-flex max-w-40 items-center gap-1.5 truncate rounded-md px-2 py-1 text-sm text-muted-foreground hover:text-foreground data-[status=active]:text-foreground"
            aria-label={t('profile.title')}
            data-testid="signed-in-as"
          >
            <UserIcon className="size-4 shrink-0" aria-hidden="true" />
            <span className="truncate max-sm:sr-only">{me.data?.username}</span>
          </Link>
          <ThemeToggle />
          <Tooltip>
            <TooltipTrigger asChild>
              <Button variant="ghost" size="icon-sm" onClick={() => logout.mutate()} disabled={logout.isPending} aria-label={t('nav.logout')} data-testid="logout">
                <LogOutIcon />
              </Button>
            </TooltipTrigger>
            <TooltipContent side="bottom">{t('nav.logout')}</TooltipContent>
          </Tooltip>
        </div>
      </header>
      <main className="flex min-w-0 flex-1 flex-col gap-6 p-4 md:p-6">
        <Outlet />
      </main>
      <Toaster />
    </div>
    </TooltipProvider>
  )
}
