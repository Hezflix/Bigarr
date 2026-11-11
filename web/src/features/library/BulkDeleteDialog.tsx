import { useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ApiError, movies, type VersionSelector } from '@/api/client'
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
import { Checkbox } from '@/components/ui/checkbox'
import { Label } from '@/components/ui/label'
import { Skeleton } from '@/components/ui/skeleton'
import { ResultList, type BulkItemResult } from './BulkResults'

/** What a bulk delete answers, for films or series. */
export type DeleteAnswer = { dryRun: boolean; recycleBin: string | null; results: BulkItemResult[] }

type Props<R extends DeleteAnswer> = {
  open: boolean
  onOpenChange: (open: boolean) => void
  /** The films (or series, with `run` and `kind`) selected. */
  movieIds: number[]
  target: VersionSelector
  /** "the 4k version" */
  targetPhrase: string
  onDeleted: (response: R) => void
  /** Series: the request, the wording, and no import exclusion (there is none for series). Films by default. */
  kind?: 'movies' | 'series'
  run?: (request: { versions: VersionSelector; deleteFiles: boolean; addImportExclusion: boolean; dryRun: boolean }) => Promise<R>
}

/** The lines that name the items: "movie" for films, "series" for series. */
type Worded = 'title' | 'titleAll' | 'allHint' | 'versionHint' | 'planVersions' | 'planFilmsKept' | 'planFilmsRemoved' | 'planPromoted' | 'planSkipped' | 'perFilm' | 'confirmFilms'

/**
 * Bulk delete asks the server for the exact plan first (a dry run of the same request) and states it: what goes, what
 * stays, where the files end up. The confirm button then runs that request for real.
 */
export function BulkDeleteDialog<R extends DeleteAnswer>({ open, onOpenChange, movieIds, target, targetPhrase, onDeleted, kind = 'movies', run }: Props<R>) {
  const { t } = useTranslation()
  const [deleteFiles, setDeleteFiles] = useState(false)
  const [addImportExclusion, setAddImportExclusion] = useState(false)
  const request = { versions: target, deleteFiles, addImportExclusion }
  const send = (dryRun: boolean): Promise<R> => (run ? run({ ...request, dryRun }) : (movies.bulkDelete({ movieIds, ...request, dryRun }) as Promise<DeleteAnswer> as Promise<R>))
  const w = (key: Worded) => (kind === 'series' ? (`seriesBulk.delete.${key}` as const) : (`bulk.delete.${key}` as const))

  const plan = useQuery({
    queryKey: kind === 'series' ? ['seriesBulkDeletePlan', target, deleteFiles, movieIds] : ['bulkDeletePlan', target, deleteFiles, addImportExclusion, movieIds],
    queryFn: () => send(true),
    enabled: open && movieIds.length > 0,
    staleTime: 0,
    gcTime: 0,
  })
  const remove = useMutation({
    mutationFn: () => send(false),
    onSuccess: (response) => {
      onOpenChange(false)
      onDeleted(response)
    },
    onError: (e) => toast.error(e instanceof ApiError ? e.message : t('common.unexpectedError')),
  })

  const summary = plan.data ? summarize(plan.data.results) : null

  return (
    <AlertDialog open={open} onOpenChange={onOpenChange}>
      <AlertDialogContent className="sm:max-w-xl" data-testid="bulk-delete-dialog">
        <AlertDialogHeader>
          <AlertDialogTitle>
            {target === 'all' ? t(w('titleAll'), { count: movieIds.length }) : t(w('title'), { target: targetPhrase, count: movieIds.length })}
          </AlertDialogTitle>
          <AlertDialogDescription>{target === 'all' ? t(w('allHint')) : t(w('versionHint'), { target: targetPhrase })}</AlertDialogDescription>
        </AlertDialogHeader>

        <div className="flex flex-col gap-3">
          <div className="flex items-start gap-2">
            <Checkbox id="bulk-delete-files" checked={deleteFiles} onCheckedChange={(checked) => setDeleteFiles(checked === true)} className="mt-0.5" data-testid="bulk-delete-files" />
            <div className="grid gap-0.5">
              <Label htmlFor="bulk-delete-files" className="font-normal">
                {t('bulk.delete.deleteFiles')}
              </Label>
              <span className="text-xs text-muted-foreground">{t('bulk.delete.deleteFilesHint')}</span>
            </div>
          </div>
          {kind === 'movies' && (
          <div className="flex items-start gap-2">
            <Checkbox id="bulk-delete-exclude" checked={addImportExclusion} onCheckedChange={(checked) => setAddImportExclusion(checked === true)} className="mt-0.5" data-testid="bulk-delete-exclusion" />
            <div className="grid gap-0.5">
              <Label htmlFor="bulk-delete-exclude" className="font-normal">
                {t('bulk.delete.exclusion')}
              </Label>
              <span className="text-xs text-muted-foreground">{t('bulk.delete.exclusionHint')}</span>
            </div>
          </div>
          )}

          <div className="rounded-md border bg-muted/40 p-3 text-sm" data-testid="bulk-delete-plan">
            {plan.isPending && (
              <div className="flex flex-col gap-2">
                <span className="text-muted-foreground">{t('bulk.delete.planning')}</span>
                <Skeleton className="h-4 w-3/4" />
                <Skeleton className="h-4 w-1/2" />
              </div>
            )}
            {plan.error && (
              <Alert variant="destructive">
                <AlertDescription>{plan.error instanceof ApiError ? plan.error.message : t('common.unexpectedError')}</AlertDescription>
              </Alert>
            )}
            {plan.data && summary && (
              <ul className="flex list-disc flex-col gap-1 pl-4 marker:text-muted-foreground">
                {summary.versions === 0 && <li>{t('bulk.delete.nothing')}</li>}
                {summary.versions > 0 && <li className="font-medium">{t(w('planVersions'), { count: summary.versions, films: summary.films })}</li>}
                {summary.filmsKept > 0 && <li>{t(w('planFilmsKept'), { count: summary.filmsKept })}</li>}
                {summary.filmsRemoved > 0 && <li>{t(w('planFilmsRemoved'), { count: summary.filmsRemoved })}</li>}
                {addImportExclusion && summary.filmsRemoved > 0 && <li>{t('bulk.delete.planExclusions', { count: summary.filmsRemoved })}</li>}
                {summary.promoted.length > 0 && (
                  <li>{t(w('planPromoted'), { count: summary.promotedCount, names: summary.promoted.map(([name, n]) => `${name} (${n})`).join(', ') })}</li>
                )}
                {summary.versions > 0 && !deleteFiles && <li>{t('bulk.delete.planFilesKept')}</li>}
                {deleteFiles && summary.files === 0 && summary.versions > 0 && <li>{t('bulk.delete.planNoFiles')}</li>}
                {deleteFiles && summary.files > 0 && (
                  <li className={plan.data.recycleBin ? undefined : 'font-medium text-destructive'}>
                    {plan.data.recycleBin
                      ? t('bulk.delete.planFilesRecycled', { count: summary.files, path: plan.data.recycleBin })
                      : t('bulk.delete.planFilesDeleted', { count: summary.files })}
                  </li>
                )}
                {summary.skipped.length > 0 && (
                  <li>
                    {t(w('planSkipped'), { count: summary.skippedCount })}
                    <ul className="mt-0.5 list-[circle] pl-4 text-muted-foreground">
                      {summary.skipped.map(([reason, n]) => (
                        <li key={reason}>{t('bulk.delete.planSkippedReason', { reason, count: n })}</li>
                      ))}
                    </ul>
                  </li>
                )}
              </ul>
            )}
          </div>

          {plan.data && plan.data.results.length > 0 && (
            <details className="text-sm">
              <summary className="cursor-pointer text-muted-foreground">{t(w('perFilm'))}</summary>
              <ResultList results={plan.data.results} className="mt-2 max-h-56 overflow-y-auto rounded-md border" />
            </details>
          )}
        </div>

        <AlertDialogFooter>
          <AlertDialogCancel>{t('common.cancel')}</AlertDialogCancel>
          <AlertDialogAction
            variant="destructive"
            disabled={!summary || summary.versions === 0 || remove.isPending || plan.isFetching}
            onClick={(event) => {
              event.preventDefault()
              remove.mutate()
            }}
            data-testid="bulk-delete-confirm"
          >
            {summary && summary.filmsKept === 0 && summary.filmsRemoved > 0
              ? t(w('confirmFilms'), { count: summary.filmsRemoved })
              : t('bulk.delete.confirmVersions', { count: summary?.versions ?? 0 })}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  )
}

/** The dry run's per-item lines, counted the way the dialog states them. */
function summarize(results: BulkItemResult[]) {
  const acted = results.filter((r) => r.outcome === 'movieDeleted' || r.outcome === 'seriesDeleted' || r.outcome === 'versionsDeleted')
  const promoted = new Map<string, number>()
  const skipped = new Map<string, number>()
  for (const r of results) {
    if (r.promoted) promoted.set(r.promoted, (promoted.get(r.promoted) ?? 0) + 1)
    if (r.outcome === 'skipped' || r.outcome === 'failed') skipped.set(r.detail ?? r.outcome, (skipped.get(r.detail ?? r.outcome) ?? 0) + 1)
  }

  return {
    films: acted.length,
    versions: acted.reduce((n, r) => n + r.versions.length, 0),
    filmsKept: acted.filter((r) => r.outcome === 'versionsDeleted').length,
    filmsRemoved: acted.filter((r) => r.outcome === 'movieDeleted' || r.outcome === 'seriesDeleted').length,
    files: acted.reduce((n, r) => n + r.files, 0),
    promoted: [...promoted].sort((a, b) => b[1] - a[1]),
    promotedCount: results.filter((r) => r.promoted).length,
    skipped: [...skipped].sort((a, b) => b[1] - a[1]),
    skippedCount: results.filter((r) => r.outcome === 'skipped' || r.outcome === 'failed').length,
  }
}
