import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { cn } from 'cn'
import { SearchIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ApiError, seriesWanted, type SeriesWantedItem, type WantedFilter } from '@/api/client'
import { Poster } from '@/components/Poster'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { useSchedulerEnabled } from '@/hooks/useSchedulerEnabled'
import { formatAge, formatDate, formatDateTime } from '@/i18n'

const pageSize = 50

const kindStyles: Record<WantedFilter, string> = {
  missing: 'border-amber-600/30 bg-amber-500/15 text-amber-700 dark:border-amber-400/30 dark:text-amber-300',
  cutoffUnmet: 'border-blue-600/30 bg-blue-500/15 text-blue-700 dark:border-blue-400/30 dark:text-blue-300',
}

const code = (item: SeriesWantedItem) => `S${String(item.seasonNumber).padStart(2, '0')}E${String(item.episodeNumber).padStart(2, '0')}`

/** Sonarr's Wanted pages for series: aired monitored episodes per version, missing or below the cutoff, most recently aired first. */
export function SeriesWantedPage() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [filter, setFilter] = useState<WantedFilter | 'all'>('all')
  const [page, setPage] = useState(1)
  const kind = filter === 'all' ? undefined : filter
  const list = useQuery({ queryKey: ['seriesWanted', kind ?? 'all', page], queryFn: () => seriesWanted.list(kind, page, pageSize), refetchInterval: 60_000 })
  const schedulerOff = useSchedulerEnabled() === false
  const queued = (result: { queued: number }) => {
    toast.success(t('wanted.queued', { count: result.queued }))
    void queryClient.invalidateQueries({ queryKey: ['seriesWanted'] })
  }
  const fail = (e: unknown) => toast.error(e instanceof ApiError ? e.message : t('search.failed'))
  const searchOne = useMutation({ mutationFn: (item: SeriesWantedItem) => seriesWanted.search([item.targetId]), onSuccess: queued, onError: fail })
  const searchAll = useMutation({ mutationFn: () => seriesWanted.search(undefined, kind), onSuccess: queued, onError: fail })

  const data = list.data
  const pages = data ? Math.max(1, Math.ceil(data.total / pageSize)) : 1

  return (
    <>
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="text-2xl font-semibold tracking-tight">{t('seriesWanted.title')}</h1>
        <div className="ml-auto flex flex-wrap items-center gap-2">
          <div className="flex rounded-md border p-0.5" role="tablist" aria-label={t('wanted.filter')}>
            {(['all', 'missing', 'cutoffUnmet'] as const).map((value) => (
              <Button
                key={value}
                type="button"
                role="tab"
                aria-selected={filter === value}
                variant={filter === value ? 'secondary' : 'ghost'}
                size="sm"
                onClick={() => {
                  setFilter(value)
                  setPage(1)
                }}
                data-testid={`series-wanted-filter-${value}`}
              >
                {t(`wanted.filters.${value}`)}
              </Button>
            ))}
          </div>
          <Button type="button" size="sm" onClick={() => searchAll.mutate()} disabled={searchAll.isPending || !data || data.total === 0} data-testid="series-wanted-search-all">
            <SearchIcon />
            {t('wanted.searchAll')}
          </Button>
        </div>
      </div>
      <p className="max-w-3xl text-sm text-muted-foreground">{t(schedulerOff ? 'seriesWanted.hintManual' : 'seriesWanted.hint')}</p>

      {list.isPending && (
        <div className="flex flex-col gap-2">
          <Skeleton className="h-8 w-full" />
          <Skeleton className="h-8 w-full" />
        </div>
      )}

      {data && data.total === 0 && <p className="text-sm text-muted-foreground">{t('seriesWanted.empty')}</p>}

      {data && data.total > 0 && (
        <>
          <p className="text-sm text-muted-foreground" data-testid="series-wanted-count">
            {t('seriesWanted.count', { count: data.total })}
          </p>
          <div className="overflow-x-auto rounded-lg border">
            <Table data-testid="series-wanted-table">
              <TableHeader>
                <TableRow>
                  <TableHead>{t('seriesWanted.series')}</TableHead>
                  <TableHead>{t('seriesWanted.episode')}</TableHead>
                  <TableHead>{t('seriesWanted.aired')}</TableHead>
                  <TableHead>{t('wanted.version')}</TableHead>
                  <TableHead>{t('wanted.kind')}</TableHead>
                  <TableHead>{t('wanted.current')}</TableHead>
                  <TableHead>{t('wanted.cutoff')}</TableHead>
                  <TableHead>{t('wanted.lastSearch')}</TableHead>
                  {!schedulerOff && <TableHead>{t('wanted.nextSearch')}</TableHead>}
                  <TableHead className="text-right">{t('versions.actions')}</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {data.items.map((item) => (
                  <TableRow key={item.targetId} data-testid={`series-wanted-${item.targetId}`}>
                    <TableCell className="align-top">
                      <Link to="/series/$seriesId" params={{ seriesId: String(item.seriesId) }} className="flex items-center gap-3 underline-offset-4 hover:underline">
                        <Poster url={item.posterUrl} title={item.seriesTitle} size="small" />
                        <span className="font-medium">
                          {item.seriesTitle}
                          {item.year ? <span className="font-normal text-muted-foreground"> ({item.year})</span> : null}
                        </span>
                      </Link>
                    </TableCell>
                    <TableCell className="min-w-[12rem] align-top whitespace-normal">
                      <span className="font-medium tabular-nums">{code(item)}</span>
                      {item.absoluteEpisodeNumber !== null && <span className="ml-1 text-xs text-muted-foreground tabular-nums">({item.absoluteEpisodeNumber})</span>}
                      <span className="block text-sm">{item.episodeTitle}</span>
                    </TableCell>
                    <TableCell className="align-top whitespace-nowrap tabular-nums">{formatDate(item.airDateUtc)}</TableCell>
                    <TableCell className="align-top">
                      <span className="font-medium">{item.versionName}</span>
                      <span className="block text-xs text-muted-foreground">{item.qualityProfile}</span>
                    </TableCell>
                    <TableCell className="align-top">
                      <Badge variant="outline" className={kindStyles[item.kind]}>
                        {t(`wanted.filters.${item.kind}`)}
                      </Badge>
                    </TableCell>
                    <TableCell className="align-top whitespace-nowrap">
                      {item.currentQuality ? (
                        <>
                          {item.currentQuality}
                          {item.currentScore !== null && <span className="block text-xs text-muted-foreground">{t('versions.score', { score: item.currentScore })}</span>}
                        </>
                      ) : (
                        <span className="text-muted-foreground">{t('common.none')}</span>
                      )}
                    </TableCell>
                    <TableCell className="align-top whitespace-nowrap">{item.cutoffQuality}</TableCell>
                    <TableCell className="align-top whitespace-nowrap">
                      {item.lastSearchAt ? (
                        <Tooltip>
                          <TooltipTrigger asChild>
                            <span>
                              {formatAge(item.lastSearchAt)}
                              {item.searchAttempts > 0 && <span className="block text-xs text-muted-foreground">{t('wanted.attempts', { count: item.searchAttempts })}</span>}
                            </span>
                          </TooltipTrigger>
                          <TooltipContent>{formatDateTime(item.lastSearchAt)}</TooltipContent>
                        </Tooltip>
                      ) : (
                        <span className="text-muted-foreground">{t('wanted.never')}</span>
                      )}
                    </TableCell>
                    {!schedulerOff && (
                      <TableCell className={cn('align-top whitespace-nowrap', !item.nextSearchAt && 'text-muted-foreground')}>
                        {item.nextSearchAt && new Date(item.nextSearchAt) > new Date() ? formatDateTime(item.nextSearchAt) : t('wanted.due')}
                      </TableCell>
                    )}
                    <TableCell className="align-top">
                      <div className="flex justify-end">
                        <Button
                          type="button"
                          variant="ghost"
                          size="icon"
                          onClick={() => searchOne.mutate(item)}
                          disabled={searchOne.isPending}
                          aria-label={t('seriesWanted.searchEpisode', { episode: code(item) })}
                          title={t('seriesWanted.searchEpisode', { episode: code(item) })}
                          data-testid={`series-wanted-search-${item.targetId}`}
                        >
                          <SearchIcon />
                        </Button>
                      </div>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
          {pages > 1 && (
            <div className="flex items-center justify-end gap-2 text-sm">
              <Button type="button" variant="outline" size="sm" onClick={() => setPage((p) => Math.max(1, p - 1))} disabled={page <= 1}>
                {t('common.previous')}
              </Button>
              <span className="text-muted-foreground">{t('common.page', { page, pages })}</span>
              <Button type="button" variant="outline" size="sm" onClick={() => setPage((p) => Math.min(pages, p + 1))} disabled={page >= pages}>
                {t('common.next')}
              </Button>
            </div>
          )}
        </>
      )}
    </>
  )
}
