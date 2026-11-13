import { useCallback, useDeferredValue, useEffect, useMemo, useState } from 'react'
import { Link, useNavigate, useSearch } from '@tanstack/react-router'
import { useMutation, useQuery } from '@tanstack/react-query'
import { FolderSearchIcon } from 'lucide-react'
import { Trans, useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ApiError, auth, profilesFor, quality, rootFolders, rootsOfKind, series as seriesApi, tags } from '@/api/client'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { BulkResultsDialog, toastBulk, type BulkReport } from '@/features/library/BulkResults'
import { versionNames, viewMode } from '@/features/library/core'
import { SavedViewsBar } from '@/features/library/LibraryControls'
import { LibraryHeader } from '@/features/library/LibraryHeader'
import { useSelection } from '@/features/library/selection'
import { SeriesBulkToolbar } from './SeriesBulkToolbar'
import { SpecialsFilmsButton } from './SpecialsFilmsButton'
import { SeriesLibraryControls, SeriesPosterGrid, SeriesTable, type SeriesLookups } from './SeriesLibraryViews'
import {
  sameSeriesFilter,
  seriesFilterCount,
  seriesFirstDirection,
  seriesMatchesFilter,
  seriesMatchesSearch,
  seriesPresets,
  seriesRootFolderView,
  seriesSortedByVersion,
  sortSeries,
  toRow,
  useSeriesPrefs,
  type SeriesPrefs,
  type SeriesPreset,
  type SeriesSavedView,
  type SeriesSortKey,
} from './seriesView'

const skeletonGrid = 'grid grid-cols-2 gap-4 sm:grid-cols-3 md:grid-cols-4 xl:grid-cols-5 2xl:grid-cols-6'

/** The film library page on series: views, search, filters, layouts, view options, sort and bulk edit. */
export function SeriesLibraryPage() {
  const { t } = useTranslation()
  const me = useQuery({ queryKey: ['me'], queryFn: auth.me })
  const [prefs, update] = useSeriesPrefs(me.data?.id)
  const library = useQuery({ queryKey: ['series'], queryFn: seriesApi.list })
  const definitions = useQuery({ queryKey: ['qualityDefinitions'], queryFn: quality.definitions, staleTime: 5 * 60_000 })
  const profiles = useQuery({ queryKey: ['qualityProfiles'], queryFn: quality.profiles, select: profilesFor('series') })
  const tagList = useQuery({ queryKey: ['tags'], queryFn: tags.list })
  const roots = useQuery({ queryKey: ['rootFolders'], queryFn: rootFolders.list, select: rootsOfKind('series') })
  const [term, setTerm] = useState('')
  const search = useDeferredValue(term.trim().toLowerCase())
  const [report, setReport] = useState<BulkReport | null>(null)

  const items = useMemo(() => (library.data ?? []).map(toRow), [library.data])
  const names = useMemo(() => versionNames(items), [items])
  const versionList = useMemo(() => names.map((n) => n.name), [names])
  const qualities = useMemo(() => [...new Set(items.flatMap((s) => s.versions.flatMap((v) => v.qualities)))].sort(), [items])
  const networks = useMemo(() => [...new Set(items.map((s) => s.network ?? ''))].sort((a, b) => (a === '' ? 1 : b === '' ? -1 : a.localeCompare(b))), [items])
  const rank = useMemo(() => new Map((definitions.data ?? []).map((d, i) => [d.name, i])), [definitions.data])
  const lookups = useMemo<SeriesLookups>(
    () => ({
      profiles: new Map((profiles.data ?? []).map((p) => [p.id, p.name])),
      tags: new Map((tagList.data ?? []).map((tag) => [tag.id, tag.label])),
      rootFolders: new Map((roots.data ?? []).map((r) => [r.id, r.path])),
    }),
    [profiles.data, tagList.data, roots.data],
  )

  const shown = useMemo(() => {
    const now = Date.now()
    return sortSeries(
      items.filter((s) => seriesMatchesSearch(s, search) && seriesMatchesFilter(s, prefs.filter, now)),
      prefs.sort,
      prefs.direction,
      rank,
    )
  }, [items, search, prefs.filter, prefs.sort, prefs.direction, rank])

  const presetList = useMemo(() => {
    const now = Date.now()
    return seriesPresets(names).map((preset) => ({ ...preset, count: items.reduce((n, s) => n + (seriesMatchesFilter(s, preset.filter, now) ? 1 : 0), 0) }))
  }, [names, items])
  const activeView =
    prefs.savedViews.find((v) => sameSeriesFilter(v.filter, prefs.filter) && v.mode === prefs.mode)?.id ?? presetList.find((p) => sameSeriesFilter(p.filter, prefs.filter))?.id ?? null
  const presetLabel = (preset: SeriesPreset) =>
    preset.version
      ? t('library.presets.missingVersion', { name: preset.version })
      : preset.id === 'continuing' || preset.id === 'ended'
        ? t(`seriesLibrary.presets.${preset.id}`)
        : t(`library.presets.${preset.id as 'all'}`)

  const selection = useSelection(shown)
  const applyPreset = useCallback(
    (preset: SeriesPreset) => update(preset.sort ? { filter: preset.filter, sort: preset.sort.key, direction: preset.sort.direction } : { filter: preset.filter }),
    [update],
  )
  const applySaved = (view: SeriesSavedView) => update({ filter: view.filter, sort: view.sort, direction: view.direction, mode: view.mode })
  const saveView = (name: string) =>
    update((current) => ({
      savedViews: [...current.savedViews, { id: `v${Date.now().toString(36)}`, name, filter: current.filter, sort: current.sort, direction: current.direction, mode: current.mode }],
    }))
  const deleteView = (id: string) => update((current) => ({ savedViews: current.savedViews.filter((v) => v.id !== id) }))
  const onSort = (key: SeriesSortKey) =>
    update((current) => (current.sort === key ? { direction: current.direction === 'asc' ? 'desc' : 'asc' } : { sort: key, direction: seriesFirstDirection(key) }))
  const onReport = (r: BulkReport) => toastBulk(t, r, setReport)

  // `?view=table&preset=continuing` (or `&rootFolder=2` from the storage bar) opens the page on that layout and view, then leaves the URL clean.
  const params = useSearch({ from: '/app/series' })
  const navigate = useNavigate()
  useEffect(() => {
    if ((!params.view && !params.preset && params.rootFolder === undefined) || !library.data) return
    const patch: Partial<SeriesPrefs> = {}
    if (params.view) patch.mode = viewMode(params.view)
    const preset = presetList.find((p) => p.id === params.preset)
    const saved = prefs.savedViews.find((v) => v.id === params.preset)
    if (preset) Object.assign(patch, preset.sort ? { filter: preset.filter, sort: preset.sort.key, direction: preset.sort.direction } : { filter: preset.filter })
    if (saved) Object.assign(patch, { filter: saved.filter, sort: saved.sort, direction: saved.direction, mode: saved.mode })
    if (params.rootFolder !== undefined) Object.assign(patch, seriesRootFolderView(items, params.rootFolder))
    update(patch)
    void navigate({ to: '/series', search: {}, replace: true })
  }, [params.view, params.preset, params.rootFolder, library.data, items, presetList, prefs.savedViews, update, navigate])

  if (library.isPending) {
    return (
      <>
        <PageHeader count={null} />
        <div className={skeletonGrid}>
          {Array.from({ length: 10 }, (_, i) => (
            // biome-ignore lint/suspicious/noArrayIndexKey: static loading placeholders
            <div key={i} className="flex flex-col gap-2">
              <Skeleton className="aspect-[2/3] w-full rounded-xl" />
              <Skeleton className="h-4 w-3/4" />
              <Skeleton className="h-3 w-1/3" />
            </div>
          ))}
        </div>
      </>
    )
  }

  if (library.error) {
    return (
      <Alert variant="destructive">
        <AlertDescription>{t('seriesLibrary.loadError')}</AlertDescription>
      </Alert>
    )
  }

  const filtered = search !== '' || seriesFilterCount(prefs.filter) > 0
  const views = { selecting: selection.selecting, selected: selection.selected, onToggle: selection.onToggle }
  const sortedBy = seriesSortedByVersion(prefs.sort)

  return (
    <>
      <PageHeader
        count={items.length}
        shown={filtered ? shown.length : null}
        selecting={selection.selecting}
        onSelect={items.length > 0 ? (selection.selecting ? selection.stop : selection.start) : undefined}
      />

      {items.length === 0 && (
        <Card className="items-center py-12 text-center">
          <CardContent className="flex flex-col gap-2">
            <p className="font-medium">{t('seriesLibrary.empty')}</p>
            <p className="text-sm text-muted-foreground">
              <Trans i18nKey="seriesLibrary.emptyHint" components={{ add: <Link to="/series/add" className="text-foreground underline underline-offset-4" /> }} />
            </p>
          </CardContent>
        </Card>
      )}

      {items.length > 0 && (
        <div className="flex flex-col gap-3">
          <SavedViewsBar
            presets={presetList}
            presetLabel={presetLabel}
            savedViews={prefs.savedViews}
            activeId={activeView}
            onPreset={applyPreset}
            onSaved={applySaved}
            onSave={saveView}
            onDelete={deleteView}
          />
          <SeriesLibraryControls
            prefs={prefs}
            onChange={update}
            term={term}
            onTermChange={setTerm}
            names={names}
            qualities={qualities}
            networks={networks}
            tags={tagList.data ?? []}
            rootFolders={roots.data ?? []}
          />
        </div>
      )}

      {items.length > 0 && shown.length === 0 && (
        <Card className="items-center py-10 text-center" data-testid="library-no-match">
          <CardContent className="flex flex-col items-center gap-3">
            <p className="text-sm text-muted-foreground">{t('seriesLibrary.noMatch')}</p>
            <Button
              type="button"
              variant="outline"
              size="sm"
              onClick={() => {
                setTerm('')
                applyPreset(presetList[0])
              }}
            >
              {t('library.clearFilters')}
            </Button>
          </CardContent>
        </Card>
      )}

      {shown.length > 0 && prefs.mode === 'posters' && (
        <SeriesPosterGrid
          items={shown}
          posterSize={prefs.posterSize}
          density={prefs.density}
          fields={prefs.cardFields}
          sizeSplit={prefs.sizeSplit}
          emphasis={sortedBy?.metric === 'size' ? sortedBy.version : null}
          {...views}
        />
      )}
      {shown.length > 0 && prefs.mode === 'table' && (
        <SeriesTable
          items={shown}
          columns={prefs.columns}
          density={prefs.density}
          posterSize={prefs.posterSize}
          lookups={lookups}
          sort={prefs.sort}
          direction={prefs.direction}
          onSort={onSort}
          sizeSplit={prefs.sizeSplit}
          versionNames={versionList}
          {...views}
        />
      )}

      {selection.selecting && (
        <SeriesBulkToolbar
          library={items}
          shown={shown}
          filtered={filtered}
          selected={selection.selected}
          onSelect={selection.add}
          onDeselect={selection.remove}
          onClear={selection.clear}
          onDone={selection.stop}
          names={names}
          profiles={profiles.data ?? []}
          tags={tagList.data ?? []}
          onReport={onReport}
        />
      )}

      <BulkResultsDialog report={report} onClose={() => setReport(null)} />
    </>
  )
}

function PageHeader({ count, shown = null, selecting = false, onSelect }: { count: number | null; shown?: number | null; selecting?: boolean; onSelect?: () => void }) {
  const { t } = useTranslation()
  return (
    <LibraryHeader
      title={t('seriesLibrary.title')}
      count={count === null ? null : shown === null ? t('seriesLibrary.count', { count }) : t('seriesLibrary.countShown', { count, shown })}
      countTestId="series-count"
      addTo="/series/add"
      addLabel={t('seriesLibrary.add')}
      selecting={selecting}
      onSelect={onSelect}
    >
      <SpecialsFilmsButton files="specialHasFile" />
      <RescanAllButton />
    </LibraryHeader>
  )
}

/** Queues a scan of every series root folder; progress shows in Settings → Library. */
function RescanAllButton() {
  const { t } = useTranslation()
  const rescan = useMutation({
    mutationFn: seriesApi.rescanAll,
    onSuccess: (queued) =>
      toast.success(t('seriesLibrary.rescanQueued', { count: queued.length }), {
        action: (
          <Link to="/settings/library" className="text-sm underline underline-offset-4">
            {t('seriesLibrary.rescanProgress')}
          </Link>
        ),
      }),
    onError: (error) => toast.error(error instanceof ApiError ? error.message : t('common.unexpectedError')),
  })

  return (
    <Button type="button" variant="outline" onClick={() => rescan.mutate()} disabled={rescan.isPending} data-testid="series-rescan-all">
      <FolderSearchIcon />
      {t('seriesLibrary.rescanAll')}
    </Button>
  )
}
