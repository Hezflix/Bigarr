import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ArrowRightIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ApiError, playback, type SureMatch } from '@/api/client'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Skeleton } from '@/components/ui/skeleton'
import { allChecked, checkedPairs } from './sureMatches'

/**
 * The films not linked whose film is sure, listed before anything is linked: each row, checked by default, says what
 * the server played and which film it is, with why. The owner unchecks the doubtful ones and links the rest at once;
 * the others are left for the picker, one by one. Asked for when the dialog opens, never on its own.
 */
export function SureMatchesDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  return (
    <Dialog open={open} onOpenChange={(next) => !next && onClose()}>
      <DialogContent className="max-h-[90vh] overflow-y-auto sm:max-w-2xl" data-testid="sure-matches">
        {open && <SureMatchesForm onClose={onClose} />}
      </DialogContent>
    </Dialog>
  )
}

function SureMatchesForm({ onClose }: { onClose: () => void }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const preview = useQuery({ queryKey: ['playback', 'sureMatches'], queryFn: playback.sureMatches, staleTime: 0, gcTime: 0, retry: false })
  const [unchecked, setUnchecked] = useState<ReadonlySet<number>>(new Set())
  const matches = preview.data?.matches ?? []
  const pairs = checkedPairs(matches, unchecked)
  const toggle = (itemId: number, on: boolean) =>
    setUnchecked((current) => {
      const next = new Set(current)
      if (on) next.delete(itemId)
      else next.add(itemId)
      return next
    })

  const save = useMutation({
    mutationFn: () => playback.linkMany(pairs),
    onSuccess: async (result) => {
      // The same queries as a single link, but not this list: it would search TMDB again for a dialog closing.
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['playback'], predicate: (q) => q.queryKey[1] !== 'sureMatches' }),
        queryClient.invalidateQueries({ queryKey: ['movies'] }),
      ])
      toast.success(t('playback.sureMatches.linked', { count: result.linked.length }), {
        description: result.skipped.length > 0 ? t('playback.sureMatches.skipped', { count: result.skipped.length }) : undefined,
      })
      onClose()
    },
    onError: (e) => toast.error(e instanceof ApiError ? e.message : t('playback.link.failed')),
  })

  return (
    <form
      className="flex min-w-0 flex-col gap-4"
      onSubmit={(e) => {
        e.preventDefault()
        if (pairs.length > 0) save.mutate()
      }}
    >
      <DialogHeader>
        <DialogTitle>{t('playback.sureMatches.title')}</DialogTitle>
        <DialogDescription>{t('playback.sureMatches.hint')}</DialogDescription>
      </DialogHeader>
      {preview.isPending && (
        <div className="flex flex-col gap-2" aria-busy="true">
          <p className="text-sm text-muted-foreground">{t('playback.sureMatches.searching')}</p>
          <Skeleton className="h-48 w-full" />
        </div>
      )}
      {preview.isError && <p className="text-sm text-destructive">{t('playback.sureMatches.failed')}</p>}
      {preview.data && matches.length === 0 && <p className="rounded-md border px-3 py-6 text-center text-sm text-muted-foreground" data-testid="sure-matches-none">{t('playback.sureMatches.none')}</p>}
      {matches.length > 0 && (
        <div className="flex flex-col gap-1">
          <label htmlFor="sure-matches-all" className="flex items-center gap-2.5 px-2 py-1 text-xs font-medium text-muted-foreground">
            <Checkbox
              id="sure-matches-all"
              checked={allChecked(matches, unchecked)}
              onCheckedChange={(v) => setUnchecked(v === true ? new Set() : new Set(matches.map((m) => m.item.id)))}
              data-testid="sure-matches-all"
            />
            {t('playback.sureMatches.checkAll')}
          </label>
          <ul className="flex max-h-[55vh] flex-col overflow-y-auto rounded-md border p-1" aria-label={t('playback.sureMatches.list')} data-testid="sure-matches-list">
            {matches.map((match) => (
              <li key={match.item.id}>
                <MatchRow match={match} checked={!unchecked.has(match.item.id)} onChange={(on) => toggle(match.item.id, on)} />
              </li>
            ))}
          </ul>
        </div>
      )}
      {preview.data && preview.data.withoutSureMatch > 0 && <p className="text-sm text-muted-foreground" data-testid="sure-matches-rest">{t('playback.sureMatches.rest', { count: preview.data.withoutSureMatch })}</p>}
      {preview.data && !preview.data.tmdbSearched && <p className="text-xs text-muted-foreground">{t('playback.sureMatches.tmdbUnavailable')}</p>}
      <DialogFooter>
        <Button type="button" variant="outline" onClick={onClose}>
          {t('common.cancel')}
        </Button>
        <Button type="submit" disabled={pairs.length === 0 || save.isPending} data-testid="sure-matches-save">
          {t('playback.sureMatches.save', { count: pairs.length })}
        </Button>
      </DialogFooter>
    </form>
  )
}

/** What the server played → the film it surely is, with why; the whole row toggles its checkbox. */
function MatchRow({ match, checked, onChange }: { match: SureMatch; checked: boolean; onChange: (on: boolean) => void }) {
  const { t } = useTranslation()
  const { item, film } = match
  return (
    <label htmlFor={`sure-match-${item.id}`} className="flex cursor-pointer items-center gap-2.5 rounded-sm px-2 py-1.5 text-sm hover:bg-accent/60" data-testid="sure-match">
      <Checkbox id={`sure-match-${item.id}`} checked={checked} onCheckedChange={(v) => onChange(v === true)} />
      <span className="flex min-w-0 flex-1 flex-col">
        <span className="truncate">
          {item.title}
          {item.year && <span className="text-muted-foreground"> ({item.year})</span>}
        </span>
        <span className="text-xs text-muted-foreground tabular-nums">{t('playback.sureMatches.plays', { count: item.plays })}</span>
      </span>
      <ArrowRightIcon className="size-4 shrink-0 text-muted-foreground" aria-hidden="true" />
      <span className="flex min-w-0 flex-1 items-center gap-2.5">
        {film.posterUrl ? (
          <img src={film.posterUrl.replace('/original/', '/w92/')} alt="" className="aspect-[2/3] w-7 shrink-0 rounded-sm object-cover" />
        ) : (
          <span className="aspect-[2/3] w-7 shrink-0 rounded-sm bg-muted" aria-hidden="true" />
        )}
        <span className="flex min-w-0 flex-col gap-1">
          <span className="truncate">
            {film.title}
            {film.year && <span className="text-muted-foreground"> ({film.year})</span>}
          </span>
          <span className="flex flex-wrap gap-1">
            {film.reasons.map((reason) => (
              <Badge key={reason} variant="outline" className="px-1.5 py-0 text-[11px] font-normal">
                {t(`playback.link.reasons.${reason}`)}
              </Badge>
            ))}
          </span>
        </span>
      </span>
    </label>
  )
}
