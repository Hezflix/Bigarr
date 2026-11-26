import { useEffect, useMemo, useRef, useState, type MouseEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { cn } from 'cn'
import { ChevronDownIcon, XIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import {
  ApiError,
  series as seriesApi,
  seriesFiles,
  type Episode,
  type SeriesManualImportFile,
  type SeriesManualImportItem,
  type SeriesManualImportResult,
  type SeriesSummary,
} from '@/api/client'
import { episodeCode } from '@/lib/episodeCode'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { type BulkAction, SeriesBatchBar, SeriesSelectionBar, VersionSelect } from '@/features/import/SeriesImportBars'
import { mostCommon, moveToSeason, range, sharedSeries } from '@/features/import/seriesImportModel'
import { TitlePicker } from '@/features/import/TitlePicker'
import { titleFromFileName } from '@/features/import/titleGuess'
import { ImportFileCell, ImportFooter, ImportListingState, ImportPathForm, ImportQualityCell, ImportReleaseGroupCell, type TransferChoice } from '@/features/import/manualImportParts'
import { ReasonList } from '@/features/search/ReasonsDisplay'

/** `selected`: the row is ticked; it is imported once it also has a version and episodes. */
type Selection = { selected: boolean; seriesId: number | null; versionId: number | null; episodeIds: number[]; qualityId: number; extras: string[]; releaseGroup: string | null }

type Batch = { seriesId: number | null; versionId: number | null }

const code = (e: { seasonNumber: number; episodeNumber: number }) => episodeCode(e.seasonNumber, e.episodeNumber)

const complete = (s: Selection) => s.versionId !== null && s.episodeIds.length > 0

const defaultVersion = (show: SeriesSummary | undefined) => show?.versions.find((v) => v.isDefault)?.id ?? show?.versions[0]?.id ?? null

/**
 * Manual import for series (Sonarr's interactive import): the files under a path or behind a queued download, each with
 * the series, version and episodes the mapper proposes; the user assigns what is missing or wrong and imports, forced.
 * The bar above the table holds the series and version every file goes to (a pack is one show in one version); a row
 * shows its own only where it differs. Ticked rows can be edited together and renumbered.
 */
export function SeriesManualImportDialog({ grabId, path: initialPath, seriesId, title, onClose }: { grabId?: number; path?: string; seriesId?: number; title?: string; onClose: () => void }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [path, setPath] = useState(initialPath ?? '')
  const [loadedPath, setLoadedPath] = useState<string | null>(initialPath ?? null)
  const [selections, setSelections] = useState<Record<string, Selection>>({})
  const [batch, setBatch] = useState<Batch>({ seriesId: null, versionId: null })
  // Rows whose series or version the user opened ("other…") while still equal to the bar's.
  const [opened, setOpened] = useState<Set<string>>(new Set())
  const [mode, setMode] = useState<TransferChoice>('default')
  const [results, setResults] = useState<SeriesManualImportResult[] | null>(null)
  const lastTicked = useRef<string | null>(null)

  const listing = useQuery({
    queryKey: ['seriesManualImport', grabId ?? null, grabId ? null : loadedPath, seriesId ?? null],
    queryFn: () => seriesFiles.list(grabId ? { grabId } : { path: loadedPath ?? '', seriesId }),
    enabled: !!grabId || !!loadedPath,
    retry: false,
    staleTime: Number.POSITIVE_INFINITY,
  })
  const library = useQuery({ queryKey: ['series'], queryFn: seriesApi.list })
  const sortedSeries = useMemo(() => [...(library.data ?? [])].sort((a, b) => a.sortTitle.localeCompare(b.sortTitle)), [library.data])

  // The mapper's proposals seed the form once per listing. The bar takes the series most files went to, and its
  // version; a file the mapper placed nowhere goes there too, waiting for its episodes.
  useEffect(() => {
    if (!listing.data) return
    const files = listing.data.files
    const shared = mostCommon(files.map((f) => f.seriesId))
    const sharedVersion = mostCommon(files.filter((f) => f.seriesId === shared).map((f) => f.versionId))
    setBatch({ seriesId: shared, versionId: sharedVersion })
    setSelections(
      Object.fromEntries(
        files.map((f) => {
          const placed = f.seriesId !== null
          return [
            f.path,
            {
              selected: !f.isSample && placed && f.versionId !== null && f.episodes.length > 0 && f.rejections.length === 0,
              seriesId: placed ? f.seriesId : shared,
              versionId: placed ? f.versionId : sharedVersion,
              episodeIds: f.episodes.map((e) => e.id),
              qualityId: f.qualityId,
              extras: f.extras.map((e) => e.path),
              releaseGroup: null,
            } satisfies Selection,
          ]
        }),
      ),
    )
    setOpened(new Set())
    setResults(null)
  }, [listing.data])

  const files = listing.data?.files ?? []
  const byPath = (p: string) => files.find((f) => f.path === p)

  /** A row moved to another series: the version the bar holds for it, else the series' default; the file's own episodes only on its own series. */
  const toSeries = (file: SeriesManualImportFile, current: Selection, id: number | null): Selection => {
    const version = id === file.seriesId && file.versionId !== null ? file.versionId : id !== null && id === batch.seriesId ? batch.versionId : defaultVersion(sortedSeries.find((x) => x.id === id))
    const episodeIds = id === file.seriesId ? file.episodes.map((e) => e.id) : id === current.seriesId ? current.episodeIds : []
    const next = { ...current, seriesId: id, versionId: version, episodeIds }
    return { ...next, selected: current.selected && complete(next) }
  }

  const items: SeriesManualImportItem[] = Object.entries(selections).flatMap(([filePath, s]) =>
    !s.selected || !complete(s) || s.versionId === null
      ? []
      : [{ path: filePath, versionId: s.versionId, episodeIds: s.episodeIds, qualityId: s.qualityId, grabId: grabId ?? null, extraPaths: s.extras, releaseGroup: s.releaseGroup }],
  )

  const run = useMutation({
    mutationFn: () => seriesFiles.import(items, mode === 'default' ? null : mode),
    onSuccess: async (outcome) => {
      setResults(outcome)
      const ok = outcome.filter((r) => r.imported).length
      if (ok > 0) toast.success(t('seriesImport.importedCount', { count: ok }))
      for (const r of outcome.filter((r) => !r.imported)) toast.error(t('manualImport.failed', { reason: r.reason }))
      await queryClient.invalidateQueries({ queryKey: ['queue'] })
      await queryClient.invalidateQueries({ queryKey: ['series'] })
      await queryClient.invalidateQueries({ queryKey: ['seriesFiles'] })
      await queryClient.invalidateQueries({ queryKey: ['episodes'] })
    },
    onError: (e) => toast.error(e instanceof ApiError ? e.message : t('manualImport.importFailed')),
  })

  const update = (filePath: string, patch: Partial<Selection>) => setSelections((s) => ({ ...s, [filePath]: { ...s[filePath], ...patch } }))
  const open = (key: string) => setOpened((o) => new Set(o).add(key))

  /** The bar's series: every row that followed the bar (or had none) follows it to the new one. */
  const onBatchSeries = (id: number | null) => {
    const version = id === null ? null : defaultVersion(sortedSeries.find((x) => x.id === id))
    setSelections((all) =>
      Object.fromEntries(
        Object.entries(all).map(([p, s]) => {
          const file = byPath(p)
          if (!file || (s.seriesId !== batch.seriesId && s.seriesId !== null)) return [p, s]
          const moved = toSeries(file, s, id)
          return [p, id === file.seriesId || version === null ? moved : { ...moved, versionId: version }]
        }),
      ),
    )
    setBatch({ seriesId: id, versionId: version })
  }

  /** The bar's version: the rows of the bar's series that had its version take the new one. */
  const onBatchVersion = (versionId: number) => {
    setSelections((all) =>
      Object.fromEntries(Object.entries(all).map(([p, s]) => [p, s.seriesId === batch.seriesId && s.versionId === batch.versionId ? { ...s, versionId } : s])),
    )
    setBatch((b) => ({ ...b, versionId }))
  }

  const ticked = files.filter((f) => selections[f.path]?.selected)
  const onTick = (filePath: string, event: MouseEvent) => {
    const on = !selections[filePath]?.selected
    const paths = event.shiftKey && lastTicked.current ? range(files.map((f) => f.path), lastTicked.current, filePath) : [filePath]
    setSelections((all) => ({ ...all, ...Object.fromEntries(paths.map((p) => [p, { ...all[p], selected: on }])) }))
    lastTicked.current = filePath
  }
  const allTicked = files.length > 0 && ticked.length === files.length
  const tickAll = (on: boolean) => setSelections((all) => Object.fromEntries(Object.entries(all).map(([p, s]) => [p, { ...s, selected: on }])))

  const shared = sharedSeries(ticked.map((f) => selections[f.path]), batch.seriesId)
  // Renumbering starts in the season the untouched files are in, after the last episode they hold.
  const held = files.filter((f) => !selections[f.path]?.selected).flatMap((f) => f.episodes)
  const hintSeason = mostCommon(files.flatMap((f) => f.episodes.map((e) => e.seasonNumber)))
  const hint = { season: hintSeason, start: Math.max(0, ...held.filter((e) => e.seasonNumber === hintSeason).map((e) => e.episodeNumber)) + 1 }

  const onBulk = async (action: BulkAction) => {
    const paths = new Set(ticked.map((f) => f.path))
    // Moving to a season needs the series' episodes: the bar loaded them already, the cache answers.
    const episodes = action.kind === 'season' && shared !== null ? await queryClient.fetchQuery({ queryKey: ['episodes', shared], queryFn: () => seriesApi.episodes(shared) }) : []
    setSelections((all) =>
      Object.fromEntries(
        Object.entries(all).map(([p, s]) => {
          const file = byPath(p)
          if (!paths.has(p) || !file) return [p, s]
          switch (action.kind) {
            case 'series':
              return [p, { ...toSeries(file, s, action.seriesId), selected: true }]
            case 'season': {
              const moved = moveToSeason(s.episodeIds, episodes, action.season)
              return [p, moved === null ? s : { ...s, episodeIds: moved }]
            }
            case 'version':
              return [p, { ...s, versionId: action.versionId }]
            case 'quality':
              return [p, { ...s, qualityId: action.qualityId }]
            case 'group':
              return [p, { ...s, releaseGroup: action.group }]
            case 'renumber': {
              const r = action.result.find((x) => x.path === p)
              return [p, r?.episodeId == null ? s : { ...s, seriesId: s.seriesId ?? shared, versionId: s.versionId ?? batch.versionId, episodeIds: [r.episodeId] }]
            }
          }
          return [p, s]
        }),
      ),
    )
  }

  const batchShow = sortedSeries.find((x) => x.id === batch.seriesId)
  const versionName = (show: SeriesSummary | undefined, id: number | null) => show?.versions.find((v) => v.id === id)?.name

  return (
    <Dialog open onOpenChange={(next) => !next && onClose()}>
      <DialogContent className="max-h-[90vh] overflow-y-auto sm:max-w-6xl" data-testid="series-manual-import">
        <DialogHeader>
          <DialogTitle>{title ? t('manualImport.titleFor', { title }) : t('seriesImport.title')}</DialogTitle>
          <DialogDescription>{t('seriesImport.hint')}</DialogDescription>
        </DialogHeader>

        {!grabId && <ImportPathForm id="smi-path" path={path} onPath={setPath} placeholder={t('seriesImport.pathPlaceholder')} loading={listing.isFetching} onLoad={setLoadedPath} />}

        <ImportListingState fetching={listing.isFetching} error={listing.error} empty={!!listing.data && files.length === 0} />

        {listing.data && files.length > 0 && (
          <>
            <SeriesBatchBar
              library={sortedSeries}
              seriesId={batch.seriesId}
              versionId={batch.versionId}
              guess={{ title: titleFromFileName(files[0].name), first: [] }}
              onSeries={onBatchSeries}
              onVersion={onBatchVersion}
            />
            {ticked.length > 0 && (
              <SeriesSelectionBar
                rows={ticked.map((f) => ({ path: f.path, name: f.name }))}
                incomplete={ticked.filter((f) => !complete(selections[f.path])).length}
                shared={shared}
                library={sortedSeries}
                hint={hint}
                onAction={(a) => void onBulk(a)}
                onClear={() => tickAll(false)}
              />
            )}
            <div className="overflow-x-auto rounded-lg border">
              <Table data-testid="series-manual-import-files">
                <TableHeader>
                  <TableRow>
                    <TableHead className="w-8">
                      <Checkbox checked={allTicked ? true : ticked.length > 0 ? 'indeterminate' : false} onCheckedChange={(v) => tickAll(v === true)} aria-label={t('seriesImport.bulk.selectAll')} />
                    </TableHead>
                    <TableHead>{t('manualImport.file')}</TableHead>
                    <TableHead>{t('manualImport.quality')}</TableHead>
                    <TableHead>{t('manualImport.releaseGroup')}</TableHead>
                    <TableHead>{t('seriesImport.series')}</TableHead>
                    <TableHead>{t('manualImport.version')}</TableHead>
                    <TableHead>{t('seriesImport.episodes')}</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {files.map((file) => {
                    const selection = selections[file.path]
                    if (!selection) return null
                    const show = sortedSeries.find((s) => s.id === selection.seriesId)
                    const result = results?.find((r) => r.path === file.path)
                    const ownSeries = selection.seriesId !== batch.seriesId || opened.has(`${file.path}:series`)
                    const ownVersion = selection.seriesId !== batch.seriesId || selection.versionId !== batch.versionId || opened.has(`${file.path}:version`)
                    return (
                      <TableRow key={file.path} data-testid="series-manual-import-file" className={cn(!selection.selected && 'text-muted-foreground', selection.selected && 'bg-primary/5')}>
                        <TableCell className="align-top">
                          <Checkbox checked={selection.selected} onClick={(e) => onTick(file.path, e)} aria-label={t('manualImport.select')} />
                        </TableCell>
                        <ImportFileCell
                          file={file}
                          selected={selection.selected}
                          extras={selection.extras}
                          onExtras={(extras) => update(file.path, { extras })}
                          extrasLabel={t('seriesImport.extras', { count: file.extras.length })}
                          result={result}
                          className="max-w-[22rem]"
                        >
                          {file.rejections.length > 0 && selection.seriesId === file.seriesId && (
                            <ReasonList reasons={file.rejections} temporary className="mt-1 max-w-[20rem] text-xs" />
                          )}
                        </ImportFileCell>
                        <ImportQualityCell value={selection.qualityId} onChange={(qualityId) => update(file.path, { qualityId })} />
                        <ImportReleaseGroupCell value={selection.releaseGroup} detected={file.releaseGroup} onChange={(releaseGroup) => update(file.path, { releaseGroup })} fileName={file.relativePath} />
                        <TableCell className="align-top">
                          {ownSeries ? (
                            <span className="flex items-start gap-1">
                              <TitlePicker
                                kind="series"
                                items={sortedSeries}
                                value={selection.seriesId}
                                guess={{ title: titleFromFileName(file.name), year: null, first: batch.seriesId === null ? [] : [batch.seriesId] }}
                                testId="series-manual-import-series"
                                onChange={(id) => update(file.path, toSeries(file, selection, id))}
                              />
                              {batch.seriesId !== null && selection.seriesId !== batch.seriesId && (
                                <Button type="button" variant="ghost" size="icon" className="size-8" onClick={() => update(file.path, toSeries(file, selection, batch.seriesId))} aria-label={t('seriesImport.batch.reset')} title={t('seriesImport.batch.reset')}>
                                  <XIcon />
                                </Button>
                              )}
                            </span>
                          ) : (
                            <FollowsBar onOther={() => open(`${file.path}:series`)} testId="series-manual-import-series-same" />
                          )}
                          {/* The mapper's word on its own guess only: a row that now follows the bar is past it. */}
                          {file.matchReason && selection.episodeIds.length === 0 && selection.seriesId === file.seriesId && (
                            <span className="mt-1 block max-w-64 text-xs text-muted-foreground" data-testid="series-manual-import-reason">
                              {file.matchReason}
                            </span>
                          )}
                        </TableCell>
                        <TableCell className="align-top">
                          {ownVersion ? (
                            <span className="flex items-start gap-1">
                              <VersionSelect show={show} value={selection.versionId} onChange={(versionId) => update(file.path, { versionId })} placeholder={t('manualImport.noVersion')} />
                              {selection.seriesId === batch.seriesId && selection.versionId !== batch.versionId && (
                                <Button type="button" variant="ghost" size="icon" className="size-8" onClick={() => update(file.path, { versionId: batch.versionId })} aria-label={t('seriesImport.batch.reset')} title={t('seriesImport.batch.reset')}>
                                  <XIcon />
                                </Button>
                              )}
                            </span>
                          ) : (
                            <FollowsBar label={versionName(batchShow, batch.versionId)} onOther={() => open(`${file.path}:version`)} testId="series-manual-import-version-same" />
                          )}
                        </TableCell>
                        <TableCell className="align-top">
                          {selection.seriesId === null ? (
                            <span className="text-xs text-muted-foreground">{t('common.none')}</span>
                          ) : (
                            <EpisodePicker
                              key={selection.seriesId}
                              seriesId={selection.seriesId}
                              value={selection.episodeIds}
                              proposed={file}
                              onChange={(ids) => update(file.path, { episodeIds: ids, selected: ids.length > 0 && selection.versionId !== null ? true : selection.selected })}
                            />
                          )}
                        </TableCell>
                      </TableRow>
                    )
                  })}
                </TableBody>
              </Table>
            </div>
          </>
        )}

        <ImportFooter
          id="smi-mode"
          mode={mode}
          onMode={setMode}
          defaultMode={listing.data?.defaultTransferMode}
          count={items.length}
          running={run.isPending}
          onRun={() => run.mutate()}
          onClose={onClose}
          runTestId="series-manual-import-run"
        />
      </DialogContent>
    </Dialog>
  )
}

/** A cell that follows the bar: what it holds, muted, and a way to give this row its own. */
function FollowsBar({ label, onOther, testId }: { label?: string; onOther: () => void; testId?: string }) {
  const { t } = useTranslation()
  return (
    <span className="flex h-8 items-center gap-1 text-xs text-muted-foreground" data-testid={testId}>
      {label ?? t('seriesImport.batch.same')}
      <button type="button" className="underline underline-offset-4 hover:text-foreground" onClick={onOther}>
        {t('seriesImport.batch.other')}
      </button>
    </span>
  )
}

/**
 * Season, then the episodes of that season the file holds (several for a multi-episode file). With `ordered`, seasons
 * and numbers are those the series page shows (its order), TVDB's beside them.
 */
export function EpisodePicker({ seriesId, value, proposed, onChange, ordered = false }: { seriesId: number; value: number[]; proposed?: SeriesManualImportFile; onChange: (ids: number[]) => void; ordered?: boolean }) {
  const { t } = useTranslation()
  const episodes = useQuery({ queryKey: ['episodes', seriesId], queryFn: () => seriesApi.episodes(seriesId) })
  const all = episodes.data ?? []
  const seasonOf = (e: Episode) => (ordered ? e.displaySeasonNumber : e.seasonNumber)
  const numberOf = (e: Episode) => (ordered ? e.displayEpisodeNumber : e.episodeNumber)
  const shown = (e: Episode) => (ordered ? episodeCode(e.displaySeasonNumber, e.displayEpisodeNumber) : code(e))
  const chosen = all.filter((e) => value.includes(e.id))
  // The season shown follows the episodes held (a renumbered row moves with them) until the user picks another.
  const [pickedSeason, setSeason] = useState<number | null>(null)
  const season = pickedSeason ?? (chosen[0] ? seasonOf(chosen[0]) : null) ?? proposed?.seasonNumber ?? null
  const seasons = [...new Set(all.map(seasonOf))].sort((a, b) => a - b)
  const inSeason = all.filter((e) => seasonOf(e) === season).sort((a, b) => numberOf(a) - numberOf(b))
  const label = chosen.length === 0 ? t('seriesImport.pickEpisodes') : chosen.map(shown).join(', ')

  const toggle = (e: Episode, on: boolean) => onChange(on ? [...value.filter((id) => { const x = all.find((y) => y.id === id); return x !== undefined && seasonOf(x) === seasonOf(e) }), e.id] : value.filter((id) => id !== e.id))

  return (
    <Popover>
      <PopoverTrigger asChild>
        <Button type="button" variant="outline" size="sm" className="min-w-36 justify-between font-normal" data-testid="episode-picker">
          <span className="truncate tabular-nums">{label}</span>
          <ChevronDownIcon className="opacity-50" />
        </Button>
      </PopoverTrigger>
      <PopoverContent className="w-80 p-2" align="start">
        <Select value={season === null ? '' : String(season)} onValueChange={(v) => setSeason(Number(v))}>
          <SelectTrigger size="sm" className="mb-2 w-full">
            <SelectValue placeholder={t('seriesImport.pickSeason')} />
          </SelectTrigger>
          <SelectContent>
            {seasons.map((n) => (
              <SelectItem key={n} value={String(n)}>
                {n === 0 ? t('seriesDetail.specials') : t('seriesDetail.season', { number: n })}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
        <div className="grid max-h-64 gap-1 overflow-y-auto">
          {inSeason.map((e) => (
            <label key={e.id} className="flex items-center gap-2 rounded px-1 py-0.5 text-sm hover:bg-muted">
              <Checkbox checked={value.includes(e.id)} onCheckedChange={(v) => toggle(e, v === true)} />
              <span className="tabular-nums text-muted-foreground">{shown(e)}</span>
              <span className="truncate">{e.title}</span>
              {ordered && shown(e) !== code(e) && <span className="ml-auto shrink-0 text-xs tabular-nums text-muted-foreground" title={t('seriesOrder.tvdbNumbers')}>{code(e)}</span>}
            </label>
          ))}
        </div>
      </PopoverContent>
    </Popover>
  )
}
