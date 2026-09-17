import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { cn } from 'cn'
import { SearchIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ApiError, search, wanted, type WantedFilter, type WantedItem } from '@/api/client'
import { Poster } from '@/components/Poster'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { toastSearchError, toastSearchResults } from '@/features/search/searchOutcome'
import { formatAge, formatDateTime } from '@/i18n'

const pageSize = 50

const kindStyles: Record<WantedFilter, string> = {
  missing: 'border-amber-600/30 bg-amber-500/15 text-amber-700 dark:border-amber-400/30 dark:text-amber-300',
  cutoffUnmet: 'border-blue-600/30 bg-blue-500/15 text-blue-700 dark:border-blue-400/30 dark:text-blue-300',
}

/** What the library still wants (blueprint §5): missing versions and files below their profile cutoff, with the search backoff. */
export function WantedPage() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [filter, setFilter] = useState<WantedFilter | 'all'>('all')
  const [page, setPage] = useState(1)
  const kind = filter === 'all' ? undefined : filter
  const list = useQuery({ queryKey: ['wanted', kind ?? 'all', page], queryFn: () => wanted.list(kind, page, pageSize), refetchInterval: 60_000 })
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
  const pages = data ? Math.max(1, Math.ceil(data.total / pageSize)) : 1

  return (
    <>
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="text-2xl font-semibold tracking-tight">{t('wanted.title')}</h1>
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
                data-testid={`wanted-filter-${value}`}
              >
                {t(`wanted.filters.${value}`)}
              </Button>
            ))}
          </div>
          <Button type="button" size="sm" onClick={() => searchAll.mutate()} disabled={searchAll.isPending || !data || data.total === 0} data-testid="wanted-search-all">
            <SearchIcon />
            {t('wanted.searchAll')}
          </Button>
        </div>
      </div>
      <p className="max-w-3xl text-sm text-muted-foreground">{t('wanted.hint')}</p>

      {list.isPending && (
        <div className="flex flex-col gap-2">
          <Skeleton className="h-8 w-full" />
          <Skeleton className="h-8 w-full" />
        </div>
      )}

      {data && data.total === 0 && <p className="text-sm text-muted-foreground">{t('wanted.empty')}</p>}

      {data && data.total > 0 && (
        <>
          <p className="text-sm text-muted-foreground" data-testid="wanted-count">
            {t('wanted.count', { count: data.total })}
          </p>
          <div className="overflow-x-auto rounded-lg border">
            <Table data-testid="wanted-table">
              <TableHeader>
                <TableRow>
                  <TableHead>{t('wanted.movie')}</TableHead>
                  <TableHead>{t('wanted.version')}</TableHead>
                  <TableHead>{t('wanted.kind')}</TableHead>
                  <TableHead>{t('wanted.current')}</TableHead>
                  <TableHead>{t('wanted.cutoff')}</TableHead>
                  <TableHead>{t('wanted.lastSearch')}</TableHead>
                  <TableHead>{t('wanted.nextSearch')}</TableHead>
                  <TableHead className="text-right">{t('versions.actions')}</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {data.items.map((item) => (
                  <TableRow key={item.versionId} data-testid={`wanted-${item.versionId}`}>
                    <TableCell className="align-top">
                      <Link to="/movies/$movieId" params={{ movieId: String(item.movieId) }} className="flex items-center gap-3 underline-offset-4 hover:underline">
                        <Poster url={item.posterUrl} title={item.movieTitle} size="small" />
                        <span className="font-medium">
                          {item.movieTitle}
                          {item.year ? <span className="font-normal text-muted-foreground"> ({item.year})</span> : null}
                        </span>
                      </Link>
                    </TableCell>
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
                    <TableCell className="align-top">
                      <div className="flex justify-end">
                        <Tooltip>
                          <TooltipTrigger asChild>
                            <Button
                              type="button"
                              variant="ghost"
                              size="icon"
                              onClick={() => searchOne.mutate(item)}
                              disabled={searchOne.isPending}
                              aria-label={t('search.autoSearch')}
                              data-testid={`wanted-search-${item.versionId}`}
                            >
                              <SearchIcon className={searchOne.isPending && searchOne.variables?.versionId === item.versionId ? 'animate-pulse' : undefined} />
                            </Button>
                          </TooltipTrigger>
                          <TooltipContent>{t('search.autoSearchHint')}</TooltipContent>
                        </Tooltip>
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
