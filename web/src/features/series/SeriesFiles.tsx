import { useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { cn } from 'cn'
import { InfoIcon, Trash2Icon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ApiError, seriesFiles, type EpisodeFile, type Series, type UnmatchedEpisodeFile } from '@/api/client'
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
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { EpisodePicker } from '@/features/import/SeriesManualImportDialog'
import { formatBytes, formatDate } from '@/i18n'

const errorMessage = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback)

/**
 * Rename a version's episode files to the current templates (Sonarr's preview rename): every file, or one season; the
 * preview lists what moves, extras included, then the user applies it.
 */
export function RenameSeriesDialog({ series: s, versionId, onClose, onDone }: { series: Series; versionId: number; onClose: () => void; onDone: () => void }) {
  const { t } = useTranslation()
  const [season, setSeason] = useState<number | null>(null)
  const preview = useQuery({
    queryKey: ['seriesRename', s.id, versionId, season],
    queryFn: () => seriesFiles.renamePreview(s.id, { versionId, seasonNumber: season ?? undefined }),
    staleTime: 0,
  })
  const changed = (preview.data ?? []).filter((p) => p.changed)
  const apply = useMutation({
    mutationFn: () => seriesFiles.rename(s.id, { versionId, seasonNumber: season, episodeFileIds: changed.map((p) => p.episodeFileId) }),
    onSuccess: (results) => {
      const done = results.filter((r) => r.renamed).length
      toast.success(t('seriesFiles.renamed', { count: done }))
      for (const r of results.filter((r) => r.error)) toast.error(r.error)
      onDone()
      onClose()
    },
    onError: (error) => toast.error(errorMessage(error, t('common.unexpectedError'))),
  })
  const seasons = [...s.seasons].map((x) => x.seasonNumber).sort((a, b) => a - b)

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="max-h-[90vh] overflow-y-auto sm:max-w-5xl" data-testid="series-rename">
        <DialogHeader>
          <DialogTitle>{t('seriesFiles.renameTitle', { title: s.title })}</DialogTitle>
          <DialogDescription>{t('seriesFiles.renameHint')}</DialogDescription>
        </DialogHeader>
        <div className="flex items-center gap-2">
          <Label htmlFor="rename-season">{t('seriesFiles.renameScope')}</Label>
          <Select value={season === null ? 'all' : String(season)} onValueChange={(v) => setSeason(v === 'all' ? null : Number(v))}>
            <SelectTrigger id="rename-season" size="sm" className="min-w-40">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">{t('seriesFiles.allSeasons')}</SelectItem>
              {seasons.map((n) => (
                <SelectItem key={n} value={String(n)}>
                  {n === 0 ? t('seriesDetail.specials') : t('seriesDetail.season', { number: n })}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
        {preview.isPending && <Skeleton className="h-24 w-full" />}
        {preview.data && changed.length === 0 && <p className="text-sm text-muted-foreground">{t('seriesFiles.nothingToRename')}</p>}
        {changed.length > 0 && (
          <div className="overflow-x-auto rounded-lg border">
            <Table data-testid="series-rename-preview" className="table-fixed">
              <TableHeader>
                <TableRow>
                  <TableHead className="w-24 pl-4">{t('seriesImport.episodes')}</TableHead>
                  <TableHead>{t('seriesFiles.from')}</TableHead>
                  <TableHead className="pr-4">{t('seriesFiles.to')}</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {changed.map((p) => (
                  <TableRow key={p.episodeFileId}>
                    <TableCell className="pl-4 align-top text-xs whitespace-normal tabular-nums">{p.episodes.join(', ')}</TableCell>
                    <TableCell className="align-top whitespace-normal">
                      <span className="block break-all font-mono text-xs text-muted-foreground line-through decoration-muted-foreground/40">{p.currentRelativePath}</span>
                    </TableCell>
                    <TableCell className="pr-4 align-top whitespace-normal">
                      <span className="block break-all font-mono text-xs">{p.newRelativePath}</span>
                      {p.extras
                        .filter((e) => e.currentRelativePath !== e.newRelativePath)
                        .map((e) => (
                          <span key={e.extraFileId} className="mt-0.5 block break-all font-mono text-[11px] text-muted-foreground">
                            + {e.newRelativePath}
                          </span>
                        ))}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        )}
        <DialogFooter>
          <Button type="button" variant="ghost" onClick={onClose}>
            {t('common.close')}
          </Button>
          <Button type="button" onClick={() => apply.mutate()} disabled={changed.length === 0 || apply.isPending} data-testid="series-rename-apply">
            {t('seriesFiles.renameCount', { count: changed.length })}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

/** Info and delete for one episode file, at the end of its row. */
export function EpisodeFileActions({ seriesId, file, onChanged }: { seriesId: number; file: EpisodeFile; onChanged: () => void }) {
  const { t } = useTranslation()
  const [info, setInfo] = useState(false)
  const [confirm, setConfirm] = useState(false)
  const remove = useMutation({
    mutationFn: () => seriesFiles.deleteFile(seriesId, file.id),
    onSuccess: () => {
      toast.success(t('seriesFiles.deleted'))
      setConfirm(false)
      onChanged()
    },
    onError: (error) => toast.error(errorMessage(error, t('common.unexpectedError'))),
  })

  return (
    <>
      <Button type="button" size="icon" variant="ghost" className="size-7" onClick={() => setInfo(true)} aria-label={t('seriesFiles.info')} title={t('seriesFiles.info')} data-testid="episode-file-info">
        <InfoIcon />
      </Button>
      <Button
        type="button"
        size="icon"
        variant="ghost"
        className="size-7 text-destructive hover:text-destructive"
        onClick={() => setConfirm(true)}
        aria-label={t('seriesFiles.delete')}
        title={t('seriesFiles.delete')}
        data-testid="episode-file-delete"
      >
        <Trash2Icon />
      </Button>
      {info && <EpisodeFileInfoDialog seriesId={seriesId} fileId={file.id} onClose={() => setInfo(false)} />}
      <AlertDialog open={confirm} onOpenChange={setConfirm}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>{t('seriesFiles.deleteTitle')}</AlertDialogTitle>
            <AlertDialogDescription>{t(file.episodeIds.length > 1 ? 'seriesFiles.deleteMulti' : 'seriesFiles.deleteHint', { path: file.relativePath, episodes: file.episodes })}</AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>{t('common.cancel')}</AlertDialogCancel>
            <AlertDialogAction variant="destructive" onClick={(e) => { e.preventDefault(); remove.mutate() }} disabled={remove.isPending}>
              {t('seriesFiles.delete')}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </>
  )
}

function EpisodeFileInfoDialog({ seriesId, fileId, onClose }: { seriesId: number; fileId: number; onClose: () => void }) {
  const { t } = useTranslation()
  const detail = useQuery({ queryKey: ['episodeFile', seriesId, fileId], queryFn: () => seriesFiles.file(seriesId, fileId) })
  const f = detail.data

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="sm:max-w-2xl" data-testid="episode-file-dialog">
        <DialogHeader>
          <DialogTitle>{f ? f.episodes.map((e) => `S${String(e.seasonNumber).padStart(2, '0')}E${String(e.episodeNumber).padStart(2, '0')} ${e.title}`).join(' + ') : t('seriesFiles.info')}</DialogTitle>
        </DialogHeader>
        <EpisodeFileDetails seriesId={seriesId} fileId={fileId} />
      </DialogContent>
    </Dialog>
  )
}

/** Everything known of an episode file: path, quality, size, group, languages, release, custom formats, media info, extras. */
export function EpisodeFileDetails({ seriesId, fileId }: { seriesId: number; fileId: number }) {
  const { t } = useTranslation()
  const detail = useQuery({ queryKey: ['episodeFile', seriesId, fileId], queryFn: () => seriesFiles.file(seriesId, fileId) })
  const f = detail.data
  const media = f?.mediaInfo
  const rows: [string, string | null | undefined][] = f
    ? [
        [t('seriesFiles.path'), f.path],
        [t('seriesFiles.version'), f.versionName],
        [t('seriesFiles.quality'), f.revisionVersion > 1 ? `${f.quality} (v${f.revisionVersion})` : f.quality],
        [t('seriesFiles.size'), formatBytes(f.size)],
        [t('seriesFiles.added'), formatDate(f.dateAdded)],
        [t('seriesFiles.group'), f.releaseGroup],
        [t('seriesFiles.languages'), f.languages.join(', ')],
        [t('seriesFiles.release'), f.originalReleaseTitle],
        [t('seriesFiles.formats'), f.customFormats.length > 0 ? `${f.customFormats.join(', ')} (${f.customFormatScore})` : null],
        [t('seriesFiles.video'), media ? [media.videoCodec, media.width && media.height ? `${media.width}×${media.height}` : null, media.videoDynamicRangeType].filter(Boolean).join(' · ') : null],
        [t('seriesFiles.audio'), media ? [media.audioCodec, media.audioChannels, media.audioLanguages?.join('+')].filter(Boolean).join(' · ') : null],
        [t('seriesFiles.subtitles'), media?.subtitles?.join(', ')],
      ]
    : []

  return (
    <>
      {detail.isPending && <Skeleton className="h-40 w-full" />}
      {detail.error && (
        <Alert variant="destructive">
          <AlertDescription>{errorMessage(detail.error, t('common.unexpectedError'))}</AlertDescription>
        </Alert>
      )}
      {f && (
        <dl className="grid grid-cols-[8rem_1fr] gap-x-3 gap-y-1.5 text-sm">
          {rows
            .filter(([, value]) => value)
            .map(([label, value]) => (
              <div key={label} className="contents">
                <dt className="text-muted-foreground">{label}</dt>
                <dd className="break-all">{value}</dd>
              </div>
            ))}
          {f.extras.length > 0 && (
            <div className="contents">
              <dt className="text-muted-foreground">{t('seriesFiles.extras')}</dt>
              <dd className="flex flex-col gap-0.5">
                {f.extras.map((e) => (
                  <span key={e.id} className="break-all font-mono text-xs">
                    {e.relativePath}
                  </span>
                ))}
              </dd>
            </div>
          )}
        </dl>
      )}
    </>
  )
}

/** A file the scan could not place, attached to the episodes the user picks where it lies; optionally renamed. */
export function AssignUnmatchedDialog({ seriesId, file, onClose, onDone }: { seriesId: number; file: UnmatchedEpisodeFile; onClose: () => void; onDone: () => void }) {
  const { t } = useTranslation()
  const [episodeIds, setEpisodeIds] = useState<number[]>([])
  const [rename, setRename] = useState(true)
  const [replace, setReplace] = useState(false)
  const assign = useMutation({
    mutationFn: () => seriesFiles.assign(seriesId, file.id, { episodeIds, rename, replace }),
    onSuccess: (placed) => {
      toast.success(t('seriesFiles.assigned', { path: placed.relativePath }))
      onDone()
      onClose()
    },
  })

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="sm:max-w-xl" data-testid="assign-unmatched">
        <DialogHeader>
          <DialogTitle>{t('seriesFiles.assignTitle')}</DialogTitle>
          <DialogDescription className="break-all font-mono text-xs">{file.relativePath}</DialogDescription>
        </DialogHeader>
        <div className="grid gap-3">
          <div className="grid gap-1.5">
            <Label>{t('seriesImport.episodes')}</Label>
            <EpisodePicker seriesId={seriesId} value={episodeIds} onChange={setEpisodeIds} />
          </div>
          <label className="flex items-start gap-2 text-sm">
            <Checkbox checked={rename} onCheckedChange={(v) => setRename(v === true)} className="mt-0.5" />
            <span className="grid gap-0.5">
              <span>{t('seriesFiles.assignRename')}</span>
              <span className="text-xs text-muted-foreground">{t('seriesFiles.assignRenameHint')}</span>
            </span>
          </label>
          <label className="flex items-start gap-2 text-sm">
            <Checkbox checked={replace} onCheckedChange={(v) => setReplace(v === true)} className="mt-0.5" />
            <span className="grid gap-0.5">
              <span>{t('seriesFiles.assignReplace')}</span>
              <span className="text-xs text-muted-foreground">{t('seriesFiles.assignReplaceHint')}</span>
            </span>
          </label>
          {assign.error && (
            <Alert variant="destructive">
              <AlertDescription>{errorMessage(assign.error, t('common.unexpectedError'))}</AlertDescription>
            </Alert>
          )}
        </div>
        <DialogFooter>
          <Button type="button" variant="ghost" onClick={onClose}>
            {t('common.cancel')}
          </Button>
          <Button type="button" onClick={() => assign.mutate()} disabled={episodeIds.length === 0 || assign.isPending} data-testid="assign-unmatched-run">
            {t('seriesFiles.assign')}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

/** The "also delete the files" choice of the series and version delete dialogs. */
export function DeleteFilesOption({ checked, onChange, hint }: { checked: boolean; onChange: (value: boolean) => void; hint: string }) {
  const { t } = useTranslation()
  return (
    <label className={cn('flex items-start gap-2 text-sm')} data-testid="delete-files-option">
      <Checkbox checked={checked} onCheckedChange={(v) => onChange(v === true)} className="mt-0.5" />
      <span className="grid gap-0.5">
        <span>{t('seriesFiles.deleteFiles')}</span>
        <span className="text-xs text-muted-foreground">{hint}</span>
      </span>
    </label>
  )
}

