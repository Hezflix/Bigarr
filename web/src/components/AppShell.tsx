import { Link, Outlet, useMatchRoute, useNavigate } from '@tanstack/react-router'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { FilmIcon, FolderCogIcon, FolderOpenIcon, HardDriveDownloadIcon, HistoryIcon, ListOrderedIcon, LogOutIcon, PlusIcon, RadioTowerIcon, RssIcon, SlidersHorizontalIcon, TagsIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { auth, system } from '@/api/client'
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
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'

type NavItem = {
  to:
    | '/'
    | '/add'
    | '/activity/queue'
    | '/activity/releases'
    | '/activity/history'
    | '/settings/library'
    | '/settings/mediamanagement'
    | '/settings/indexers'
    | '/settings/downloadclients'
    | '/settings/quality'
    | '/settings/customformats'
  label: string
  icon: React.ComponentType<{ className?: string }>
  exact?: boolean
}

type NavSection = { title: string; items: NavItem[] }

/** Adding a page = one entry here. Sections keep the sidebar readable as slices land. */
function useSections(): NavSection[] {
  const { t } = useTranslation()
  return [
    {
      title: t('nav.movies'),
      items: [
        { to: '/', label: t('nav.library'), icon: FilmIcon, exact: true },
        { to: '/add', label: t('nav.addMovie'), icon: PlusIcon },
      ],
    },
    {
      title: t('nav.activity'),
      items: [
        { to: '/activity/queue', label: t('nav.queue'), icon: ListOrderedIcon },
        { to: '/activity/releases', label: t('nav.releases'), icon: RssIcon },
        { to: '/activity/history', label: t('nav.history'), icon: HistoryIcon },
      ],
    },
    {
      title: t('nav.settings'),
      items: [
        { to: '/settings/library', label: t('nav.settingsLibrary'), icon: FolderOpenIcon },
        { to: '/settings/mediamanagement', label: t('nav.settingsMediaManagement'), icon: FolderCogIcon },
        { to: '/settings/indexers', label: t('nav.settingsIndexers'), icon: RadioTowerIcon },
        { to: '/settings/downloadclients', label: t('nav.settingsDownloadClients'), icon: HardDriveDownloadIcon },
        { to: '/settings/quality', label: t('nav.settingsQuality'), icon: SlidersHorizontalIcon },
        { to: '/settings/customformats', label: t('nav.settingsCustomFormats'), icon: TagsIcon },
      ],
    },
  ]
}

export function AppShell() {
  const { t } = useTranslation()

  return (
    <SidebarProvider>
      <AppSidebar />
      <SidebarInset className="min-w-0">
        {/* The sidebar is a sheet below the `md` breakpoint, so small screens need a trigger. */}
        <header className="sticky top-0 z-10 flex h-14 shrink-0 items-center gap-2 border-b bg-background/95 px-3 backdrop-blur md:hidden">
          <SidebarTrigger />
          <Separator orientation="vertical" className="!h-5" />
          <Link to="/" className="font-semibold tracking-tight">
            {t('app.name')}
          </Link>
        </header>
        <div className="flex min-w-0 flex-1 flex-col gap-6 p-4 md:p-6">
          <Outlet />
        </div>
      </SidebarInset>
      <Toaster />
    </SidebarProvider>
  )
}

function AppSidebar() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const matchRoute = useMatchRoute()
  const sections = useSections()
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
                  {section.items.map((item) => (
                    <SidebarMenuItem key={item.to}>
                      <SidebarMenuButton asChild isActive={!!matchRoute({ to: item.to, fuzzy: !item.exact })} tooltip={item.label}>
                        <Link to={item.to} onClick={() => setOpenMobile(false)}>
                          <item.icon />
                          <span>{item.label}</span>
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
