import React from 'react'
import ReactDOM from 'react-dom/client'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { Outlet, RouterProvider, createRootRoute, createRoute, createRouter, redirect } from '@tanstack/react-router'
import { ApiError, auth } from './api/client'
import { AppShell } from './components/AppShell'
import { ThemeProvider } from './components/theme'
import { ReleasesPage } from './features/activity/ReleasesPage'
import { LoginPage } from './features/auth/LoginPage'
import { LibraryPage } from './features/library/LibraryPage'
import { AddMoviePage } from './features/movies/AddMoviePage'
import { MovieDetailPage } from './features/movies/MovieDetailPage'
import { IndexersSettingsPage } from './features/settings/IndexersSettingsPage'
import { LibrarySettingsPage } from './features/settings/LibrarySettingsPage'
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

const libraryRoute = createRoute({ getParentRoute: () => appRoute, path: '/', component: LibraryPage })
const addRoute = createRoute({ getParentRoute: () => appRoute, path: '/add', component: AddMoviePage })
const movieRoute = createRoute({ getParentRoute: () => appRoute, path: '/movies/$movieId', component: MovieDetailPage })
const releasesRoute = createRoute({ getParentRoute: () => appRoute, path: '/activity/releases', component: ReleasesPage })
const librarySettingsRoute = createRoute({ getParentRoute: () => appRoute, path: '/settings/library', component: LibrarySettingsPage })
const indexersSettingsRoute = createRoute({ getParentRoute: () => appRoute, path: '/settings/indexers', component: IndexersSettingsPage })

const router = createRouter({
  routeTree: rootRoute.addChildren([loginRoute, appRoute.addChildren([libraryRoute, addRoute, movieRoute, releasesRoute, librarySettingsRoute, indexersSettingsRoute])]),
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
