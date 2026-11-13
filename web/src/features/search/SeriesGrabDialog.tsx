import { useState } from 'react'
import { cn } from 'cn'
import { ChevronRightIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import type { Episode } from '@/api/client'
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
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { GrabReasons } from '@/features/search/releaseTable'
import { episodeCode, pickerSeasons } from './seriesGrabScope'

/**
 * A series grab waiting for its episodes. Every grab asks: the search scope only decides what is listed, the episodes a
 * release goes to are always the user's pick, started from Bigarr's match (`matched`) or from the search scope.
 */
export type PendingSeriesGrab = {
  title: string
  version: string
  /** Why the search would not pick it for this version: shown, and the button says "anyway". */
  reasons: string[]
  /** The episodes the version can take, film-owned specials left out. */
  episodes: Episode[]
  /** Ticked when the dialog opens. */
  prefill: number[]
  /** Bigarr's match, as the search labels it (`S01E01–S01E07 (7 episodes)`); null when it maps to no episode. */
  matched: string | null
  go: (episodeIds: number[]) => void
}

export function SeriesGrabDialog({ pending, onClose }: { pending: PendingSeriesGrab | null; onClose: () => void }) {
  return (
    <AlertDialog open={pending !== null} onOpenChange={(open) => !open && onClose()}>
      {/* Keyed so the ticks start over with each release. */}
      {pending && <SeriesGrabContent key={`${pending.title}:${pending.version}`} pending={pending} onClose={onClose} />}
    </AlertDialog>
  )
}

function SeriesGrabContent({ pending, onClose }: { pending: PendingSeriesGrab; onClose: () => void }) {
  const { t } = useTranslation()
  const [selected, setSelected] = useState(() => new Set(pending.prefill))
  const seasons = pickerSeasons(pending.episodes)
  const prefilledSeasons = new Set(pending.episodes.filter((e) => pending.prefill.includes(e.id)).map((e) => e.seasonNumber))
  // Seasons holding the prefill start open, the others folded; a single season is always open.
  const [open, setOpen] = useState(() => new Set(seasons.length === 1 ? [seasons[0].season] : prefilledSeasons))
  const set = (ids: number[], on: boolean) =>
    setSelected((current) => {
      const next = new Set(current)
      for (const id of ids) {
        if (on) next.add(id)
        else next.delete(id)
      }
      return next
    })
  const toggleOpen = (season: number) =>
    setOpen((current) => {
      const next = new Set(current)
      if (!next.delete(season)) next.add(season)
      return next
    })
  // In the episodes' order, whatever order they were ticked in.
  const picked = pending.episodes.filter((e) => selected.has(e.id)).map((e) => e.id)
  const hint = pending.matched ? t('seriesSearch.pickMatched', { episodes: pending.matched }) : pending.prefill.length > 0 ? t('seriesSearch.pickScope') : t('seriesSearch.pickNone')
  const anyway = pending.reasons.length > 0

  return (
    <AlertDialogContent className="flex max-h-[90vh] flex-col sm:max-w-xl" data-testid="series-grab-dialog">
      <AlertDialogHeader>
        <AlertDialogTitle>{anyway ? t('search.confirmTitle', { version: pending.version }) : t('seriesSearch.pickTitle', { version: pending.version })}</AlertDialogTitle>
        <AlertDialogDescription className="break-all">{pending.title}</AlertDialogDescription>
      </AlertDialogHeader>
      {anyway && <GrabReasons reasons={pending.reasons} />}
      <p className="text-sm" data-testid="series-grab-hint">
        {hint}
      </p>
      <div className="flex items-center gap-2 text-sm">
        <span className="tabular-nums text-muted-foreground" data-testid="series-grab-count">
          {t('seriesSearch.pickSelected', { count: picked.length })}
        </span>
        <Button variant="ghost" size="sm" className="ml-auto" onClick={() => setSelected(new Set(pending.prefill))} disabled={pending.prefill.length === 0}>
          {t('seriesSearch.pickReset')}
        </Button>
        <Button variant="ghost" size="sm" onClick={() => setSelected(new Set())} disabled={picked.length === 0}>
          {t('seriesSearch.pickClear')}
        </Button>
      </div>
      {seasons.length === 0 ? (
        <p className="text-sm text-muted-foreground">{t('seriesSearch.pickEmpty')}</p>
      ) : (
        <ul className="min-h-0 flex-1 overflow-y-auto rounded-lg border" data-testid="series-grab-episodes">
          {seasons.map(({ season, episodes }) => {
            const name = season === 0 ? t('seriesSearch.pickSpecials') : t('seriesSearch.pickSeason', { season })
            const ids = episodes.map((e) => e.id)
            const count = ids.filter((id) => selected.has(id)).length
            const expanded = open.has(season)
            return (
              <li key={season} className="border-b last:border-b-0">
                <div className="flex items-center gap-2 bg-muted/40 px-3 py-1.5">
                  <Checkbox
                    checked={count === ids.length ? true : count > 0 ? 'indeterminate' : false}
                    onCheckedChange={(checked) => set(ids, checked === true)}
                    aria-label={name}
                    data-testid={`series-grab-season-${season}`}
                  />
                  <button type="button" className="flex flex-1 items-center gap-1 text-left text-sm font-medium" onClick={() => toggleOpen(season)} aria-expanded={expanded} title={t('seriesSearch.pickToggleSeason', { season: name })}>
                    <ChevronRightIcon className={cn('size-4 transition-transform', expanded && 'rotate-90')} />
                    {name}
                    <span className="ml-auto text-xs font-normal tabular-nums text-muted-foreground">{t('seriesSearch.pickSeasonCount', { selected: count, count: ids.length })}</span>
                  </button>
                </div>
                {expanded && (
                  <ul>
                    {episodes.map((episode) => (
                      <li key={episode.id}>
                        <label className="flex cursor-pointer items-center gap-2 px-3 py-1 pl-9 text-sm hover:bg-muted/30">
                          <Checkbox checked={selected.has(episode.id)} onCheckedChange={(checked) => set([episode.id], checked === true)} data-testid={`series-grab-episode-${episode.id}`} />
                          <span className="text-xs tabular-nums text-muted-foreground">{episodeCode(episode)}</span>
                          <span className="truncate">{episode.title}</span>
                        </label>
                      </li>
                    ))}
                  </ul>
                )}
              </li>
            )
          })}
        </ul>
      )}
      <AlertDialogFooter>
        <AlertDialogCancel>{t('common.cancel')}</AlertDialogCancel>
        <AlertDialogAction
          disabled={picked.length === 0}
          onClick={() => {
            pending.go(picked)
            onClose()
          }}
          data-testid="series-grab-go"
        >
          {t(anyway ? 'seriesSearch.pickGrabAnyway' : 'seriesSearch.pickGrab', { count: picked.length })}
        </AlertDialogAction>
      </AlertDialogFooter>
    </AlertDialogContent>
  )
}
