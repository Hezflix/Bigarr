import React from 'react'
import ReactDOM from 'react-dom/client'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { Outlet, RouterProvider, createRootRoute, createRoute, createRouter, lazyRouteComponent, redirect } from '@tanstack/react-router'
import { ApiError, auth } from './api/client'
import { AppShell } from './components/AppShell'
import { ThemeProvider } from './components/theme'
import { LoginPage } from './features/auth/LoginPage'
import './i18n'
import './index.css'

const queryClient = new QueryClient({
  defaultOptions: { queries: { retry: false, refetchOnWindowFocus: false } },
})

const rootRoute = createRootRoute({ component: () => <Outlet /> })

const loginRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/login',
  component: LoginPage,
})

// Everything under the shell requires a session; a 401 sends to the login page.
const appRoute = createRoute({
  getParentRoute: () => rootRoute,
  id: 'app',
  beforeLoad: async () => {
    try {
      await queryClient.fetchQuery({ queryKey: ['me'], queryFn: auth.me, staleTime: 60_000 })
    } catch (error) {
      if (error instanceof ApiError && error.status === 401) {
        throw redirect({ to: '/login' })
      }
      throw error
    }
  },
  component: AppShell,
})

// Each page is its own chunk, fetched on first visit: the shell and the login page load without the rest.
const libraryRoute = createRoute({
  getParentRoute: () => appRoute,
  path: '/',
  // `?view=table&preset=missing-4k`: a layout and a saved view to open on; `rootFolder=2`: the films stored in that root
  // folder, biggest first. Applied once, then dropped from the URL.
  validateSearch: (search: Record<string, unknown>): { view?: string; preset?: string; rootFolder?: number } => ({
    view: typeof search.view === 'string' ? search.view : undefined,
    preset: typeof search.preset === 'string' ? search.preset : undefined,
    rootFolder: typeof search.rootFolder === 'number' ? search.rootFolder : undefined,
  }),
  component: lazyRouteComponent(() => import('./features/library/LibraryPage'), 'LibraryPage'),
})
const addRoute = createRoute({ getParentRoute: () => appRoute, path: '/add', component: lazyRouteComponent(() => import('./features/movies/AddMoviePage'), 'AddMoviePage') })
const movieRoute = createRoute({ getParentRoute: () => appRoute, path: '/movies/$movieId', component: lazyRouteComponent(() => import('./features/movies/MovieDetailPage'), 'MovieDetailPage') })
const seriesLibraryRoute = createRoute({ getParentRoute: () => appRoute, path: '/series', component: lazyRouteComponent(() => import('./features/series/SeriesLibraryPage'), 'SeriesLibraryPage') })
const addSeriesRoute = createRoute({ getParentRoute: () => appRoute, path: '/series/add', component: lazyRouteComponent(() => import('./features/series/AddSeriesPage'), 'AddSeriesPage') })
const seriesRoute = createRoute({ getParentRoute: () => appRoute, path: '/series/$seriesId', component: lazyRouteComponent(() => import('./features/series/SeriesDetailPage'), 'SeriesDetailPage') })
const queueRoute = createRoute({ getParentRoute: () => appRoute, path: '/activity/queue', component: lazyRouteComponent(() => import('./features/activity/QueuePage'), 'QueuePage') })
const releasesRoute = createRoute({ getParentRoute: () => appRoute, path: '/activity/releases', component: lazyRouteComponent(() => import('./features/activity/ReleasesPage'), 'ReleasesPage') })
const historyRoute = createRoute({ getParentRoute: () => appRoute, path: '/activity/history', component: lazyRouteComponent(() => import('./features/activity/HistoryPage'), 'HistoryPage') })
const compareRoute = createRoute({ getParentRoute: () => appRoute, path: '/activity/compare', component: lazyRouteComponent(() => import('./features/activity/ComparePage'), 'ComparePage') })
const blocklistRoute = createRoute({ getParentRoute: () => appRoute, path: '/activity/blocklist', component: lazyRouteComponent(() => import('./features/activity/BlocklistPage'), 'BlocklistPage') })
const nowPlayingRoute = createRoute({ getParentRoute: () => appRoute, path: '/playback/now', component: lazyRouteComponent(() => import('./features/playback/NowPlayingPage'), 'NowPlayingPage') })
// `?kind=unmatched`: the film plays linked to no film, as the Tautulli import report links them.
const playsRoute = createRoute({
  getParentRoute: () => appRoute,
  path: '/playback/plays',
  validateSearch: (search: Record<string, unknown>): { kind?: 'movie' | 'episode' | 'unmatched' } => ({
    kind: search.kind === 'movie' || search.kind === 'episode' || search.kind === 'unmatched' ? search.kind : undefined,
  }),
  component: lazyRouteComponent(() => import('./features/playback/PlaysPage'), 'PlaysPage'),
})
const wantedRoute = createRoute({ getParentRoute: () => appRoute, path: '/wanted', component: lazyRouteComponent(() => import('./features/wanted/WantedPage'), 'WantedPage') })
const systemRoute = createRoute({ getParentRoute: () => appRoute, path: '/system/status', component: lazyRouteComponent(() => import('./features/system/SystemPage'), 'SystemPage') })
const librarySettingsRoute = createRoute({ getParentRoute: () => appRoute, path: '/settings/library', component: lazyRouteComponent(() => import('./features/settings/LibrarySettingsPage'), 'LibrarySettingsPage') })
const mediaManagementRoute = createRoute({ getParentRoute: () => appRoute, path: '/settings/mediamanagement', component: lazyRouteComponent(() => import('./features/settings/MediaManagementPage'), 'MediaManagementPage') })
const indexersSettingsRoute = createRoute({ getParentRoute: () => appRoute, path: '/settings/indexers', component: lazyRouteComponent(() => import('./features/settings/IndexersSettingsPage'), 'IndexersSettingsPage') })
const qualitySettingsRoute = createRoute({ getParentRoute: () => appRoute, path: '/settings/quality', component: lazyRouteComponent(() => import('./features/settings/QualitySettingsPage'), 'QualitySettingsPage') })
const customFormatsRoute = createRoute({ getParentRoute: () => appRoute, path: '/settings/customformats', component: lazyRouteComponent(() => import('./features/settings/CustomFormatsPage'), 'CustomFormatsPage') })
const downloadClientsRoute = createRoute({ getParentRoute: () => appRoute, path: '/settings/downloadclients', component: lazyRouteComponent(() => import('./features/settings/DownloadClientsPage'), 'DownloadClientsPage') })
const notificationsRoute = createRoute({ getParentRoute: () => appRoute, path: '/settings/notifications', component: lazyRouteComponent(() => import('./features/settings/NotificationsPage'), 'NotificationsPage') })
const connectRoute = createRoute({ getParentRoute: () => appRoute, path: '/settings/connect', component: lazyRouteComponent(() => import('./features/settings/ConnectPage'), 'ConnectPage') })
const mediaServersRoute = createRoute({ getParentRoute: () => appRoute, path: '/settings/mediaservers', component: lazyRouteComponent(() => import('./features/settings/MediaServersPage'), 'MediaServersPage') })
const importRadarrRoute = createRoute({ getParentRoute: () => appRoute, path: '/settings/import-radarr', component: lazyRouteComponent(() => import('./features/settings/ImportRadarrPage'), 'ImportRadarrPage') })
const importTautulliRoute = createRoute({ getParentRoute: () => appRoute, path: '/settings/import-tautulli', component: lazyRouteComponent(() => import('./features/settings/ImportTautulliPage'), 'ImportTautulliPage') })
const followedRadarrsRoute = createRoute({ getParentRoute: () => appRoute, path: '/settings/followed-radarrs', component: lazyRouteComponent(() => import('./features/settings/RadarrFollowPage'), 'RadarrFollowPage') })

const router = createRouter({
  routeTree: rootRoute.addChildren([loginRoute, appRoute.addChildren([
      libraryRoute,
      addRoute,
      wantedRoute,
      nowPlayingRoute,
      playsRoute,
      movieRoute,
      seriesLibraryRoute,
      addSeriesRoute,
      seriesRoute,
      queueRoute,
      releasesRoute,
      historyRoute,
      blocklistRoute,
      compareRoute,
      librarySettingsRoute,
      mediaManagementRoute,
      indexersSettingsRoute,
      downloadClientsRoute,
      notificationsRoute,
      connectRoute,
      mediaServersRoute,
      importRadarrRoute,
      importTautulliRoute,
      followedRadarrsRoute,
      qualitySettingsRoute,
      customFormatsRoute,
      systemRoute,
    ])]),
})

declare module '@tanstack/react-router' {
  interface Register {
    router: typeof router
  }
}

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <ThemeProvider>
      <QueryClientProvider client={queryClient}>
        <RouterProvider router={router} />
      </QueryClientProvider>
    </ThemeProvider>
  </React.StrictMode>,
)
