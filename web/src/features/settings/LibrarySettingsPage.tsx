import { useEffect, useMemo, useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { FolderInputIcon, LinkIcon, Trash2Icon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ApiError, movies, rootFolders, series as seriesApi, type MediaKind, type RootFolder, type UnmappedFolder } from '@/api/client'
import { PathPicker } from '@/components/PathPicker'
import { Alert, AlertDescription } from '@/components/ui/alert'
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Progress } from '@/components/ui/progress'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { ManualImportDialog } from '@/features/import/ManualImportDialog'
import { MapSeriesFolderDialog } from '@/features/series/MapSeriesFolderDialog'
import { formatBytes } from '@/i18n'
import { StorageBar, usagePerRootFolder } from './StorageBar'

export function LibrarySettingsPage() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const roots = useQuery({ queryKey: ['rootFolders'], queryFn: rootFolders.list })
  const unmapped = useQuery({ queryKey: ['unmapped'], queryFn: rootFolders.unmapped })
  const library = useQuery({ queryKey: ['movies'], queryFn: movies.list })
  const seriesLibrary = useQuery({ queryKey: ['series'], queryFn: seriesApi.list })
  // Movie files per root folder, next to its version count; null until the library is loaded.
  const files = useMemo(
    () => (library.data && roots.data ? new Map(usagePerRootFolder(library.data, roots.data).map((f) => [f.id, f.files])) : null),
    [library.data, roots.data],
  )
  const [path, setPath] = useState('')
  const [kind, setKind] = useState<MediaKind>('movies')
  const [pendingRemove, setPendingRemove] = useState<RootFolder | null>(null)
  const [importing, setImporting] = useState<UnmappedFolder | null>(null)
  const [mapping, setMapping] = useState<UnmappedFolder | null>(null)

  const add = useMutation({
    mutationFn: () => rootFolders.add(path.trim(), kind),
    onSuccess: async () => {
      setPath('')
      await queryClient.invalidateQueries({ queryKey: ['rootFolders'] })
    },
  })
  const remove = useMutation({
    mutationFn: (id: number) => rootFolders.remove(id),
    onSuccess: async () => {
      setPendingRemove(null)
      await queryClient.invalidateQueries({ queryKey: ['rootFolders'] })
    },
    onError: (e) => {
      setPendingRemove(null)
      toast.error(e instanceof ApiError ? e.message : t('settings.removeFailed'))
    },
  })

  const submit = (e: FormEvent) => {
    e.preventDefault()
    add.mutate()
  }

  return (
    <>
      <h1 className="text-2xl font-semibold tracking-tight">{t('settings.library')}</h1>

      <section className="flex flex-col gap-3">
        <h2 className="text-xl font-semibold tracking-tight">{t('settings.rootFolders')}</h2>

        {library.data && roots.data && <StorageBar movies={library.data} series={seriesLibrary.data} roots={roots.data} />}

        <div className="overflow-x-auto rounded-lg border">
          <Table data-testid="rootfolders-table">
            <TableHeader>
              <TableRow>
                <TableHead>{t('settings.path')}</TableHead>
                <TableHead>{t('settings.kind')}</TableHead>
                <TableHead>{t('settings.freeSpace')}</TableHead>
                <TableHead>
                  <Tooltip>
                    <TooltipTrigger asChild>
                      <span className="cursor-help underline decoration-dotted underline-offset-4">{t('settings.versions')}</span>
                    </TooltipTrigger>
                    <TooltipContent className="max-w-xs">{t('settings.versionsHint')}</TooltipContent>
                  </Tooltip>
                </TableHead>
                <TableHead>{t('settings.unmapped')}</TableHead>
                <TableHead className="w-[28%]">{t('settings.scan')}</TableHead>
                <TableHead className="text-right">{t('versions.actions')}</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {roots.data?.map((r) => (
                <RootFolderRow key={r.id} root={r} files={files === null ? null : (files.get(r.id) ?? 0)} onRequestRemove={() => setPendingRemove(r)} />
              ))}
              {roots.data?.length === 0 && (
                <TableRow>
                  <TableCell colSpan={7} className="text-muted-foreground">
                    {t('settings.noRootFolders')}
                  </TableCell>
                </TableRow>
              )}
            </TableBody>
          </Table>
        </div>

        <form className="flex max-w-xl flex-wrap items-start gap-2" onSubmit={submit}>
          <PathPicker
            className="min-w-64 flex-1"
            value={path}
            onChange={setPath}
            placeholder={t('settings.pathPlaceholder')}
            testId="rootfolder-path"
          />
          <Select value={kind} onValueChange={(value) => setKind(value as MediaKind)}>
            <SelectTrigger className="w-32" aria-label={t('settings.kind')} data-testid="rootfolder-kind">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="movies">{t('settings.kinds.movies')}</SelectItem>
              <SelectItem value="series">{t('settings.kinds.series')}</SelectItem>
            </SelectContent>
          </Select>
          <Button type="submit" disabled={add.isPending || !path.trim()}>
            {t('settings.addRootFolder')}
          </Button>
          {add.error && (
            <Alert variant="destructive" className="basis-full">
              <AlertDescription>{add.error instanceof ApiError ? add.error.message : t('settings.addFailed')}</AlertDescription>
            </Alert>
          )}
        </form>

        <p className="max-w-3xl text-xs text-muted-foreground">{t('settings.scanHint')}</p>
      </section>

      <section className="flex flex-col gap-3">
        <h2 className="text-xl font-semibold tracking-tight">{t('settings.unmappedFolders')}</h2>
        {unmapped.data?.length === 0 && <p className="text-sm text-muted-foreground">{t('settings.unmappedEmpty')}</p>}
        {unmapped.data && unmapped.data.length > 0 && <p className="max-w-3xl text-xs text-muted-foreground">{t('settings.unmappedHint')}</p>}
        {unmapped.data && unmapped.data.length > 0 && (
          <div className="overflow-x-auto rounded-lg border">
            <Table data-testid="unmapped-table">
              <TableHeader>
                <TableRow>
                  <TableHead>{t('settings.folder')}</TableHead>
                  <TableHead>{t('settings.files')}</TableHead>
                  <TableHead>{t('settings.reason')}</TableHead>
                  <TableHead>{t('settings.detail')}</TableHead>
                  <TableHead className="text-right">{t('versions.actions')}</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {unmapped.data.map((u) => (
                  <TableRow key={u.id}>
                    <TableCell title={u.path}>
                      <span className="flex flex-wrap items-center gap-2">
                        {u.relativePath}
                        <Badge variant="outline">{t(`settings.kinds.${u.kind}`)}</Badge>
                      </span>
                    </TableCell>
                    <TableCell>{u.fileCount}</TableCell>
                    <TableCell>{t(`unmappedReason.${u.reason}`)}</TableCell>
                    <TableCell className="text-xs text-muted-foreground">{u.detail}</TableCell>
                    <TableCell className="text-right">
                      {u.kind === 'series' ? (
                        <Button type="button" variant="outline" size="sm" onClick={() => setMapping(u)} disabled={u.fileCount === 0} data-testid="unmapped-map-series">
                          <LinkIcon />
                          {t('settings.mapSeries')}
                        </Button>
                      ) : (
                        <Button type="button" variant="outline" size="sm" onClick={() => setImporting(u)} disabled={u.fileCount === 0} data-testid="unmapped-import">
                          <FolderInputIcon />
                          {t('settings.importFolder')}
                        </Button>
                      )}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        )}
      </section>

      {mapping && <MapSeriesFolderDialog folder={mapping} onClose={() => setMapping(null)} />}
      {importing && <ManualImportDialog path={importing.path} title={importing.relativePath} onClose={() => setImporting(null)} />}

      <AlertDialog open={pendingRemove !== null} onOpenChange={(open) => !open && setPendingRemove(null)}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>{t('settings.removeRootTitle')}</AlertDialogTitle>
            <AlertDialogDescription>{t('settings.confirmRemoveRoot', { path: pendingRemove?.path ?? '' })}</AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>{t('common.cancel')}</AlertDialogCancel>
            <AlertDialogAction
              onClick={(event) => {
                event.preventDefault()
                if (pendingRemove) remove.mutate(pendingRemove.id)
              }}
              disabled={remove.isPending}
            >
              {t('settings.remove')}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </>
  )
}

function RootFolderRow({ root, files, onRequestRemove }: { root: RootFolder; files: number | null; onRequestRemove: () => void }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const status = useQuery({
    queryKey: ['scan', root.id],
    queryFn: () => rootFolders.scanStatus(root.id),
    refetchInterval: (query) => (query.state.data?.state === 'running' || query.state.data?.state === 'queued' ? 1000 : false),
  })
  const scan = useMutation({
    mutationFn: () => rootFolders.scan(root.id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['scan', root.id] }),
    onError: (e) => toast.error(e instanceof ApiError ? e.message : t('common.unexpectedError')),
  })

  const s = status.data
  const busy = s?.state === 'running' || s?.state === 'queued'
  const scanState = s?.state
  // A scan that just finished changed the library: refresh what shows it, once, after the render.
  useEffect(() => {
    if (scanState !== 'completed') return
    void queryClient.invalidateQueries({ queryKey: ['movies'] })
    void queryClient.invalidateQueries({ queryKey: ['series'] })
    void queryClient.invalidateQueries({ queryKey: ['rootFolders'] })
    void queryClient.invalidateQueries({ queryKey: ['unmapped'] })
  }, [scanState, queryClient])

  const setKind = useMutation({
    mutationFn: (kind: MediaKind) => rootFolders.setKind(root.id, kind),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['rootFolders'] })
      void queryClient.invalidateQueries({ queryKey: ['unmapped'] })
    },
    onError: (e) => toast.error(e instanceof ApiError ? e.message : t('common.unexpectedError')),
  })

  const progress = busy && s && s.foldersTotal > 0 ? Math.round((s.foldersProcessed / s.foldersTotal) * 100) : 0

  return (
    <TableRow>
      <TableCell className="align-top whitespace-normal">
        <span className="flex flex-wrap items-center gap-2">
          <code className="rounded bg-muted px-1.5 py-0.5 text-xs break-all">{root.path}</code>
          {!root.accessible && (
            <Badge variant="outline" className="border-amber-600/30 bg-amber-500/15 text-amber-700 dark:border-amber-400/30 dark:text-amber-300">
              {t('settings.inaccessible')}
            </Badge>
          )}
        </span>
      </TableCell>
      <TableCell className="align-top">
        <Select value={root.kind} onValueChange={(value) => setKind.mutate(value as MediaKind)} disabled={setKind.isPending || root.versionCount > 0}>
          <SelectTrigger size="sm" className="w-28" aria-label={t('settings.kind')} title={root.versionCount > 0 ? t('settings.kindLocked') : undefined}>
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="movies">{t('settings.kinds.movies')}</SelectItem>
            <SelectItem value="series">{t('settings.kinds.series')}</SelectItem>
          </SelectContent>
        </Select>
      </TableCell>
      <TableCell className="align-top whitespace-nowrap">
        {formatBytes(root.freeSpace)} <span className="text-muted-foreground">/ {formatBytes(root.totalSpace)}</span>
      </TableCell>
      <TableCell className="align-top whitespace-nowrap" data-testid="rootfolder-versions">
        {root.versionCount}
        {files !== null && root.kind === 'movies' && <span className="text-muted-foreground"> · {t('settings.versionFiles', { count: files })}</span>}
      </TableCell>
      <TableCell className="align-top">{root.unmappedFolderCount}</TableCell>
      <TableCell className="align-top">
        {busy && s && (
          <div className="flex flex-col gap-1">
            <Progress value={progress} />
            <span className="text-xs text-muted-foreground">
              {s.foldersProcessed}/{s.foldersTotal}
              {s.currentFolder ? ` · ${s.currentFolder}` : ''}
            </span>
          </div>
        )}
        {s?.state === 'completed' && root.kind === 'movies' && (
          <span className="text-xs text-muted-foreground">
            {t('settings.scanMovies', { count: s.moviesAdded })}, {t('settings.scanVersions', { count: s.versionsAdded })},{' '}
            {t('settings.scanUnmapped', { count: s.unmapped })}
          </span>
        )}
        {s?.state === 'completed' && root.kind === 'series' && (
          <span className="text-xs text-muted-foreground" data-testid="series-scan-summary">
            {t('settings.scanSeries', { count: s.seriesAdded })}, {t('settings.scanEpisodeFiles', { count: s.filesAdded })},{' '}
            {t('settings.scanUnmatchedFiles', { count: s.unmatchedFiles })}, {t('settings.scanUnmapped', { count: s.unmapped })}
          </span>
        )}
        {s?.state === 'failed' && <span className="text-xs text-destructive">{s.error}</span>}
      </TableCell>
      <TableCell className="align-top">
        <div className="flex items-center justify-end gap-1">
          <Button type="button" size="sm" onClick={() => scan.mutate()} disabled={busy || scan.isPending || !root.accessible}>
            {busy ? t('settings.scanning') : t('settings.scan')}
          </Button>
          <Tooltip>
            <TooltipTrigger asChild>
              <Button
                type="button"
                variant="ghost"
                size="icon"
                className="text-destructive hover:text-destructive"
                onClick={onRequestRemove}
                aria-label={t('settings.remove')}
              >
                <Trash2Icon />
              </Button>
            </TooltipTrigger>
            <TooltipContent>{t('settings.remove')}</TooltipContent>
          </Tooltip>
        </div>
      </TableCell>
    </TableRow>
  )
}
