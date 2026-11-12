import { useEffect, useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { cn } from 'cn'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ApiError, manualImport, movies, quality, type ManualImportFile, type ManualImportItem, type ManualImportResult } from '@/api/client'
import { Badge } from '@/components/ui/badge'
import { Checkbox } from '@/components/ui/checkbox'
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { ImportFileCell, ImportFooter, ImportListingState, ImportPathForm, ImportQualityCell, rejectedStyle, type TransferChoice } from '@/features/import/manualImportParts'
import { approvedStyle } from '@/features/search/releaseTable'


/** `extras`: paths of the listed extra files the user keeps (all of them at first). */
type Selection = { selected: boolean; movieId: number | null; versionId: number | null; qualityId: number; extras: string[] }

/**
 * Manual import: the files under a path or behind a queued download, each with the film, version and
 * quality Bigarr guessed and the verdict of every version; the user corrects and imports, forced.
 */
export function ManualImportDialog({ grabId, path: initialPath, title, onClose }: { grabId?: number; path?: string; title?: string; onClose: () => void }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [path, setPath] = useState(initialPath ?? '')
  const [loadedPath, setLoadedPath] = useState<string | null>(initialPath ?? null)
  const [selections, setSelections] = useState<Record<string, Selection>>({})
  const [mode, setMode] = useState<TransferChoice>('default')
  const [results, setResults] = useState<ManualImportResult[] | null>(null)

  const listing = useQuery({
    queryKey: ['manualImport', grabId ?? null, grabId ? null : loadedPath],
    queryFn: () => manualImport.list(grabId ? { grabId } : { path: loadedPath ?? '' }),
    enabled: !!grabId || !!loadedPath,
    retry: false,
    staleTime: Number.POSITIVE_INFINITY,
  })
  const library = useQuery({ queryKey: ['movies'], queryFn: movies.list })
  const qualities = useQuery({ queryKey: ['qualityDefinitions'], queryFn: quality.definitions })

  // Bigarr's guesses seed the form once per listing; the user's changes then win.
  useEffect(() => {
    if (!listing.data) return
    setSelections(
      Object.fromEntries(
        listing.data.files.map((f) => [
          f.path,
          {
            selected: !f.isSample && f.movieId !== null && f.versionId !== null,
            movieId: f.movieId,
            versionId: f.versionId,
            qualityId: f.qualityId,
            extras: f.extras.map((e) => e.path),
          } satisfies Selection,
        ]),
      ),
    )
    setResults(null)
  }, [listing.data])

  const sortedMovies = useMemo(() => [...(library.data ?? [])].sort((a, b) => a.sortTitle.localeCompare(b.sortTitle)), [library.data])

  const items: ManualImportItem[] = Object.entries(selections).flatMap(([filePath, s]) =>
    !s.selected || s.versionId === null ? [] : [{ path: filePath, versionId: s.versionId, qualityId: s.qualityId, grabId: grabId ?? null, extraPaths: s.extras }])

  const run = useMutation({
    mutationFn: () => manualImport.import(items, mode === 'default' ? null : mode),
    onSuccess: async (outcome) => {
      setResults(outcome)
      const ok = outcome.filter((r) => r.imported).length
      if (ok > 0) toast.success(t('manualImport.importCount', { count: ok }))
      for (const r of outcome.filter((r) => !r.imported)) toast.error(t('manualImport.failed', { reason: r.reason }))
      await queryClient.invalidateQueries({ queryKey: ['queue'] })
      await queryClient.invalidateQueries({ queryKey: ['movies'] })
      await queryClient.invalidateQueries({ queryKey: ['movie'] })
      await queryClient.invalidateQueries({ queryKey: ['unmapped'] })
      await queryClient.invalidateQueries({ queryKey: ['rootFolders'] })
    },
    onError: (e) => toast.error(e instanceof ApiError ? e.message : t('manualImport.importFailed')),
  })

  const update = (filePath: string, patch: Partial<Selection>) => setSelections((s) => ({ ...s, [filePath]: { ...s[filePath], ...patch } }))

  const files = listing.data?.files ?? []

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="max-h-[90vh] overflow-y-auto sm:max-w-5xl" data-testid="manual-import">
        <DialogHeader>
          <DialogTitle>{title ? t('manualImport.titleFor', { title }) : t('manualImport.title')}</DialogTitle>
          <DialogDescription>{t('manualImport.hint')}</DialogDescription>
        </DialogHeader>

        {!grabId && <ImportPathForm id="mi-path" path={path} onPath={setPath} placeholder={t('manualImport.pathPlaceholder')} loading={listing.isFetching} onLoad={setLoadedPath} />}

        <ImportListingState fetching={listing.isFetching} error={listing.error} empty={!!listing.data && files.length === 0} />

        {listing.data && files.length > 0 && (
          <div className="overflow-x-auto rounded-lg border">
            <Table data-testid="manual-import-files">
              <TableHeader>
                <TableRow>
                  <TableHead className="w-8" />
                  <TableHead>{t('manualImport.file')}</TableHead>
                  <TableHead>{t('manualImport.quality')}</TableHead>
                  <TableHead>{t('manualImport.movie')}</TableHead>
                  <TableHead>{t('manualImport.version')}</TableHead>
                  <TableHead>{t('manualImport.verdict')}</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {files.map((file) => {
                  const selection = selections[file.path]
                  if (!selection) return null
                  const movie = sortedMovies.find((m) => m.id === selection.movieId)
                  const result = results?.find((r) => r.path === file.path)
                  return (
                    <TableRow key={file.path} data-testid="manual-import-file" className={cn(!selection.selected && 'text-muted-foreground')}>
                      <TableCell className="align-top">
                        <Checkbox
                          checked={selection.selected}
                          onCheckedChange={(v) => update(file.path, { selected: v === true })}
                          aria-label={t('manualImport.select')}
                          disabled={selection.versionId === null}
                        />
                      </TableCell>
                      <ImportFileCell
                        file={file}
                        edition={file.edition}
                        selected={selection.selected}
                        extras={selection.extras}
                        onExtras={(extras) => update(file.path, { extras })}
                        extrasLabel={t('manualImport.extras', { count: file.extras.length })}
                        extrasTestId="manual-import-extras"
                        result={result}
                        className="max-w-[24rem]"
                      />
                      <ImportQualityCell value={selection.qualityId} onChange={(qualityId) => update(file.path, { qualityId })} qualities={qualities.data ?? []} />
                      <TableCell className="align-top">
                        <Select
                          value={selection.movieId === null ? 'none' : String(selection.movieId)}
                          onValueChange={(v) => {
                            const movieId = v === 'none' ? null : Number(v)
                            const target = sortedMovies.find((m) => m.id === movieId)
                            const defaultVersion = movieId === file.movieId ? file.versionId : (target?.versions.find((x) => x.isDefault)?.id ?? target?.versions[0]?.id ?? null)
                            update(file.path, { movieId, versionId: defaultVersion, selected: defaultVersion !== null })
                          }}
                        >
                          <SelectTrigger className="min-w-44 max-w-64" size="sm">
                            <SelectValue placeholder={t('manualImport.noMovie')} />
                          </SelectTrigger>
                          <SelectContent>
                            <SelectItem value="none">{t('manualImport.noMovie')}</SelectItem>
                            {sortedMovies.map((m) => (
                              <SelectItem key={m.id} value={String(m.id)}>
                                {m.title}
                                {m.year ? ` (${m.year})` : ''}
                              </SelectItem>
                            ))}
                          </SelectContent>
                        </Select>
                        {selection.movieId === null && (
                          <span className="mt-1 block max-w-64 text-xs text-muted-foreground">
                            {file.matchReason ?? t('manualImport.noMovie')}{' '}
                            <Link to="/add" className="underline underline-offset-4 hover:text-foreground">
                              {t('manualImport.noMovieHint')}
                            </Link>
                          </span>
                        )}
                      </TableCell>
                      <TableCell className="align-top">
                        <Select
                          value={selection.versionId === null ? '' : String(selection.versionId)}
                          onValueChange={(v) => update(file.path, { versionId: Number(v), selected: true })}
                          disabled={!movie}
                        >
                          <SelectTrigger className="min-w-32" size="sm">
                            <SelectValue placeholder={t('manualImport.noVersion')} />
                          </SelectTrigger>
                          <SelectContent>
                            {(movie?.versions ?? []).map((v) => (
                              <SelectItem key={v.id} value={String(v.id)}>
                                {v.name}
                                {v.isDefault ? ` · ${t('versions.default').toLowerCase()}` : ''}
                              </SelectItem>
                            ))}
                          </SelectContent>
                        </Select>
                      </TableCell>
                      <TableCell className="min-w-[12rem] align-top whitespace-normal">
                        <Verdict file={file} movieId={selection.movieId} versionId={selection.versionId} />
                      </TableCell>
                    </TableRow>
                  )
                })}
              </TableBody>
            </Table>
          </div>
        )}

        <ImportFooter
          id="mi-mode"
          mode={mode}
          onMode={setMode}
          defaultMode={listing.data?.defaultTransferMode}
          count={items.length}
          running={run.isPending}
          onRun={() => run.mutate()}
          onClose={onClose}
          runTestId="manual-import-run"
        />
      </DialogContent>
    </Dialog>
  )
}

/** The engine's opinion for the suggested film only: once the user picks another film there is nothing to show. */
function Verdict({ file, movieId, versionId }: { file: ManualImportFile; movieId: number | null; versionId: number | null }) {
  const { t } = useTranslation()
  if (movieId === null || movieId !== file.movieId || versionId === null) return <span className="text-xs text-muted-foreground">{t('common.none')}</span>
  const option = file.versions.find((v) => v.versionId === versionId)
  if (!option) return <span className="text-xs text-muted-foreground">{t('common.none')}</span>
  if (option.approved) {
    return (
      <Badge variant="outline" className={approvedStyle}>
        {t('manualImport.accepted')}
      </Badge>
    )
  }
  return (
    <span className="flex flex-wrap gap-1">
      {option.rejections.map((reason) => (
        <Tooltip key={reason}>
          <TooltipTrigger asChild>
            <Badge variant="outline" className={cn('max-w-[16rem] cursor-default whitespace-normal text-left', rejectedStyle)}>
              {reason}
            </Badge>
          </TooltipTrigger>
          <TooltipContent className="max-w-sm whitespace-normal">{reason}</TooltipContent>
        </Tooltip>
      ))}
    </span>
  )
}
