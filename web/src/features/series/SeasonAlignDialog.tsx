import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { cn } from 'cn'
import { LayersIcon, PinIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ApiError, series as seriesApi } from '@/api/client'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Label } from '@/components/ui/label'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { formatDateTime } from '@/i18n'

/**
 * "Align season": one release group per season, as Sonarr users keep it by hand. The button shows on a season with a
 * group (most files, at least 3, or the one the user chose) and episodes from another one, or files from several groups;
 * the dialog lets the user choose the group (a chip per group), is the dry run (which episodes, from which group to
 * which) and its button runs the search that takes only the group's releases.
 */
export function SeasonAlignButton({ group, count, season, kept, onClick }: { group: string | null; count: number; season: string; kept?: boolean; onClick: () => void }) {
  const { t } = useTranslation()
  // No group yet (a tie, or under 3 files): the dialog is where one is chosen.
  const name = group && count > 0 ? t('seasonAlign.button', { season, group, count }) : t('seasonAlign.buttonChoose', { season })
  return (
    <Tooltip>
      <TooltipTrigger asChild>
        <Button type="button" variant="ghost" size="sm" className={cn('h-7 gap-1 px-1.5 text-xs sm:px-2', kept && 'text-muted-foreground')} aria-label={name} onClick={onClick} data-testid="season-align">
          <LayersIcon aria-hidden="true" />
          {/* The count where there is room; a phone keeps the icon (its name says how many). */}
          {count > 0 && <span className="hidden tabular-nums sm:inline">{count}</span>}
        </Button>
      </TooltipTrigger>
      <TooltipContent className="max-w-72">
        <p className="font-medium">{name}</p>
        {kept && <p>{t('seasonAlign.kept')}</p>}
        {t('seasonAlign.hint')}
      </TooltipContent>
    </Tooltip>
  )
}

type Props = {
  seriesId: number
  versionId: number
  versionName: string
  seasonNumber: number
  seasonName: string
  open: boolean
  onOpenChange: (open: boolean) => void
  onDone: () => void
}

export function SeasonAlignDialog({ seriesId, versionId, versionName, seasonNumber, seasonName, open, onOpenChange, onDone }: Props) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const key = ['seasonAlignment', seriesId, versionId, seasonNumber]
  const keep = useMutation({
    mutationFn: (keepAsIs: boolean) => seriesApi.keepSeasonGroups(seriesId, versionId, seasonNumber, keepAsIs),
    onSuccess: (next) => {
      queryClient.setQueryData(key, next)
      onDone()
    },
    onError: (e) => toast.error(e instanceof ApiError ? e.message : t('common.unexpectedError')),
  })
  const pin = useMutation({
    mutationFn: (group: string | null) => seriesApi.pinSeasonGroup(seriesId, versionId, seasonNumber, group),
    onSuccess: (next) => {
      queryClient.setQueryData(key, next)
      onDone()
    },
    onError: (e) => toast.error(e instanceof ApiError ? e.message : t('common.unexpectedError')),
  })
  const preview = useQuery({
    queryKey: key,
    queryFn: () => seriesApi.alignment(seriesId, versionId, seasonNumber),
    enabled: open,
    staleTime: 0,
  })
  const align = useMutation({
    mutationFn: () => seriesApi.align(seriesId, versionId, seasonNumber),
    onSuccess: (result) => {
      const search = result.search
      if (!search) toast.info(t('seasonAlign.nothing'))
      else if (search.grabbed > 0) toast.success(t('seasonAlign.grabbed', { count: search.covered, group: result.preview.seasonGroup }))
      else if (search.picked > 0) toast.success(t('seasonAlign.picked', { count: search.covered, group: result.preview.seasonGroup }))
      else toast.info(t('seasonAlign.notFound', { group: result.preview.seasonGroup, detail: search.detail ?? '' }))
      onOpenChange(false)
      onDone()
    },
    onError: (e) => toast.error(e instanceof ApiError ? e.message : t('search.failed')),
  })
  const data = preview.data

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-xl" data-testid="season-align-dialog">
        <DialogHeader>
          <DialogTitle>{t('seasonAlign.title', { season: seasonName })}</DialogTitle>
          <DialogDescription>{t('seasonAlign.description', { version: versionName })}</DialogDescription>
        </DialogHeader>
        {preview.isLoading || !data ? (
          <div className="flex flex-col gap-2">
            <Skeleton className="h-5 w-full" />
            <Skeleton className="h-5 w-2/3" />
          </div>
        ) : (
          <div className="flex min-w-0 flex-col gap-3 text-sm">
            {data.groups.length > 0 && (
              <div className="flex flex-col gap-1.5">
                {/* One toggle per group: pressing another than the season's pins it, the majority's chip unpins. */}
                <fieldset className="min-w-0" data-testid="season-align-groups">
                  <legend className="mb-1.5 text-xs text-muted-foreground">{seasonNumber > 0 ? t('seasonAlign.chooseGroup') : t('seasonAlign.groupsLabel')}</legend>
                  <div className="flex flex-wrap items-center gap-1.5">
                    {data.groups.map((g) => {
                      const chosen = data.seasonGroup !== null && sameGroup(g.group, data.seasonGroup)
                      const majority = data.majorityGroup !== null && sameGroup(g.group, data.majorityGroup)
                      const pinned = data.pinnedGroup !== null && sameGroup(g.group, data.pinnedGroup)
                      return (
                        <button
                          key={g.group}
                          type="button"
                          aria-pressed={chosen}
                          disabled={seasonNumber <= 0 || pin.isPending}
                          onClick={() => !chosen && pin.mutate(majority ? null : g.group)}
                          className={cn(
                            'inline-flex min-h-8 max-w-full items-center gap-1.5 rounded-full border px-3 py-1 text-xs tabular-nums transition-colors focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none disabled:cursor-default',
                            chosen ? 'border-primary bg-primary text-primary-foreground' : 'border-border bg-secondary text-secondary-foreground hover:bg-secondary/70',
                          )}
                          data-testid="season-align-group"
                          data-group={g.group}
                        >
                          <span className="truncate">{t('seasonAlign.groupFiles', { group: g.group, count: g.files })}</span>
                          {majority && <span className={cn('rounded-full px-1.5 text-[10px] uppercase', chosen ? 'bg-primary-foreground/20' : 'bg-background/60')}>{t('seasonAlign.majorityTag')}</span>}
                          {pinned && <PinIcon className="size-3" aria-label={t('seasonAlign.pinnedTag')} />}
                        </button>
                      )
                    })}
                  </div>
                </fieldset>
                {data.pinnedGroup && (
                  <p className="text-xs text-muted-foreground" data-testid="season-align-pinned">
                    {t('seasonAlign.pinnedNote', { group: data.pinnedGroup })}{' '}
                    <button type="button" className="underline underline-offset-2 hover:text-foreground" onClick={() => pin.mutate(null)} disabled={pin.isPending} data-testid="season-align-unpin">
                      {data.majorityGroup ? t('seasonAlign.unpinTo', { group: data.majorityGroup }) : t('seasonAlign.unpin')}
                    </button>
                  </p>
                )}
              </div>
            )}
            {!data.seasonGroup ? (
              <p className="text-muted-foreground">{data.reason}</p>
            ) : data.episodes.length === 0 ? (
              <p className="text-muted-foreground">{t('seasonAlign.aligned', { group: data.seasonGroup })}</p>
            ) : (
              <>
                <p>{t('seasonAlign.plan', { count: data.episodes.length, group: data.seasonGroup })}</p>
                <div className="max-h-72 overflow-y-auto rounded-md border">
                  <Table>
                    <TableHeader>
                      <TableRow>
                        <TableHead className="w-20">{t('seasonAlign.episode')}</TableHead>
                        <TableHead>{t('seasonAlign.from')}</TableHead>
                        <TableHead>{t('seasonAlign.to')}</TableHead>
                        <TableHead className="hidden sm:table-cell">{t('seasonAlign.quality')}</TableHead>
                      </TableRow>
                    </TableHeader>
                    <TableBody>
                      {data.episodes.map((e) => (
                        <TableRow key={e.episodeId} data-testid="season-align-episode">
                          <TableCell className="font-mono text-xs">{e.label}</TableCell>
                          <TableCell className="max-w-44 whitespace-normal break-words" title={e.title ?? undefined}>
                            {e.currentGroup ?? <span className="text-muted-foreground">{t('seasonAlign.unknownGroup')}</span>}
                            {e.isPick && <span className="ml-1 text-xs text-muted-foreground">{t('seasonAlign.pick')}</span>}
                            {data.autoAlign && !data.keepAsIs && (e.gaveUp || e.givesUpAt) && (
                              <span className="block text-xs text-muted-foreground" data-testid="season-align-giveup">
                                {e.gaveUp ? t('seasonAlign.gaveUp') : t('seasonAlign.givesUp', { date: formatDateTime(e.givesUpAt) })}
                              </span>
                            )}
                          </TableCell>
                          <TableCell className="font-medium">{data.seasonGroup}</TableCell>
                          <TableCell className="hidden text-muted-foreground sm:table-cell">{e.quality}</TableCell>
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>
                </div>
                <p className="text-xs text-muted-foreground">{t('seasonAlign.rules')}</p>
              </>
            )}
            {data.seasonGroup && (
              <div className="flex items-start gap-2 border-t pt-3">
                <Checkbox
                  id="season-align-keep"
                  checked={data.keepAsIs}
                  disabled={keep.isPending}
                  onCheckedChange={(v) => keep.mutate(v === true)}
                  className="mt-0.5"
                  data-testid="season-align-keep"
                />
                <div className="grid gap-0.5">
                  <Label htmlFor="season-align-keep" className="font-normal">
                    {t('seasonAlign.keepAsIs')}
                  </Label>
                  <span className="text-xs text-muted-foreground">{data.autoAlign || data.keepAsIs ? t('seasonAlign.keepAsIsHint') : t('seasonAlign.autoOff')}</span>
                </div>
              </div>
            )}
          </div>
        )}
        <DialogFooter>
          <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
            {t('common.cancel')}
          </Button>
          <Button
            type="button"
            onClick={() => align.mutate()}
            disabled={!data?.seasonGroup || data.episodes.length === 0 || align.isPending}
            aria-busy={align.isPending || undefined}
            data-testid="season-align-confirm"
          >
            {align.isPending ? t('search.searching') : t('seasonAlign.confirm', { count: data?.episodes.length ?? 0 })}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

const sameGroup = (a: string, b: string) => a.trim().toLowerCase() === b.trim().toLowerCase()
