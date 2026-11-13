import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { cn } from 'cn'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ApiError, search, wanted, type WantedItem } from '@/api/client'
import { TableCell, TableHead, TableRow } from '@/components/ui/table'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { AutomaticSearchButton } from '@/features/search/SearchButtons'
import { toastSearchError, toastSearchResults } from '@/features/search/searchOutcome'
import { WantedFrame, wantedPageSize, WantedTitle, WantedVersionCells, WantedVersionHeads, type WantedFilterChoice } from '@/features/wanted/wantedTable'
import { useSchedulerEnabled } from '@/hooks/useSchedulerEnabled'
import { formatDateTime } from '@/i18n'

/** What the library still wants: missing versions and files below their profile cutoff, with the search backoff. */
export function WantedPage() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [filter, setFilter] = useState<WantedFilterChoice>('all')
  const [page, setPage] = useState(1)
  const kind = filter === 'all' ? undefined : filter
  const list = useQuery({ queryKey: ['wanted', kind ?? 'all', page], queryFn: () => wanted.list(kind, page, wantedPageSize), refetchInterval: 60_000 })
  // Scheduler off: no automatic search will come, so no "next automatic search" column.
  const schedulerOff = useSchedulerEnabled() === false
  const invalidate = () => {
    void queryClient.invalidateQueries({ queryKey: ['wanted'] })
    void queryClient.invalidateQueries({ queryKey: ['movies'] })
    void queryClient.invalidateQueries({ queryKey: ['queue'] })
  }

  const searchOne = useMutation({
    mutationFn: (item: WantedItem) => search.automatic(item.movieId, item.versionId),
    onSuccess: (results) => {
      toastSearchResults(t, results)
      invalidate()
    },
    onError: (e) => toastSearchError(t, e),
  })
  const searchAll = useMutation({
    mutationFn: () => wanted.searchAll(kind),
    onSuccess: (result) => toast.success(t('wanted.queued', { count: result.queued })),
    onError: (e) => toast.error(e instanceof ApiError ? e.message : t('search.failed')),
  })

  const data = list.data

  return (
    <WantedFrame
      testId="wanted"
      title={t('wanted.title')}
      hint={t(schedulerOff ? 'wanted.hintManual' : 'wanted.hint')}
      empty={t('wanted.empty')}
      count={t('wanted.count', { count: data?.total ?? 0 })}
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
          <TableHead>{t('wanted.movie')}</TableHead>
          <WantedVersionHeads schedulerOff={schedulerOff} />
        </>
      }
    >
      {data?.items.map((item) => (
        <TableRow key={item.versionId} data-testid={`wanted-${item.versionId}`}>
          <TableCell className="align-top">
            <Link to="/movies/$movieId" params={{ movieId: String(item.movieId) }} className="flex items-center gap-3 underline-offset-4 hover:underline">
              <WantedTitle posterUrl={item.posterUrl} title={item.movieTitle} year={item.year} />
            </Link>
          </TableCell>
          <WantedVersionCells item={item} />
          {!schedulerOff && (
            <TableCell className={cn('align-top whitespace-nowrap', !item.nextSearchAt && 'text-muted-foreground')}>
              {item.nextSearchAt ? (
                <Tooltip>
                  <TooltipTrigger asChild>
                    <span>{new Date(item.nextSearchAt) <= new Date() ? t('wanted.due') : formatDateTime(item.nextSearchAt)}</span>
                  </TooltipTrigger>
                  <TooltipContent>{t('wanted.nextSearchHint')}</TooltipContent>
                </Tooltip>
              ) : (
                t('wanted.due')
              )}
            </TableCell>
          )}
          <TableCell className="align-top">
            <div className="flex justify-end">
              <AutomaticSearchButton
                iconOnly
                target={`${item.movieTitle} · ${item.versionName}`}
                onClick={() => searchOne.mutate(item)}
                pending={searchOne.isPending && searchOne.variables?.versionId === item.versionId}
                data-testid={`wanted-search-${item.versionId}`}
              />
            </div>
          </TableCell>
        </TableRow>
      ))}
    </WantedFrame>
  )
}
