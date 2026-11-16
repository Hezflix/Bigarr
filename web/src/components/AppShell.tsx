import { Link, Outlet, useMatchRoute, useNavigate } from '@tanstack/react-router'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { cn } from 'cn'
import { LogOutIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { auth, health, system } from '@/api/client'
import { useServerEvents } from '@/hooks/useServerEvents'
import { PaletteProvider } from '@/features/palette/PaletteProvider'
import { PaletteTrigger } from '@/features/palette/PaletteTrigger'
import { useNavSections } from '@/components/nav'
import { ThemeToggle } from '@/components/ThemeToggle'
import { Button } from '@/components/ui/button'
import { Separator } from '@/components/ui/separator'
import {
  Sidebar,
  SidebarContent,
  SidebarFooter,
  SidebarGroup,
  SidebarGroupContent,
  SidebarGroupLabel,
  SidebarHeader,
  SidebarInset,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
  SidebarProvider,
  SidebarTrigger,
  useSidebar,
} from '@/components/ui/sidebar'
import { Toaster } from '@/components/ui/sonner'
import { OpenIssuesBadge } from '@/features/issues/issueBits'
import { PendingBadge } from '@/features/requests/PendingBadge'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'

/** Warnings and errors of the last health check, next to the System entry. Notices are not worth a badge. */
function HealthBadge() {
  const { t } = useTranslation()
  const query = useQuery({ queryKey: ['health'], queryFn: health.get, refetchInterval: 60_000 })
  const issues = query.data?.issues.filter((i) => i.type !== 'notice') ?? []
  if (issues.length === 0) return null
  const hasError = issues.some((i) => i.type === 'error')
  return (
    <span
      className={cn(
        'ml-auto inline-flex h-5 min-w-5 items-center justify-center rounded-full px-1.5 text-xs font-medium tabular-nums group-data-[collapsible=icon]:hidden',
        hasError ? 'bg-red-500/15 text-red-700 dark:text-red-300' : 'bg-amber-500/15 text-amber-700 dark:text-amber-300',
      )}
      data-testid="health-badge"
    >
      <span aria-hidden="true">{issues.length}</span>
      <span className="sr-only">{t('health.badge', { count: issues.length })}</span>
    </span>
  )
}

export function AppShell() {
  const { t } = useTranslation()
  useServerEvents()

  return (
    <SidebarProvider>
      <PaletteProvider>
        <AppSidebar />
        {/* --app-header: the bar's height, for what sticks under it (the library table's header). */}
        <SidebarInset className="min-w-0 [--app-header:3.5rem] md:[--app-header:2.75rem]">
          {/* A thin bar on every screen: the palette's button, and below `md`, where the sidebar is a sheet, its trigger. */}
          <header className="sticky top-0 z-10 flex h-(--app-header) shrink-0 items-center gap-2 border-b bg-background/95 px-3 backdrop-blur md:px-6">
            <SidebarTrigger className="md:hidden" />
            <Separator orientation="vertical" className="!h-5 md:hidden" />
            <Link to="/" className="font-semibold tracking-tight md:hidden">
              {t('app.name')}
            </Link>
            <PaletteTrigger className="ml-auto w-auto max-w-48 flex-1 md:max-w-72" />
          </header>
          <div className="flex min-w-0 flex-1 flex-col gap-6 p-4 md:p-6">
            <Outlet />
          </div>
        </SidebarInset>
        <Toaster />
      </PaletteProvider>
    </SidebarProvider>
  )
}

function AppSidebar() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const matchRoute = useMatchRoute()
  const sections = useNavSections()
  const { setOpenMobile } = useSidebar()

  const me = useQuery({ queryKey: ['me'], queryFn: auth.me })
  const status = useQuery({ queryKey: ['system', 'status'], queryFn: system.status, staleTime: 5 * 60_000 })
  const logout = useMutation({
    mutationFn: auth.logout,
    onSuccess: async () => {
      queryClient.clear()
      await navigate({ to: '/login' })
    },
  })

  const version = status.data ? `v${status.data.version.split('+')[0]}` : null

  return (
    // The test id sits on an inner wrapper: on mobile <Sidebar> forwards its props to the
    // Sheet root, which renders nothing, so a test id on <Sidebar> itself would disappear.
    <Sidebar>
      <div className="flex h-full w-full flex-col" data-testid="app-sidebar">
        <SidebarHeader>
          <Link to="/" onClick={() => setOpenMobile(false)} className="px-2 py-1 text-lg font-semibold tracking-tight">
            {t('app.name')}
          </Link>
        </SidebarHeader>

        <SidebarContent>
          {sections.map((section) => (
            <SidebarGroup key={section.title}>
              <SidebarGroupLabel>{section.title}</SidebarGroupLabel>
              <SidebarGroupContent>
                <SidebarMenu>
                  {section.items.filter((item) => !item.paletteOnly).map((item) => (
                    <SidebarMenuItem key={item.to}>
                      <SidebarMenuButton asChild isActive={!!matchRoute({ to: item.to, fuzzy: !item.exact })} tooltip={item.label}>
                        <Link to={item.to} onClick={() => setOpenMobile(false)}>
                          <item.icon />
                          <span>{item.label}</span>
                          {item.health && <HealthBadge />}
                          {item.pendingRequests && <PendingBadge className="group-data-[collapsible=icon]:hidden" />}
                          {item.openIssues && <OpenIssuesBadge className="group-data-[collapsible=icon]:hidden" />}
                        </Link>
                      </SidebarMenuButton>
                    </SidebarMenuItem>
                  ))}
                </SidebarMenu>
              </SidebarGroupContent>
            </SidebarGroup>
          ))}
        </SidebarContent>

        <SidebarFooter>
          <div className="flex items-center gap-1 group-data-[collapsible=icon]:flex-col">
            <div className="flex min-w-0 flex-1 flex-col px-2 group-data-[collapsible=icon]:hidden">
              <span className="truncate text-sm font-medium">{me.data?.username}</span>
              {version && <span className="text-xs text-muted-foreground">{version}</span>}
            </div>
            <ThemeToggle />
            <Tooltip>
              <TooltipTrigger asChild>
                <Button
                  variant="ghost"
                  size="icon-sm"
                  onClick={() => logout.mutate()}
                  disabled={logout.isPending}
                  aria-label={t('nav.logout')}
                  data-testid="logout"
                >
                  <LogOutIcon />
                </Button>
              </TooltipTrigger>
              <TooltipContent side="top">{t('nav.logout')}</TooltipContent>
            </Tooltip>
          </div>
        </SidebarFooter>
      </div>
    </Sidebar>
  )
}
