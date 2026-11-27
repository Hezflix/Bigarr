import { useQueryClient } from '@tanstack/react-query'
import { useNavigate } from '@tanstack/react-router'
import {
  ArchiveIcon,
  FolderInputIcon,
  FolderSearchIcon,
  HeartPulseIcon,
  ListOrderedIcon,
  MonitorIcon,
  MoonIcon,
  RefreshCwIcon,
  RssIcon,
  SearchIcon,
  SunIcon,
} from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ApiError, backups, health, rootFolders, rootsOfKind, rss, series, seriesWanted, tasks, wanted } from '@/api/client'
import { useTheme } from '@/components/theme'
import type { PaletteAction } from './actions'

/** The palette's actions available on every page: the buttons of Wanted, Releases, System and the library, in one place. */
export function useGlobalActions(): PaletteAction[] {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  const { theme, resolvedTheme, setTheme } = useTheme()

  const failed = (e: unknown) => (e instanceof ApiError ? e.message : t('common.unexpectedError'))
  const invalidate = (...keys: string[]) => {
    for (const key of keys) void queryClient.invalidateQueries({ queryKey: [key] })
  }
  /** Runs a scheduled task now: it can take minutes, so the toast says so until it ends. */
  const runTask = (name: string, loading: string) =>
    toast.promise(
      tasks.run(name).then((result) => {
        invalidate('tasks', 'movies', 'series', 'health')
        if (!result.success) throw new Error(t('tasks.failed', { name: result.name, error: result.error ?? '' }))
        return result
      }),
      {
        loading,
        success: (result) => t('tasks.ran', { name: result.name, summary: result.summary ?? '' }),
        error: failed,
      },
    )

  const actions: PaletteAction[] = [
    {
      id: 'search-missing-movies',
      label: t('palette.actions.searchMissingMovies'),
      icon: SearchIcon,
      keywords: ['wanted', 'missing', 'films'],
      confirm: { title: t('palette.actions.searchMissingMovies'), description: t('palette.confirm.searchMissingMovies') },
      run: () =>
        wanted.searchAll('missing').then(
          (result) => toast.success(t('wanted.queued', { count: result.queued })),
          (e) => toast.error(failed(e)),
        ),
    },
    {
      id: 'search-missing-episodes',
      label: t('palette.actions.searchMissingEpisodes'),
      icon: SearchIcon,
      keywords: ['wanted', 'missing', 'series', 'episodes'],
      confirm: { title: t('palette.actions.searchMissingEpisodes'), description: t('palette.confirm.searchMissingEpisodes') },
      run: () =>
        seriesWanted.search(undefined, 'missing').then(
          (result) => {
            toast.success(t('wanted.queued', { count: result.queued }))
            invalidate('seriesWanted')
          },
          (e) => toast.error(failed(e)),
        ),
    },
    {
      id: 'rss-sync',
      label: t('palette.actions.rssSync'),
      icon: RssIcon,
      keywords: ['releases', 'feed'],
      run: () =>
        toast.promise(
          rss.sync().finally(() => invalidate('rss', 'queue')),
          {
            loading: t('rss.syncing'),
            success: (result) =>
              result.picked > 0
                ? t('rss.syncedShadow', { fetched: result.fetched, new: result.new, matched: result.matched, picked: result.picked })
                : t('rss.synced', { fetched: result.fetched, new: result.new, matched: result.matched, grabbed: result.grabbed }),
            error: (e) => (e instanceof ApiError ? e.message : t('rss.syncFailed')),
          },
        ),
    },
    {
      id: 'rescan-movie-folders',
      label: t('palette.actions.rescanMovies'),
      icon: FolderSearchIcon,
      keywords: ['scan', 'library', 'disk', 'root'],
      confirm: { title: t('palette.actions.rescanMovies'), description: t('palette.confirm.rescanMovies') },
      run: async () => {
        try {
          const roots = rootsOfKind('movies')(await rootFolders.list())
          await Promise.all(roots.map((root) => rootFolders.scan(root.id)))
          toast.success(t('palette.done.rescanMovies', { count: roots.length }))
          invalidate('rootFolders')
        } catch (e) {
          toast.error(failed(e))
        }
      },
    },
    {
      id: 'rescan-series-folders',
      label: t('palette.actions.rescanSeries'),
      icon: FolderSearchIcon,
      keywords: ['scan', 'library', 'disk', 'root'],
      confirm: { title: t('palette.actions.rescanSeries'), description: t('palette.confirm.rescanSeries') },
      run: () =>
        series.rescanAll().then(
          (queued) => toast.success(t('seriesLibrary.rescanQueued', { count: queued.length })),
          (e) => toast.error(failed(e)),
        ),
    },
    {
      id: 'refresh-movie-metadata',
      label: t('palette.actions.refreshMovies'),
      icon: RefreshCwIcon,
      keywords: ['tmdb', 'metadata', 'library'],
      confirm: { title: t('palette.actions.refreshMovies'), description: t('palette.confirm.refreshMovies') },
      run: () => runTask('metadataRefresh', t('palette.running.refreshMovies')),
    },
    {
      id: 'refresh-series-metadata',
      label: t('palette.actions.refreshSeries'),
      icon: RefreshCwIcon,
      keywords: ['tvdb', 'tmdb', 'metadata', 'episodes'],
      confirm: { title: t('palette.actions.refreshSeries'), description: t('palette.confirm.refreshSeries') },
      run: () => runTask('seriesRefresh', t('palette.running.refreshSeries')),
    },
    {
      id: 'health-check',
      label: t('palette.actions.healthCheck'),
      icon: HeartPulseIcon,
      keywords: ['status', 'system', 'issues'],
      run: () =>
        health.check().then(
          (result) => {
            queryClient.setQueryData(['health'], result)
            const issues = result.issues.filter((issue) => issue.type !== 'notice').length
            if (issues === 0) toast.success(t('health.allGood'))
            else toast.warning(t('health.badge', { count: issues }), { action: { label: t('nav.systemStatus'), onClick: () => void navigate({ to: '/system/status' }) } })
          },
          (e) => toast.error(failed(e)),
        ),
    },
    {
      id: 'create-backup',
      label: t('palette.actions.backup'),
      icon: ArchiveIcon,
      keywords: ['backup', 'database', 'zip'],
      run: () =>
        backups.create().then(
          (backup) => {
            toast.success(t('backups.created', { name: backup.name }))
            invalidate('backups', 'tasks')
          },
          (e) => toast.error(e instanceof ApiError ? e.message : t('backups.createFailed')),
        ),
    },
    {
      id: 'manual-import-films',
      label: t('palette.actions.manualImportFilms'),
      icon: FolderInputIcon,
      keywords: ['import', 'files', 'movies', 'manual'],
      run: () => void navigate({ to: '/activity/queue', search: { import: 'film' } }),
    },
    {
      id: 'manual-import-series',
      label: t('palette.actions.manualImportSeries'),
      icon: FolderInputIcon,
      keywords: ['import', 'files', 'episodes', 'manual'],
      run: () => void navigate({ to: '/activity/queue', search: { import: 'series' } }),
    },
    {
      id: 'open-queue',
      label: t('palette.actions.openQueue'),
      icon: ListOrderedIcon,
      keywords: ['activity', 'downloads'],
      run: () => void navigate({ to: '/activity/queue' }),
    },
    {
      id: 'theme-toggle',
      label: t(resolvedTheme === 'dark' ? 'palette.actions.themeLight' : 'palette.actions.themeDark'),
      icon: resolvedTheme === 'dark' ? SunIcon : MoonIcon,
      keywords: ['theme', 'appearance', 'mode', 'dark', 'light'],
      run: () => setTheme(resolvedTheme === 'dark' ? 'light' : 'dark'),
    },
  ]
  if (theme !== 'system') {
    actions.push({
      id: 'theme-system',
      label: t('palette.actions.themeSystem'),
      icon: MonitorIcon,
      keywords: ['theme', 'appearance', 'mode', 'auto'],
      run: () => setTheme('system'),
    })
  }
  return actions
}
