import React from 'react'
import ReactDOM from 'react-dom/client'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { Outlet, RouterProvider, createRootRoute, createRoute, createRouter, lazyRouteComponent, redirect } from '@tanstack/react-router'
import { ApiError, auth, isAdmin } from './api/client'
import { AppShell } from './components/AppShell'
import { ThemeProvider } from './components/theme'
import { LoginPage } from './features/auth/LoginPage'
import { DiscoverShell } from './features/discover/DiscoverShell'
import './i18n'
import './index.css'

import { validateIssuesSearch } from './features/issues/issuesSearch'
import { validateRequestsSearch } from './features/requests/requestsSearch'
const queryClient = new QueryClient({
  defaultOptions: { queries: { retry: false, refetchOnWindowFocus: false } },
})

const rootRoute = createRootRoute({ component: () => <Outlet /> })

const loginRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/login',
  component: LoginPage,
})

const fetchMe = async () => {
  try {
    return await queryClient.fetchQuery({ queryKey: ['me'], queryFn: auth.me, staleTime: 60_000 })
  } catch (error) {
    if (error instanceof ApiError && error.status === 401) {
      throw redirect({ to: '/login' })
    }
    throw error
  }
}

// Every page needs a session (a 401 sends to the login page). The admin gets the full app in its sidebar; anyone else
// gets Discover in a frame of its own, and is sent there from the admin's pages (the API refuses them those anyway).
const shellRoute = createRoute({
  getParentRoute: () => rootRoute,
  id: 'shell',
  beforeLoad: async () => ({ admin: isAdmin(await fetchMe()) }),
  component: function Shell() {
    const { admin } = shellRoute.useRouteContext()
    return admin ? <AppShell /> : <DiscoverShell />
  },
})

const appRoute = createRoute({
  getParentRoute: () => shellRoute,
  id: 'app',
  beforeLoad: ({ context }) => {
    if (!context.admin) {
      throw redirect({ to: '/discover' })
    }
  },
  component: Outlet,
})

// The landing page of R1, kept as an address: it is Discover now.
const welcomeRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/welcome',
  beforeLoad: () => {
    throw redirect({ to: '/discover' })
  },
})

// Discover: every signed-in user. `name` is the genre, studio or network the list was opened from, for its heading.
const optionalNumber = (value: unknown) => (typeof value === 'number' && Number.isFinite(value) ? value : typeof value === 'string' && /^\d+$/.test(value) ? Number(value) : undefined)
const optionalText = (value: unknown) => (typeof value === 'string' && value.trim() ? value : undefined)
const discoverRoute = createRoute({ getParentRoute: () => shellRoute, path: '/discover', component: lazyRouteComponent(() => import('./features/discover/DiscoverPage'), 'DiscoverPage') })
const discoverSearchRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/discover/search',
  validateSearch: (search: Record<string, unknown>): { q?: string } => ({ q: optionalText(search.q) }),
  component: lazyRouteComponent(() => import('./features/discover/DiscoverListPage'), 'DiscoverSearchPage'),
})
const discoverRowRoute = createRoute({ getParentRoute: () => shellRoute, path: '/discover/rows/$sliderId', component: lazyRouteComponent(() => import('./features/discover/DiscoverListPage'), 'DiscoverRowPage') })
const discoverMoviesRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/discover/movies',
  validateSearch: (search: Record<string, unknown>): { genre?: number; studio?: number; keyword?: number; name?: string } => ({
    genre: optionalNumber(search.genre),
    studio: optionalNumber(search.studio),
    keyword: optionalNumber(search.keyword),
    name: optionalText(search.name),
  }),
  component: lazyRouteComponent(() => import('./features/discover/DiscoverListPage'), 'DiscoverMoviesPage'),
})
const discoverSeriesRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/discover/series',
  validateSearch: (search: Record<string, unknown>): { genre?: number; network?: number; keyword?: number; name?: string } => ({
    genre: optionalNumber(search.genre),
    network: optionalNumber(search.network),
    keyword: optionalNumber(search.keyword),
    name: optionalText(search.name),
  }),
  component: lazyRouteComponent(() => import('./features/discover/DiscoverListPage'), 'DiscoverSeriesListPage'),
})
const discoverMovieRoute = createRoute({ getParentRoute: () => shellRoute, path: '/discover/movie/$tmdbId', component: lazyRouteComponent(() => import('./features/discover/TitlePages'), 'DiscoverMoviePage') })
const discoverTvRoute = createRoute({ getParentRoute: () => shellRoute, path: '/discover/tv/$tmdbId', component: lazyRouteComponent(() => import('./features/discover/TitlePages'), 'DiscoverSeriesPage') })
const discoverPersonRoute = createRoute({ getParentRoute: () => shellRoute, path: '/discover/person/$tmdbId', component: lazyRouteComponent(() => import('./features/discover/TitlePages'), 'DiscoverPersonPage') })
// Requests: everyone's own, and everyone's for an admin or a request manager. `?filter=pending&type=series&user=3`.
const requestsRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/requests',
  validateSearch: validateRequestsSearch,
  component: lazyRouteComponent(() => import('./features/requests/RequestsPage'), 'RequestsPage'),
})
// Issues: everyone's own, and everyone's for an admin or a request manager. `?status=open&type=audio&q=matrix`.
const issuesRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/issues',
  validateSearch: validateIssuesSearch,
  component: lazyRouteComponent(() => import('./features/issues/IssuesPage'), 'IssuesPage'),
})
const issueRoute = createRoute({ getParentRoute: () => shellRoute, path: '/issues/$issueId', component: lazyRouteComponent(() => import('./features/issues/IssuePage'), 'IssuePage') })

// The signed-in user's own settings (their Discord id and mentions), whatever their role.
const profileRoute = createRoute({ getParentRoute: () => shellRoute, path: '/profile', component: lazyRouteComponent(() => import('./features/profile/ProfilePage'), 'ProfilePage') })

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
// `?term=alien`: the term an old Add link carried, handed to Discover's search.
const termSearch = (search: Record<string, unknown>): { term?: string } => ({
  term: typeof search.term === 'string' && search.term.trim() ? search.term : undefined,
})
// The old Add pages: adding is done from Discover now. Their addresses (and the term they were opened on) still land there.
const addRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/add',
  validateSearch: termSearch,
  beforeLoad: ({ search }) => {
    throw redirect({ to: '/discover/search', search: { q: search.term } })
  },
})
const movieRoute = createRoute({ getParentRoute: () => appRoute, path: '/movies/$movieId', component: lazyRouteComponent(() => import('./features/movies/MovieDetailPage'), 'MovieDetailPage') })
const seriesLibraryRoute = createRoute({
  getParentRoute: () => appRoute,
  path: '/series',
  // Same links as the film library: `?view=table&preset=continuing`, or `rootFolder=3` from the storage bar.
  validateSearch: (search: Record<string, unknown>): { view?: string; preset?: string; rootFolder?: number } => ({
    view: typeof search.view === 'string' ? search.view : undefined,
    preset: typeof search.preset === 'string' ? search.preset : undefined,
    rootFolder: typeof search.rootFolder === 'number' ? search.rootFolder : undefined,
  }),
  component: lazyRouteComponent(() => import('./features/series/SeriesLibraryPage'), 'SeriesLibraryPage'),
})
const specialsFilmsRoute = createRoute({
  getParentRoute: () => appRoute,
  path: '/library/specials-films',
  // `?files=filmInLibrary` (from the films) or `specialHasFile` (from the series): the filter to open on.
  validateSearch: (search: Record<string, unknown>): { files?: 'all' | 'specialHasFile' | 'filmHasFile' | 'filmInLibrary' } => ({
    files: search.files === 'specialHasFile' || search.files === 'filmHasFile' || search.files === 'filmInLibrary' ? search.files : undefined,
  }),
  component: lazyRouteComponent(() => import('./features/series/SpecialsFilmsPage'), 'SpecialsFilmsPage'),
})
const addSeriesRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/series/add',
  validateSearch: termSearch,
  beforeLoad: ({ search }) => {
    throw redirect({ to: '/discover/search', search: { q: search.term } })
  },
})
const seriesRoute = createRoute({ getParentRoute: () => appRoute, path: '/series/$seriesId', component: lazyRouteComponent(() => import('./features/series/SeriesDetailPage'), 'SeriesDetailPage') })
// `import` opens the header's manual import on arrival: the palette's "Manual import" lands here.
const queueRoute = createRoute({
  getParentRoute: () => appRoute,
  path: '/activity/queue',
  validateSearch: (search: Record<string, unknown>): { import?: 'film' | 'series' } => ({
    import: search.import === 'film' || search.import === 'series' ? search.import : undefined,
  }),
  component: lazyRouteComponent(() => import('./features/activity/QueuePage'), 'QueuePage'),
})
const releasesRoute = createRoute({ getParentRoute: () => appRoute, path: '/activity/releases', component: lazyRouteComponent(() => import('./features/activity/ReleasesPage'), 'ReleasesPage') })
const historyRoute = createRoute({ getParentRoute: () => appRoute, path: '/activity/history', component: lazyRouteComponent(() => import('./features/activity/HistoryPage'), 'HistoryPage') })
const compareRoute = createRoute({ getParentRoute: () => appRoute, path: '/activity/compare', component: lazyRouteComponent(() => import('./features/activity/ComparePage'), 'ComparePage') })
const compareSonarrRoute = createRoute({ getParentRoute: () => appRoute, path: '/activity/compare-sonarr', component: lazyRouteComponent(() => import('./features/activity/SeriesCompare'), 'SonarrComparePage') })
const blocklistRoute = createRoute({ getParentRoute: () => appRoute, path: '/activity/blocklist', component: lazyRouteComponent(() => import('./features/activity/BlocklistPage'), 'BlocklistPage') })
const nowPlayingRoute = createRoute({ getParentRoute: () => appRoute, path: '/playback/now', component: lazyRouteComponent(() => import('./features/playback/NowPlayingPage'), 'NowPlayingPage') })
// `?kind=unmatched`: the film plays linked to no film, as the Tautulli import report links them.
const playsRoute = createRoute({
  getParentRoute: () => appRoute,
  path: '/playback/plays',
  validateSearch: (search: Record<string, unknown>): { kind?: 'movie' | 'episode' | 'unmatched'; q?: string } => ({
    kind: search.kind === 'movie' || search.kind === 'episode' || search.kind === 'unmatched' ? search.kind : undefined,
    q: typeof search.q === 'string' && search.q.trim() ? search.q : undefined,
  }),
  component: lazyRouteComponent(() => import('./features/playback/PlaysPage'), 'PlaysPage'),
})
const playbackStatsRoute = createRoute({ getParentRoute: () => appRoute, path: '/playback/stats', component: lazyRouteComponent(() => import('./features/playback/StatsPage'), 'StatsPage') })
const playbackUsersRoute = createRoute({ getParentRoute: () => appRoute, path: '/playback/users', component: lazyRouteComponent(() => import('./features/playback/UsersPage'), 'UsersPage') })
const playbackUserRoute = createRoute({ getParentRoute: () => appRoute, path: '/playback/users/$userId', component: lazyRouteComponent(() => import('./features/playback/UserPage'), 'UserPage') })
const wantedRoute = createRoute({ getParentRoute: () => appRoute, path: '/wanted', component: lazyRouteComponent(() => import('./features/wanted/WantedPage'), 'WantedPage') })
const seriesWantedRoute = createRoute({ getParentRoute: () => appRoute, path: '/series/wanted', component: lazyRouteComponent(() => import('./features/wanted/SeriesWantedPage'), 'SeriesWantedPage') })
const calendarRoute = createRoute({ getParentRoute: () => appRoute, path: '/calendar', component: lazyRouteComponent(() => import('./features/calendar/CalendarPage'), 'CalendarPage') })
const heldRoute = createRoute({ getParentRoute: () => appRoute, path: '/activity/held', component: lazyRouteComponent(() => import('./features/activity/HeldPage'), 'HeldPage') })
const systemRoute = createRoute({ getParentRoute: () => appRoute, path: '/system/status', component: lazyRouteComponent(() => import('./features/system/SystemPage'), 'SystemPage') })
const librarySettingsRoute = createRoute({ getParentRoute: () => appRoute, path: '/settings/library', component: lazyRouteComponent(() => import('./features/settings/LibrarySettingsPage'), 'LibrarySettingsPage') })
const mediaManagementRoute = createRoute({
  getParentRoute: () => appRoute,
  path: '/settings/mediamanagement',
  // `?tab=series`: the tab to open on (films when absent). The page keeps it in the URL as you switch tabs.
  validateSearch: (search: Record<string, unknown>): { tab?: 'films' | 'series' | 'both' } => ({
    tab: search.tab === 'films' || search.tab === 'series' || search.tab === 'both' ? search.tab : undefined,
  }),
  component: lazyRouteComponent(() => import('./features/settings/MediaManagementPage'), 'MediaManagementPage'),
})
const indexersSettingsRoute = createRoute({ getParentRoute: () => appRoute, path: '/settings/indexers', component: lazyRouteComponent(() => import('./features/settings/IndexersSettingsPage'), 'IndexersSettingsPage') })
const qualitySettingsRoute = createRoute({ getParentRoute: () => appRoute, path: '/settings/quality', component: lazyRouteComponent(() => import('./features/settings/QualitySettingsPage'), 'QualitySettingsPage') })
const customFormatsRoute = createRoute({ getParentRoute: () => appRoute, path: '/settings/customformats', component: lazyRouteComponent(() => import('./features/settings/CustomFormatsPage'), 'CustomFormatsPage') })
const downloadClientsRoute = createRoute({ getParentRoute: () => appRoute, path: '/settings/downloadclients', component: lazyRouteComponent(() => import('./features/settings/DownloadClientsPage'), 'DownloadClientsPage') })
const notificationsRoute = createRoute({ getParentRoute: () => appRoute, path: '/settings/notifications', component: lazyRouteComponent(() => import('./features/settings/NotificationsPage'), 'NotificationsPage') })
const connectRoute = createRoute({ getParentRoute: () => appRoute, path: '/settings/connect', component: lazyRouteComponent(() => import('./features/settings/ConnectPage'), 'ConnectPage') })
const discoverSettingsRoute = createRoute({ getParentRoute: () => appRoute, path: '/settings/discover', component: lazyRouteComponent(() => import('./features/settings/DiscoverSettingsPage'), 'DiscoverSettingsPage') })
const usersRoute = createRoute({ getParentRoute: () => appRoute, path: '/settings/users', component: lazyRouteComponent(() => import('./features/settings/UsersPage'), 'UsersPage') })
const securityRoute = createRoute({ getParentRoute: () => appRoute, path: '/settings/security', component: lazyRouteComponent(() => import('./features/settings/SecurityPage'), 'SecurityPage') })
const mediaServersRoute = createRoute({ getParentRoute: () => appRoute, path: '/settings/mediaservers', component: lazyRouteComponent(() => import('./features/settings/MediaServersPage'), 'MediaServersPage') })
const importRadarrRoute = createRoute({ getParentRoute: () => appRoute, path: '/settings/import-radarr', component: lazyRouteComponent(() => import('./features/settings/ImportRadarrPage'), 'ImportRadarrPage') })
const importTautulliRoute = createRoute({ getParentRoute: () => appRoute, path: '/settings/import-tautulli', component: lazyRouteComponent(() => import('./features/settings/ImportTautulliPage'), 'ImportTautulliPage') })
const importSeerrRoute = createRoute({ getParentRoute: () => appRoute, path: '/settings/import-seerr', component: lazyRouteComponent(() => import('./features/settings/ImportSeerrPage'), 'ImportSeerrPage') })
const followedRadarrsRoute = createRoute({ getParentRoute: () => appRoute, path: '/settings/followed-radarrs', component: lazyRouteComponent(() => import('./features/settings/RadarrFollowPage'), 'RadarrFollowPage') })
const importSonarrRoute = createRoute({ getParentRoute: () => appRoute, path: '/settings/import-sonarr', component: lazyRouteComponent(() => import('./features/settings/ImportSonarrPage'), 'ImportSonarrPage') })
const followedSonarrsRoute = createRoute({ getParentRoute: () => appRoute, path: '/settings/followed-sonarrs', component: lazyRouteComponent(() => import('./features/settings/SonarrFollowPage'), 'SonarrFollowPage') })

const router = createRouter({
  routeTree: rootRoute.addChildren([loginRoute, welcomeRoute, addRoute, addSeriesRoute, shellRoute.addChildren([
    discoverRoute,
    discoverSearchRoute,
    discoverRowRoute,
    discoverMoviesRoute,
    discoverSeriesRoute,
    discoverMovieRoute,
    discoverTvRoute,
    discoverPersonRoute,
    requestsRoute,
    issuesRoute,
    issueRoute,
    profileRoute,
    appRoute.addChildren([
      libraryRoute,
      wantedRoute,
      nowPlayingRoute,
      playsRoute,
      playbackStatsRoute,
      playbackUsersRoute,
      playbackUserRoute,
      movieRoute,
      seriesLibraryRoute,
      specialsFilmsRoute,
      seriesWantedRoute,
      calendarRoute,
      seriesRoute,
      queueRoute,
      heldRoute,
      releasesRoute,
      historyRoute,
      blocklistRoute,
      compareRoute,
      compareSonarrRoute,
      librarySettingsRoute,
      mediaManagementRoute,
      indexersSettingsRoute,
      downloadClientsRoute,
      notificationsRoute,
      connectRoute,
      securityRoute,
      usersRoute,
      discoverSettingsRoute,
      mediaServersRoute,
      importRadarrRoute,
      importTautulliRoute,
      importSeerrRoute,
      followedRadarrsRoute,
      importSonarrRoute,
      followedSonarrsRoute,
      qualitySettingsRoute,
      customFormatsRoute,
      systemRoute,
    ])])]),
})

declare module '@tanstack/react-router' {
  interface Register {
    router: typeof router
  }
}

const root = document.getElementById('root')
if (!root) throw new Error('index.html has no #root element')

ReactDOM.createRoot(root).render(
  <React.StrictMode>
    <ThemeProvider>
      <QueryClientProvider client={queryClient}>
        <RouterProvider router={router} />
      </QueryClientProvider>
    </ThemeProvider>
  </React.StrictMode>,
)
