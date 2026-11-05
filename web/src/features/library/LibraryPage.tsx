import { useCallback, useDeferredValue, useEffect, useMemo, useState } from 'react'
import { Link, useNavigate, useSearch } from '@tanstack/react-router'
import { useQuery } from '@tanstack/react-query'
import { CheckSquareIcon, PlusIcon } from 'lucide-react'
import { Trans, useTranslation } from 'react-i18next'
import { auth, movies, quality, rootFolders, tags } from '@/api/client'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { BulkResultsDialog, toastBulk, type BulkReport } from './BulkResults'
import { BulkToolbar } from './BulkToolbar'
import { LibraryControls, SavedViewsBar } from './LibraryControls'
import {
  activeFilterCount,
  matchesFilter,
  matchesSearch,
  presets,
  sameFilter,
  sortMovies,
  useLibraryPrefs,
  versionNames,
  viewModes,
  type LibraryPrefs,
  type Preset,
  type SavedView,
  type SortKey,
  type ViewMode,
} from './libraryView'
import { MovieTable, type Lookups } from './MovieTable'
import { PosterGrid } from './PosterGrid'
import { useSelection } from './selection'
import { VersionMatrix } from './VersionMatrix'

const skeletonGrid = 'grid grid-cols-2 gap-4 sm:grid-cols-3 md:grid-cols-4 xl:grid-cols-5 2xl:grid-cols-6'

export function LibraryPage() {
  const { t } = useTranslation()
  const me = useQuery({ queryKey: ['me'], queryFn: auth.me })
  const [prefs, update] = useLibraryPrefs(me.data?.id)
  const library = useQuery({ queryKey: ['movies'], queryFn: movies.list })
  const definitions = useQuery({ queryKey: ['qualityDefinitions'], queryFn: quality.definitions, staleTime: 5 * 60_000 })
  const profiles = useQuery({ queryKey: ['qualityProfiles'], queryFn: quality.profiles })
  const tagList = useQuery({ queryKey: ['tags'], queryFn: tags.list })
  const roots = useQuery({ queryKey: ['rootFolders'], queryFn: rootFolders.list })
  const [term, setTerm] = useState('')
  const search = useDeferredValue(term.trim().toLowerCase())
  const [report, setReport] = useState<BulkReport | null>(null)

  const items = useMemo(() => library.data ?? [], [library.data])
  const names = useMemo(() => versionNames(items), [items])
  const qualities = useMemo(() => [...new Set(items.flatMap((m) => m.versions.map((v) => v.quality).filter((q): q is string => q !== null)))].sort(), [items])
  const rank = useMemo(() => new Map((definitions.data ?? []).map((d, i) => [d.name, i])), [definitions.data])
  const lookups = useMemo<Lookups>(
    () => ({
      profiles: new Map((profiles.data ?? []).map((p) => [p.id, p.name])),
      tags: new Map((tagList.data ?? []).map((tag) => [tag.id, tag.label])),
      rootFolders: new Map((roots.data ?? []).map((r) => [r.id, r.path])),
    }),
    [profiles.data, tagList.data, roots.data],
  )

  const shown = useMemo(() => {
    const now = Date.now()
    return sortMovies(
      items.filter((m) => matchesSearch(m, search) && matchesFilter(m, prefs.filter, now)),
      prefs.sort,
      prefs.direction,
      rank,
    )
  }, [items, search, prefs.filter, prefs.sort, prefs.direction, rank])

  const presetList = useMemo(() => {
    const now = Date.now()
    return presets(names).map((preset) => ({ ...preset, count: items.reduce((n, m) => n + (matchesFilter(m, preset.filter, now) ? 1 : 0), 0) }))
  }, [names, items])
  const activeView = prefs.savedViews.find((v) => sameFilter(v.filter, prefs.filter) && v.mode === prefs.mode)?.id ?? presetList.find((p) => sameFilter(p.filter, prefs.filter))?.id ?? null

  const selection = useSelection(shown)
  const applyPreset = useCallback(
    (preset: Preset) => update(preset.sort ? { filter: preset.filter, sort: preset.sort.key, direction: preset.sort.direction } : { filter: preset.filter }),
    [update],
  )
  const applySaved = (view: SavedView) => update({ filter: view.filter, sort: view.sort, direction: view.direction, mode: view.mode })
  const saveView = (name: string) =>
    update((current) => ({
      savedViews: [...current.savedViews, { id: `v${Date.now().toString(36)}`, name, filter: current.filter, sort: current.sort, direction: current.direction, mode: current.mode }],
    }))
  const deleteView = (id: string) => update((current) => ({ savedViews: current.savedViews.filter((v) => v.id !== id) }))
  const onSort = (key: SortKey) => update((current) => (current.sort === key ? { direction: current.direction === 'asc' ? 'desc' : 'asc' } : { sort: key, direction: 'asc' }))
  const onReport = (r: BulkReport) => toastBulk(t, r, setReport)

  // `?view=table&preset=missing-4k` opens the page on that layout and view, then leaves the URL clean.
  const params = useSearch({ from: '/app/' })
  const navigate = useNavigate()
  useEffect(() => {
    if ((!params.view && !params.preset) || !library.data) return
    const patch: Partial<LibraryPrefs> = {}
    if (params.view && (viewModes as readonly string[]).includes(params.view)) patch.mode = params.view as ViewMode
    const preset = presetList.find((p) => p.id === params.preset)
    const saved = prefs.savedViews.find((v) => v.id === params.preset)
    if (preset) Object.assign(patch, preset.sort ? { filter: preset.filter, sort: preset.sort.key, direction: preset.sort.direction } : { filter: preset.filter })
    if (saved) Object.assign(patch, { filter: saved.filter, sort: saved.sort, direction: saved.direction, mode: saved.mode })
    update(patch)
    void navigate({ to: '/', search: {}, replace: true })
  }, [params.view, params.preset, library.data, presetList, prefs.savedViews, update, navigate])

  if (library.isPending) {
    return (
      <>
        <PageHeader count={null} />
        <div className={skeletonGrid}>
          {Array.from({ length: 10 }, (_, i) => (
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
        <AlertDescription>{t('library.loadError')}</AlertDescription>
      </Alert>
    )
  }

  const filtered = search !== '' || activeFilterCount(prefs.filter) > 0
  const views = { selecting: selection.selecting, selected: selection.selected, onToggle: selection.onToggle }

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
            <p className="font-medium">{t('library.empty')}</p>
            <p className="text-sm text-muted-foreground">
              <Trans
                i18nKey="library.emptyHint"
                components={{
                  add: <Link to="/add" className="text-foreground underline underline-offset-4" />,
                  scan: <Link to="/settings/library" className="text-foreground underline underline-offset-4" />,
                }}
              />
            </p>
          </CardContent>
        </Card>
      )}

      {items.length > 0 && (
        <div className="flex flex-col gap-3">
          <SavedViewsBar
            presets={presetList}
            savedViews={prefs.savedViews}
            activeId={activeView}
            onPreset={applyPreset}
            onSaved={applySaved}
            onSave={saveView}
            onDelete={deleteView}
          />
          <LibraryControls
            prefs={prefs}
            onChange={update}
            term={term}
            onTermChange={setTerm}
            names={names}
            qualities={qualities}
            tags={tagList.data ?? []}
            rootFolders={roots.data ?? []}
          />
        </div>
      )}

      {items.length > 0 && shown.length === 0 && (
        <Card className="items-center py-10 text-center" data-testid="library-no-match">
          <CardContent className="flex flex-col items-center gap-3">
            <p className="text-sm text-muted-foreground">{t('library.noMatch')}</p>
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

      {shown.length > 0 && prefs.mode === 'posters' && <PosterGrid movies={shown} posterSize={prefs.posterSize} density={prefs.density} fields={prefs.cardFields} {...views} />}
      {shown.length > 0 && prefs.mode === 'table' && (
        <MovieTable movies={shown} columns={prefs.columns} density={prefs.density} lookups={lookups} sort={prefs.sort} direction={prefs.direction} onSort={onSort} {...views} />
      )}
      {shown.length > 0 && prefs.mode === 'matrix' && (
        <VersionMatrix movies={shown} names={names} density={prefs.density} onSelectWhere={(predicate) => selection.add(shown.filter(predicate).map((m) => m.id))} {...views} />
      )}

      {selection.selecting && (
        <BulkToolbar
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
    <div className="flex flex-wrap items-center gap-x-3 gap-y-2">
      <h1 className="text-2xl font-semibold tracking-tight">{t('library.title')}</h1>
      {count !== null && (
        <span className="text-sm text-muted-foreground tabular-nums" data-testid="library-count">
          {shown === null ? t('library.count', { count }) : t('library.countShown', { count, shown })}
        </span>
      )}
      <div className="ml-auto flex items-center gap-2">
        {onSelect && (
          <Button type="button" variant={selecting ? 'secondary' : 'outline'} onClick={onSelect} aria-pressed={selecting} data-testid="library-select-toggle">
            <CheckSquareIcon />
            {selecting ? t('bulk.done') : t('library.select')}
          </Button>
        )}
        <Button asChild>
          <Link to="/add">
            <PlusIcon />
            {t('library.addMovie')}
          </Link>
        </Button>
      </div>
    </div>
  )
}
