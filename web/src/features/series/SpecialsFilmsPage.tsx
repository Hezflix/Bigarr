import { useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useSearch } from '@tanstack/react-router'
import { ChevronRightIcon, ClapperboardIcon, EyeOffIcon, InfoIcon, Link2Icon, Link2OffIcon, RotateCcwIcon, TriangleAlertIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ApiError, seriesFilms, type SeriesFilmReviewRow, type SeriesFilmState } from '@/api/client'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { FileCompare, FilesCell, type Keep, Plan, planFor } from './SpecialsFilmsFiles'

type StateFilter = 'all' | SeriesFilmState
type EvidenceFilter = 'all' | 'flagged' | 'likely'
export type FilesFilter = 'all' | 'specialHasFile' | 'filmHasFile' | 'filmInLibrary'

const FILES_FILTERS: FilesFilter[] = ['all', 'specialHasFile', 'filmHasFile', 'filmInLibrary']

/**
 * Specials as films (S8): every special detected as a film, across all shows, with what linking it would do on disk
 * before anything happens (the keeper's dry run). Opened from either library: from the films it starts on the films
 * already in the library, from the series on the specials that have a file.
 */
export function SpecialsFilmsPage() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const search = useSearch({ strict: false }) as { files?: FilesFilter }
  const review = useQuery({ queryKey: ['seriesFilmsReview'], queryFn: seriesFilms.review })
  const [state, setState] = useState<StateFilter>('all')
  const [evidence, setEvidence] = useState<EvidenceFilter>('all')
  const [files, setFiles] = useState<FilesFilter>(search.files && FILES_FILTERS.includes(search.files) ? search.files : 'all')
  const [selected, setSelected] = useState<Set<number>>(new Set())
  const [confirming, setConfirming] = useState<SeriesFilmReviewRow[] | null>(null)
  // The specials whose own file stays when the film has one too; the film's wins otherwise.
  const [planOpen, setPlanOpen] = useState<Set<number>>(new Set())
  const togglePlan = (episodeId: number) =>
    setPlanOpen((current) => {
      const next = new Set(current)
      if (!next.delete(episodeId)) next.add(episodeId)
      return next
    })
  const [keepSpecial, setKeepSpecial] = useState<Set<number>>(new Set())
  const keepOf = (row: SeriesFilmReviewRow): Keep => (row.keepSpecialPlan && keepSpecial.has(row.episodeId) ? 'special' : 'film')
  const setKeep = (row: SeriesFilmReviewRow, keep: Keep) =>
    setKeepSpecial((current) => {
      const next = new Set(current)
      if (keep === 'special') next.add(row.episodeId)
      else next.delete(row.episodeId)
      return next
    })

  const rows = useMemo(
    () =>
      (review.data?.rows ?? []).filter(
        (r) =>
          (state === 'all' || r.state === state) &&
          (evidence === 'all' || r.confidence === evidence) &&
          (files === 'all' || (files === 'specialHasFile' ? r.specialHasFile : files === 'filmHasFile' ? r.filmHasFile : r.filmInLibrary)),
      ),
    [review.data, state, evidence, files],
  )
  const linkable = rows.filter((r) => r.state !== 'linked' && r.tmdbId)
  const picked = linkable.filter((r) => selected.has(r.episodeId))
  const counts = useMemo(() => {
    const all = review.data?.rows ?? []
    return { suggested: all.filter((r) => r.state === 'suggested').length, linked: all.filter((r) => r.state === 'linked').length, dismissed: all.filter((r) => r.state === 'dismissed').length }
  }, [review.data])

  const refresh = () => {
    void queryClient.invalidateQueries({ queryKey: ['seriesFilmsReview'] })
    void queryClient.invalidateQueries({ queryKey: ['seriesFilmsSummary'] })
    void queryClient.invalidateQueries({ queryKey: ['seriesFilms'] })
  }
  const fail = (e: unknown) => toast.error(e instanceof ApiError ? e.message : t('common.unexpectedError'))
  const dismiss = useMutation({ mutationFn: (r: SeriesFilmReviewRow) => seriesFilms.dismiss(r.seriesId, r.episodeId), onSuccess: refresh, onError: fail })
  const restore = useMutation({ mutationFn: (r: SeriesFilmReviewRow) => seriesFilms.restore(r.seriesId, r.episodeId), onSuccess: refresh, onError: fail })
  const unlink = useMutation({
    mutationFn: (r: SeriesFilmReviewRow) => seriesFilms.remove(r.seriesId, r.episodeId),
    onSuccess: (_, r) => {
      toast.success(t('seriesFilms.unlinked', { label: r.label }))
      refresh()
    },
    onError: fail,
  })

  const toggle = (episodeId: number, on: boolean) =>
    setSelected((current) => {
      const next = new Set(current)
      if (on) next.add(episodeId)
      else next.delete(episodeId)
      return next
    })
  const allPicked = linkable.length > 0 && picked.length === linkable.length

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center gap-x-3 gap-y-2">
        <h1 className="text-2xl font-semibold tracking-tight">{t('specialsFilms.title')}</h1>
        {review.data && (
          <span className="text-sm text-muted-foreground tabular-nums" data-testid="specials-films-counts">
            {t('specialsFilms.counts', counts)}
          </span>
        )}
        <div className="ml-auto flex items-center gap-2">
          <Button type="button" onClick={() => setConfirming(picked)} disabled={picked.length === 0} data-testid="specials-films-link-selected">
            <Link2Icon />
            {t('specialsFilms.linkSelected', { count: picked.length })}
          </Button>
        </div>
      </div>
      <Alert className="max-w-4xl" data-testid="specials-films-how">
        <InfoIcon />
        <AlertTitle>{t('specialsFilms.how.title')}</AlertTitle>
        <AlertDescription>
          <ul className="list-disc space-y-0.5 pl-4">
            <li>{t('specialsFilms.how.film')}</li>
            <li>{t('specialsFilms.how.special')}</li>
            <li>{t('specialsFilms.how.both')}</li>
            <li>{t('specialsFilms.how.nothing')}</li>
          </ul>
        </AlertDescription>
      </Alert>
      {review.data?.shadowEnabled && (
        <Alert>
          <AlertDescription>{t('specialsFilms.shadow')}</AlertDescription>
        </Alert>
      )}

      <div className="flex flex-wrap items-center gap-2" data-testid="specials-films-filters">
        <FilterSelect label={t('specialsFilms.filters.state')} value={state} onChange={(v) => setState(v as StateFilter)} options={(['all', 'suggested', 'linked', 'dismissed'] as const).map((v) => [v, t(`specialsFilms.state.${v}`)])} />
        <FilterSelect label={t('specialsFilms.filters.evidence')} value={evidence} onChange={(v) => setEvidence(v as EvidenceFilter)} options={[['all', t('specialsFilms.state.all')], ['flagged', t('seriesFilms.confidence.flagged')], ['likely', t('seriesFilms.confidence.likely')]]} />
        <FilterSelect label={t('specialsFilms.filters.files')} value={files} onChange={(v) => setFiles(v as FilesFilter)} options={FILES_FILTERS.map((v) => [v, t(`specialsFilms.files.${v}`)])} />
        {review.data && rows.length !== review.data.rows.length && (
          <span className="text-xs text-muted-foreground tabular-nums">{t('specialsFilms.shown', { count: rows.length, total: review.data.rows.length })}</span>
        )}
      </div>

      {review.isLoading ? (
        <Skeleton className="h-64 w-full" />
      ) : rows.length === 0 ? (
        <p className="rounded-lg border p-6 text-center text-sm text-muted-foreground" data-testid="specials-films-empty">
          {review.data?.rows.length ? t('specialsFilms.noneShown') : t('specialsFilms.empty')}
        </p>
      ) : (
        <div className="overflow-x-auto rounded-lg border">
          <Table data-testid="specials-films-table">
            <TableHeader>
              <TableRow>
                <TableHead className="w-10 pl-4">
                  <Checkbox
                    checked={allPicked ? true : picked.length > 0 ? 'indeterminate' : false}
                    onCheckedChange={(checked) => setSelected(checked === true ? new Set(linkable.map((r) => r.episodeId)) : new Set())}
                    disabled={linkable.length === 0}
                    aria-label={t('specialsFilms.selectAll')}
                  />
                </TableHead>
                <TableHead className="w-[27%]">{t('specialsFilms.columns.special')}</TableHead>
                <TableHead className="w-[24%]">{t('specialsFilms.columns.film')}</TableHead>
                <TableHead className="w-28">{t('specialsFilms.columns.state')}</TableHead>
                <TableHead>{t('specialsFilms.columns.files')}</TableHead>
                <TableHead className="w-40 pr-4" />
              </TableRow>
            </TableHeader>
            <TableBody>
              {rows.flatMap((row) => [
                <TableRow key={row.episodeId} data-testid="specials-films-row" data-state={row.state}>
                  <TableCell className="pl-4 align-top">
                    {row.state !== 'linked' && (
                      <Checkbox
                        checked={selected.has(row.episodeId)}
                        onCheckedChange={(checked) => toggle(row.episodeId, checked === true)}
                        disabled={!row.tmdbId}
                        aria-label={t('specialsFilms.select', { label: row.label, series: row.seriesTitle })}
                      />
                    )}
                  </TableCell>
                  <TableCell className="max-w-0 align-top whitespace-normal">
                    <span className="block truncate" title={`${row.label} ${row.title}`}>
                      <span className="tabular-nums text-muted-foreground">{row.label}</span> {row.title}
                    </span>
                    <span className="flex min-w-0 items-center gap-1 text-xs text-muted-foreground">
                      <Link to="/series/$seriesId" params={{ seriesId: String(row.seriesId) }} className="truncate underline-offset-4 hover:underline" title={row.seriesTitle}>
                        {row.seriesTitle}
                      </Link>
                      {row.placementLabel && <span className="shrink-0">· {row.placementLabel}</span>}
                      {row.runtime ? <span className="shrink-0 tabular-nums">· {t('seriesFilms.minutes', { count: row.runtime })}</span> : null}
                    </span>
                  </TableCell>
                  <TableCell className="max-w-0 align-top whitespace-normal text-sm">
                    <FilmCell row={row} />
                  </TableCell>
                  <TableCell className="align-top">
                    <StateBadge row={row} />
                  </TableCell>
                  <TableCell className="max-w-0 align-top whitespace-normal">
                    <FilesCell row={row} keep={keepOf(row)} onKeep={(keep) => setKeep(row, keep)} />
                  </TableCell>
                  <TableCell className="py-1 pr-4 text-right align-top whitespace-nowrap">
                    <Button
                      type="button"
                      size="icon-sm"
                      variant="ghost"
                      onClick={() => togglePlan(row.episodeId)}
                      aria-expanded={planOpen.has(row.episodeId)}
                      aria-label={row.state === 'linked' ? t('specialsFilms.plan.showLinked') : t('specialsFilms.plan.show')}
                      title={row.state === 'linked' ? t('specialsFilms.plan.showLinked') : t('specialsFilms.plan.show')}
                      data-testid="specials-films-plan-toggle"
                    >
                      <ChevronRightIcon className={planOpen.has(row.episodeId) ? 'rotate-90 transition-transform' : 'transition-transform'} />
                    </Button>
                    {row.state === 'linked' ? (
                      <Button type="button" size="sm" variant="ghost" onClick={() => unlink.mutate(row)} disabled={unlink.isPending} title={t('seriesFilms.unlinkHint')}>
                        <Link2OffIcon />
                        {t('seriesFilms.unlink')}
                      </Button>
                    ) : (
                      <span className="inline-flex gap-1">
                        <Button type="button" size="sm" variant="outline" onClick={() => setConfirming([row])} disabled={!row.tmdbId} data-testid="specials-films-link">
                          <Link2Icon />
                          {t('specialsFilms.link')}
                        </Button>
                        {row.state === 'dismissed' ? (
                          <Button type="button" size="sm" variant="ghost" onClick={() => restore.mutate(row)} disabled={restore.isPending} title={t('specialsFilms.restoreHint')}>
                            <RotateCcwIcon />
                            {t('specialsFilms.restore')}
                          </Button>
                        ) : (
                          <Button type="button" size="sm" variant="ghost" onClick={() => dismiss.mutate(row)} disabled={dismiss.isPending} title={t('specialsFilms.dismissHint')} data-testid="specials-films-dismiss">
                            <EyeOffIcon />
                            {t('specialsFilms.dismiss')}
                          </Button>
                        )}
                      </span>
                    )}
                  </TableCell>
                </TableRow>,
                planOpen.has(row.episodeId) && (
                  <TableRow key={`${row.episodeId}-plan`} className="bg-muted/30 hover:bg-muted/30" data-testid="specials-films-plan-row">
                    <TableCell />
                    <TableCell colSpan={5} className="whitespace-normal pr-4 pb-3">
                      <span className="block pb-1.5 text-xs font-medium text-muted-foreground">{row.state === 'linked' ? t('specialsFilms.plan.showLinked') : t('specialsFilms.plan.show')}</span>
                      <Plan steps={planFor(row, keepOf(row))} />
                    </TableCell>
                  </TableRow>
                ),
              ])}
            </TableBody>
          </Table>
        </div>
      )}
      {confirming && (
        <LinkDialog
          rows={confirming}
          keepOf={keepOf}
          onKeep={setKeep}
          shadow={review.data?.shadowEnabled ?? false}
          onClose={() => setConfirming(null)}
          onDone={() => {
            setSelected(new Set())
            setKeepSpecial(new Set())
            refresh()
          }}
        />
      )}
    </div>
  )
}

function FilterSelect({ label, value, onChange, options }: { label: string; value: string; onChange: (value: string) => void; options: string[][] }) {
  return (
    <Select value={value} onValueChange={onChange}>
      <SelectTrigger size="sm" aria-label={label} className="min-w-40">
        <span className="text-muted-foreground">{label}:</span>
        <SelectValue />
      </SelectTrigger>
      <SelectContent>
        {options.map(([v, text]) => (
          <SelectItem key={v} value={v}>
            {text}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  )
}

function FilmCell({ row }: { row: SeriesFilmReviewRow }) {
  const { t } = useTranslation()
  const name = row.filmTitle ? (row.filmYear ? `${row.filmTitle} (${row.filmYear})` : row.filmTitle) : row.tmdbId ? t('specialsFilms.tmdbOnly', { id: row.tmdbId }) : t('seriesFilms.noTmdb')
  return (
    <span className="flex flex-col gap-0.5">
      {row.movieId ? (
        <Link to="/movies/$movieId" params={{ movieId: String(row.movieId) }} title={name} className="flex min-w-0 items-center gap-1 font-medium underline-offset-4 hover:underline">
          <ClapperboardIcon className="size-3.5 shrink-0" />
          <span className="truncate">{name}</span>
        </Link>
      ) : (
        <span className="truncate">{name}</span>
      )}
      <span className="text-xs text-muted-foreground tabular-nums">
        {row.tmdbId && `TMDB ${row.tmdbId}`}
        {row.filmInLibrary && row.state !== 'linked' && ` · ${t('seriesFilms.inLibrary')}`}
      </span>
    </span>
  )
}

function StateBadge({ row }: { row: SeriesFilmReviewRow }) {
  const { t } = useTranslation()
  return (
    <span className="flex flex-col items-start gap-1">
      <Badge variant={row.state === 'linked' ? 'secondary' : 'outline'} className={row.state === 'dismissed' ? 'text-muted-foreground' : undefined}>
        {t(`specialsFilms.state.${row.state}`)}
      </Badge>
      {row.confidence && (
        <span className="text-xs text-muted-foreground" title={row.evidence.join(' · ')}>
          {t(`seriesFilms.confidence.${row.confidence}`)}
        </span>
      )}
      {row.linkError && (
        <span className="flex items-center gap-1 text-xs text-destructive" title={row.linkError}>
          <TriangleAlertIcon className="size-3.5 shrink-0" />
          {t('seriesFilms.linkFailed')}
        </span>
      )}
    </span>
  )
}

/** Link asks first, with the file operations it is about to do. */
function LinkDialog({
  rows,
  keepOf,
  onKeep,
  shadow,
  onClose,
  onDone,
}: {
  rows: SeriesFilmReviewRow[]
  keepOf: (row: SeriesFilmReviewRow) => Keep
  onKeep: (row: SeriesFilmReviewRow, keep: Keep) => void
  shadow: boolean
  onClose: () => void
  onDone: () => void
}) {
  const { t } = useTranslation()
  const link = useMutation({
    mutationFn: () =>
      seriesFilms.linkMany(
        rows.map((r) => r.episodeId),
        rows.filter((r) => keepOf(r) === 'special').map((r) => r.episodeId),
      ),
    onSuccess: (results) => {
      const failed = results.filter((r) => !r.linked)
      const ok = results.length - failed.length
      if (ok > 0) toast.success(t('specialsFilms.linked', { count: ok }))
      for (const f of failed) {
        const row = rows.find((r) => r.episodeId === f.episodeId)
        toast.error(t('specialsFilms.linkFailed', { label: row ? `${row.seriesTitle} ${row.label}` : String(f.episodeId), error: f.error ?? '' }))
      }
      onDone()
      onClose()
    },
    onError: (e) => toast.error(e instanceof ApiError ? e.message : t('common.unexpectedError')),
  })

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="sm:max-w-3xl">
        <DialogHeader>
          <DialogTitle>{t('specialsFilms.confirmTitle', { count: rows.length })}</DialogTitle>
          <DialogDescription>{shadow ? t('specialsFilms.shadow') : t('specialsFilms.confirmHint')}</DialogDescription>
        </DialogHeader>
        <ul className="flex max-h-[60vh] flex-col gap-5 overflow-y-auto pr-1" data-testid="specials-films-confirm">
          {rows.map((row) => (
            <li key={row.episodeId} className="flex flex-col gap-2">
              <span className="text-sm font-medium">
                {row.seriesTitle} {row.label} → {row.filmTitle ?? `TMDB ${row.tmdbId}`}
              </span>
              {row.keepSpecialPlan && <span className="text-xs text-amber-700 dark:text-amber-300">{t('specialsFilms.keep.choose')}</span>}
              <FileCompare row={row} keep={keepOf(row)} onKeep={(keep) => onKeep(row, keep)} />
              <span className="pt-1 text-xs font-medium text-muted-foreground">{t('specialsFilms.plan.title')}</span>
              <Plan steps={planFor(row, keepOf(row))} />
            </li>
          ))}
        </ul>
        <DialogFooter>
          <Button type="button" variant="outline" onClick={onClose}>
            {t('common.cancel')}
          </Button>
          <Button type="button" onClick={() => link.mutate()} disabled={link.isPending} data-testid="specials-films-confirm-link">
            <Link2Icon />
            {t('specialsFilms.confirm', { count: rows.length })}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
