import type { KeyboardEvent, ReactNode } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useNavigate, useSearch } from '@tanstack/react-router'
import { cn } from 'cn'
import { FilmIcon, HistoryIcon, InboxIcon, type LucideIcon, TvIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { migration, seerrImport, sonarrMigration, tautulliImport, type RadarrImportState } from '@/api/client'
import { ImportRadarrPage } from '@/features/settings/ImportRadarrPage'
import { ImportSeerrPage } from '@/features/settings/ImportSeerrPage'
import { ImportSonarrPage } from '@/features/settings/ImportSonarrPage'
import { ImportTautulliPage } from '@/features/settings/ImportTautulliPage'
import { importSources, importTone, type ImportSource, type ImportTone } from '@/features/settings/importTabs'

const icons: Record<ImportSource, LucideIcon> = { radarr: FilmIcon, sonarr: TvIcon, tautulli: HistoryIcon, seerr: InboxIcon }

/** The colour of a tab's icon is the state of its last import. */
const toneStyle: Record<ImportTone, string> = {
  idle: 'border-border bg-muted text-muted-foreground',
  running: 'border-sky-500/40 bg-sky-500/10 text-sky-700 dark:text-sky-300 [&>svg]:animate-pulse',
  done: 'border-emerald-500/40 bg-emerald-500/10 text-emerald-700 dark:text-emerald-300',
  failed: 'border-destructive/40 bg-destructive/10 text-destructive',
}

/**
 * Each import's status, under the query key its tab's page polls with: the page and the tab share one cache, so the
 * icon turns blue the moment a run starts and green when it completes. Polled here too, while a run is on.
 */
const statusQueries: Record<ImportSource, { key: string; fn: () => Promise<{ state: RadarrImportState }> }> = {
  radarr: { key: 'radarrImport', fn: migration.status },
  sonarr: { key: 'sonarrImport', fn: sonarrMigration.status },
  tautulli: { key: 'tautulliImport', fn: tautulliImport.status },
  seerr: { key: 'seerrImport', fn: seerrImport.status },
}

const pages: Record<ImportSource, () => ReactNode> = {
  radarr: () => <ImportRadarrPage />,
  sonarr: () => <ImportSonarrPage />,
  tautulli: () => <ImportTautulliPage />,
  seerr: () => <ImportSeerrPage />,
}

const tabId = (source: ImportSource) => `import-tab-${source}`
const panelId = (source: ImportSource) => `import-panel-${source}`

/**
 * Settings → Import: moving in from Radarr, Sonarr, Tautulli and Seerr, one tab each. Only the open tab is rendered;
 * an import runs on the server, so switching tabs never stops it.
 */
export function ImportPage() {
  const { t } = useTranslation()
  // The open tab lives in the URL (`?tab=sonarr`), so a link opens the right one; Radarr, the default, keeps it bare.
  const tab: ImportSource = useSearch({ from: '/shell/app/settings/import' }).tab ?? 'radarr'
  const navigate = useNavigate()
  const select = (source: ImportSource) => void navigate({ to: '/settings/import', search: source === 'radarr' ? {} : { tab: source }, replace: true })

  const onKeyDown = (e: KeyboardEvent<HTMLDivElement>) => {
    const index = importSources.indexOf(tab)
    const count = importSources.length
    const next = e.key === 'ArrowRight' ? (index + 1) % count : e.key === 'ArrowLeft' ? (index + count - 1) % count : e.key === 'Home' ? 0 : e.key === 'End' ? count - 1 : -1
    if (next < 0) return
    e.preventDefault()
    select(importSources[next])
    document.getElementById(tabId(importSources[next]))?.focus()
  }

  return (
    <>
      <h1 className="text-2xl font-semibold tracking-tight">{t('import.title')}</h1>
      <p className="max-w-3xl text-sm text-muted-foreground">{t('import.intro')}</p>
      <div role="tablist" aria-label={t('import.title')} onKeyDown={onKeyDown} className="grid grid-cols-4 gap-1 rounded-lg border bg-muted/40 p-1" data-testid="import-tabs">
        {importSources.map((source) => (
          <ImportTab key={source} source={source} selected={source === tab} onSelect={() => select(source)} />
        ))}
      </div>
      <section id={panelId(tab)} role="tabpanel" aria-labelledby={tabId(tab)} className="flex flex-col gap-6" data-testid={`import-panel-${tab}`}>
        {pages[tab]()}
      </section>
    </>
  )
}

function ImportTab({ source, selected, onSelect }: { source: ImportSource; selected: boolean; onSelect: () => void }) {
  const { t } = useTranslation()
  const query = statusQueries[source]
  const status = useQuery({
    queryKey: [query.key],
    queryFn: query.fn,
    refetchInterval: (q) => (importTone(q.state.data?.state) === 'running' ? 1000 : false),
  })
  const tone = importTone(status.data?.state)
  const Icon = icons[source]
  const state = t(`import.state.${tone}`)
  return (
    <button
      type="button"
      role="tab"
      id={tabId(source)}
      aria-selected={selected}
      aria-controls={panelId(source)}
      tabIndex={selected ? 0 : -1}
      onClick={onSelect}
      title={state}
      className={cn(
        'flex min-w-0 flex-col items-center justify-center gap-1 rounded-md px-2 py-2 text-center text-xs font-medium leading-tight transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring sm:flex-row sm:gap-2 sm:text-sm',
        selected ? 'bg-background shadow-sm' : 'text-muted-foreground hover:bg-background/60 hover:text-foreground',
      )}
      data-testid={`import-tab-${source}`}
      data-tone={tone}
    >
      <span className={cn('flex size-6 shrink-0 items-center justify-center rounded-md border [&>svg]:size-3.5', toneStyle[tone])}>
        <Icon />
      </span>
      <span className="min-w-0">{t(`import.sources.${source}`)}</span>
      <span className="sr-only">{state}</span>
    </button>
  )
}
