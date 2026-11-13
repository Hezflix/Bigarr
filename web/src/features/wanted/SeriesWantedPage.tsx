import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { cn } from 'cn'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ApiError, seriesWanted, type SeriesWantedItem } from '@/api/client'
import { TableCell, TableHead, TableRow } from '@/components/ui/table'
import { AutomaticSearchButton } from '@/features/search/SearchButtons'
import { WantedFrame, wantedPageSize, WantedTitle, WantedVersionCells, WantedVersionHeads, type WantedFilterChoice } from '@/features/wanted/wantedTable'
import { useSchedulerEnabled } from '@/hooks/useSchedulerEnabled'
import { formatDate, formatDateTime } from '@/i18n'

const code = (item: SeriesWantedItem) => `S${String(item.seasonNumber).padStart(2, '0')}E${String(item.episodeNumber).padStart(2, '0')}`

/** Sonarr's Wanted pages for series: aired monitored episodes per version, missing or below the cutoff, most recently aired first. */
export function SeriesWantedPage() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [filter, setFilter] = useState<WantedFilterChoice>('all')
  const [page, setPage] = useState(1)
  const kind = filter === 'all' ? undefined : filter
  const list = useQuery({ queryKey: ['seriesWanted', kind ?? 'all', page], queryFn: () => seriesWanted.list(kind, page, wantedPageSize), refetchInterval: 60_000 })
  const schedulerOff = useSchedulerEnabled() === false
  const queued = (result: { queued: number }) => {
    toast.success(t('wanted.queued', { count: result.queued }))
    void queryClient.invalidateQueries({ queryKey: ['seriesWanted'] })
  }
  const fail = (e: unknown) => toast.error(e instanceof ApiError ? e.message : t('search.failed'))
  const searchOne = useMutation({ mutationFn: (item: SeriesWantedItem) => seriesWanted.search([item.targetId]), onSuccess: queued, onError: fail })
  const searchAll = useMutation({ mutationFn: () => seriesWanted.search(undefined, kind), onSuccess: queued, onError: fail })

  const data = list.data

  return (
    <WantedFrame
      testId="series-wanted"
      title={t('seriesWanted.title')}
      hint={t(schedulerOff ? 'seriesWanted.hintManual' : 'seriesWanted.hint')}
      empty={t('seriesWanted.empty')}
      count={t('seriesWanted.count', { count: data?.total ?? 0 })}
      filter={filter}
      onFilter={(value) => {
        setFilter(value)
        setPage(1)
      }}
      page={page}
      onPage={setPage}
      total={data?.total}
      loading={list.isPending}
      onSearchAll={() => searchAll.mutate()}
      searchingAll={searchAll.isPending}
      head={
        <>
          <TableHead>{t('seriesWanted.series')}</TableHead>
          <TableHead>{t('seriesWanted.episode')}</TableHead>
          <TableHead>{t('seriesWanted.aired')}</TableHead>
          <WantedVersionHeads schedulerOff={schedulerOff} />
        </>
      }
    >
      {data?.items.map((item) => (
        <TableRow key={item.targetId} data-testid={`series-wanted-${item.targetId}`}>
          <TableCell className="align-top">
            <Link to="/series/$seriesId" params={{ seriesId: String(item.seriesId) }} className="flex items-center gap-3 underline-offset-4 hover:underline">
              <WantedTitle posterUrl={item.posterUrl} title={item.seriesTitle} year={item.year} />
            </Link>
          </TableCell>
          <TableCell className="min-w-[12rem] align-top whitespace-normal">
            <span className="font-medium tabular-nums">{code(item)}</span>
            {item.absoluteEpisodeNumber !== null && <span className="ml-1 text-xs text-muted-foreground tabular-nums">({item.absoluteEpisodeNumber})</span>}
            <span className="block text-sm">{item.episodeTitle}</span>
          </TableCell>
          <TableCell className="align-top whitespace-nowrap tabular-nums">{formatDate(item.airDateUtc)}</TableCell>
          <WantedVersionCells item={item} />
          {!schedulerOff && (
            <TableCell className={cn('align-top whitespace-nowrap', !item.nextSearchAt && 'text-muted-foreground')}>
              {item.nextSearchAt && new Date(item.nextSearchAt) > new Date() ? formatDateTime(item.nextSearchAt) : t('wanted.due')}
            </TableCell>
          )}
          <TableCell className="align-top">
            <div className="flex justify-end">
              <AutomaticSearchButton
                iconOnly
                target={`${item.seriesTitle} ${code(item)}`}
                onClick={() => searchOne.mutate(item)}
                pending={searchOne.isPending && searchOne.variables?.targetId === item.targetId}
                data-testid={`series-wanted-search-${item.targetId}`}
              />
            </div>
          </TableCell>
        </TableRow>
      ))}
    </WantedFrame>
  )
}
