import { useEffect, useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { cn } from 'cn'
import { ChevronDownIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import {
  ApiError,
  quality,
  series as seriesApi,
  seriesFiles,
  type Episode,
  type SeriesManualImportFile,
  type SeriesManualImportItem,
  type SeriesManualImportResult,
} from '@/api/client'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { ImportFileCell, ImportFooter, ImportListingState, ImportPathForm, ImportQualityCell, rejectedStyle, type TransferChoice } from '@/features/import/manualImportParts'


type Selection = { selected: boolean; seriesId: number | null; versionId: number | null; episodeIds: number[]; qualityId: number; extras: string[] }

const code = (e: { seasonNumber: number; episodeNumber: number }) => `S${String(e.seasonNumber).padStart(2, '0')}E${String(e.episodeNumber).padStart(2, '0')}`

/**
 * Manual import for series (Sonarr's interactive import): the files under a path or behind a queued download, each with
 * the series, version and episodes the mapper proposes; the user assigns what is missing or wrong and imports, forced.
 */
export function SeriesManualImportDialog({ grabId, path: initialPath, seriesId, title, onClose }: { grabId?: number; path?: string; seriesId?: number; title?: string; onClose: () => void }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [path, setPath] = useState(initialPath ?? '')
  const [loadedPath, setLoadedPath] = useState<string | null>(initialPath ?? null)
  const [selections, setSelections] = useState<Record<string, Selection>>({})
  const [mode, setMode] = useState<TransferChoice>('default')
  const [results, setResults] = useState<SeriesManualImportResult[] | null>(null)

  const listing = useQuery({
    queryKey: ['seriesManualImport', grabId ?? null, grabId ? null : loadedPath, seriesId ?? null],
    queryFn: () => seriesFiles.list(grabId ? { grabId } : { path: loadedPath!, seriesId }),
    enabled: !!grabId || !!loadedPath,
    retry: false,
    staleTime: Number.POSITIVE_INFINITY,
  })
  const library = useQuery({ queryKey: ['series'], queryFn: seriesApi.list })
  const qualities = useQuery({ queryKey: ['qualityDefinitions'], queryFn: quality.definitions })

  useEffect(() => {
    if (!listing.data) return
    setSelections(
      Object.fromEntries(
        listing.data.files.map((f) => [
          f.path,
          {
            selected: !f.isSample && f.seriesId !== null && f.versionId !== null && f.episodes.length > 0 && f.rejections.length === 0,
            seriesId: f.seriesId,
            versionId: f.versionId,
            episodeIds: f.episodes.map((e) => e.id),
            qualityId: f.qualityId,
            extras: f.extras.map((e) => e.path),
          } satisfies Selection,
        ]),
      ),
    )
    setResults(null)
  }, [listing.data])

  const sortedSeries = useMemo(() => [...(library.data ?? [])].sort((a, b) => a.sortTitle.localeCompare(b.sortTitle)), [library.data])

  const items: SeriesManualImportItem[] = Object.entries(selections)
    .filter(([, s]) => s.selected && s.versionId !== null && s.episodeIds.length > 0)
    .map(([filePath, s]) => ({ path: filePath, versionId: s.versionId!, episodeIds: s.episodeIds, qualityId: s.qualityId, grabId: grabId ?? null, extraPaths: s.extras }))

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

  const files = listing.data?.files ?? []

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="max-h-[90vh] overflow-y-auto sm:max-w-6xl" data-testid="series-manual-import">
        <DialogHeader>
          <DialogTitle>{title ? t('manualImport.titleFor', { title }) : t('seriesImport.title')}</DialogTitle>
          <DialogDescription>{t('seriesImport.hint')}</DialogDescription>
        </DialogHeader>

        {!grabId && <ImportPathForm id="smi-path" path={path} onPath={setPath} placeholder={t('seriesImport.pathPlaceholder')} loading={listing.isFetching} onLoad={setLoadedPath} />}

        <ImportListingState fetching={listing.isFetching} error={listing.error} empty={!!listing.data && files.length === 0} />

        {listing.data && files.length > 0 && (
          <div className="overflow-x-auto rounded-lg border">
            <Table data-testid="series-manual-import-files">
              <TableHeader>
                <TableRow>
                  <TableHead className="w-8" />
                  <TableHead>{t('manualImport.file')}</TableHead>
                  <TableHead>{t('manualImport.quality')}</TableHead>
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
                  return (
                    <TableRow key={file.path} data-testid="series-manual-import-file" className={cn(!selection.selected && 'text-muted-foreground')}>
                      <TableCell className="align-top">
                        <Checkbox
                          checked={selection.selected}
                          onCheckedChange={(v) => update(file.path, { selected: v === true })}
                          aria-label={t('manualImport.select')}
                          disabled={selection.versionId === null || selection.episodeIds.length === 0}
                        />
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
                          <span className="mt-1 flex flex-wrap gap-1">
                            {file.rejections.map((reason) => (
                              <Badge key={reason} variant="outline" className={cn('max-w-[20rem] whitespace-normal text-left', rejectedStyle)}>
                                {reason}
                              </Badge>
                            ))}
                          </span>
                        )}
                      </ImportFileCell>
                      <ImportQualityCell value={selection.qualityId} onChange={(qualityId) => update(file.path, { qualityId })} qualities={qualities.data ?? []} />
                      <TableCell className="align-top">
                        <Select
                          value={selection.seriesId === null ? 'none' : String(selection.seriesId)}
                          onValueChange={(v) => {
                            const id = v === 'none' ? null : Number(v)
                            const target = sortedSeries.find((s) => s.id === id)
                            const version = id === file.seriesId ? file.versionId : (target?.versions.find((x) => x.isDefault)?.id ?? target?.versions[0]?.id ?? null)
                            update(file.path, { seriesId: id, versionId: version, episodeIds: id === file.seriesId ? file.episodes.map((e) => e.id) : [], selected: false })
                          }}
                        >
                          <SelectTrigger className="min-w-44 max-w-64" size="sm">
                            <SelectValue placeholder={t('seriesImport.noSeries')} />
                          </SelectTrigger>
                          <SelectContent>
                            <SelectItem value="none">{t('seriesImport.noSeries')}</SelectItem>
                            {sortedSeries.map((s) => (
                              <SelectItem key={s.id} value={String(s.id)}>
                                {s.title}
                                {s.year ? ` (${s.year})` : ''}
                              </SelectItem>
                            ))}
                          </SelectContent>
                        </Select>
                        {file.matchReason && selection.episodeIds.length === 0 && (
                          <span className="mt-1 block max-w-64 text-xs text-muted-foreground" data-testid="series-manual-import-reason">
                            {file.matchReason}
                          </span>
                        )}
                      </TableCell>
                      <TableCell className="align-top">
                        <Select value={selection.versionId === null ? '' : String(selection.versionId)} onValueChange={(v) => update(file.path, { versionId: Number(v) })} disabled={!show}>
                          <SelectTrigger className="min-w-28" size="sm">
                            <SelectValue placeholder={t('manualImport.noVersion')} />
                          </SelectTrigger>
                          <SelectContent>
                            {(show?.versions ?? []).map((v) => (
                              <SelectItem key={v.id} value={String(v.id)}>
                                {v.name}
                                {v.isDefault ? ` · ${t('versions.default').toLowerCase()}` : ''}
                              </SelectItem>
                            ))}
                          </SelectContent>
                        </Select>
                      </TableCell>
                      <TableCell className="align-top">
                        {selection.seriesId === null ? (
                          <span className="text-xs text-muted-foreground">{t('common.none')}</span>
                        ) : (
                          <EpisodePicker
                            seriesId={selection.seriesId}
                            value={selection.episodeIds}
                            proposed={file}
                            onChange={(ids) => update(file.path, { episodeIds: ids, selected: ids.length > 0 && selection.versionId !== null })}
                          />
                        )}
                      </TableCell>
                    </TableRow>
                  )
                })}
              </TableBody>
            </Table>
          </div>
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
  const shown = (e: Episode) => (ordered ? `S${String(e.displaySeasonNumber).padStart(2, '0')}E${String(e.displayEpisodeNumber).padStart(2, '0')}` : code(e))
  const chosen = all.filter((e) => value.includes(e.id))
  const [season, setSeason] = useState<number | null>((chosen[0] ? seasonOf(chosen[0]) : null) ?? proposed?.seasonNumber ?? null)
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
