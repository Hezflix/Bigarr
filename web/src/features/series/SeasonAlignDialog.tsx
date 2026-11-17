import { useMutation, useQuery } from '@tanstack/react-query'
import { LayersIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ApiError, series as seriesApi } from '@/api/client'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'

/**
 * "Align season": one release group per season, as Sonarr users keep it by hand. The button shows on a season with a
 * group (most files, at least 3) and episodes from another one; the dialog is the dry run (which episodes, from which
 * group to which) and its button runs the search that takes only the group's releases.
 */
export function SeasonAlignButton({ group, count, season, onClick }: { group: string; count: number; season: string; onClick: () => void }) {
  const { t } = useTranslation()
  const name = t('seasonAlign.button', { season, group, count })
  return (
    <Tooltip>
      <TooltipTrigger asChild>
        <Button type="button" variant="ghost" size="sm" className="h-7 gap-1 px-1.5 text-xs sm:px-2" aria-label={name} onClick={onClick} data-testid="season-align">
          <LayersIcon aria-hidden="true" />
          {/* The count where there is room; a phone keeps the icon (its name says how many). */}
          <span className="hidden tabular-nums sm:inline">{count}</span>
        </Button>
      </TooltipTrigger>
      <TooltipContent className="max-w-72">
        <p className="font-medium">{name}</p>
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
  const preview = useQuery({
    queryKey: ['seasonAlignment', seriesId, versionId, seasonNumber],
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
            <div className="flex flex-wrap items-center gap-1.5">
              {data.groups.map((g) => (
                <Badge key={g.group} variant={g.group === data.seasonGroup ? 'default' : 'secondary'} className="tabular-nums">
                  {t('seasonAlign.groupFiles', { group: g.group, count: g.files })}
                </Badge>
              ))}
            </div>
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
                          <TableCell className="max-w-40 truncate" title={e.title ?? undefined}>
                            {e.currentGroup ?? <span className="text-muted-foreground">{t('seasonAlign.unknownGroup')}</span>}
                            {e.isPick && <span className="ml-1 text-xs text-muted-foreground">{t('seasonAlign.pick')}</span>}
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
