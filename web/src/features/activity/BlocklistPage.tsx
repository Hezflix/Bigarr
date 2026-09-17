import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { Trash2Icon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ApiError, blocklist } from '@/api/client'
import { Poster } from '@/components/Poster'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { formatAge, formatDateTime } from '@/i18n'

const pageSize = 50

/** Releases refused per version (blueprint §3.5): failed downloads and user removals. Dropping an entry lets the release through again. */
export function BlocklistPage() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [page, setPage] = useState(1)
  const [selected, setSelected] = useState<number[]>([])
  const list = useQuery({ queryKey: ['blocklist', page], queryFn: () => blocklist.list({ page, pageSize }) })
  const invalidate = async () => {
    setSelected([])
    await queryClient.invalidateQueries({ queryKey: ['blocklist'] })
  }
  const fail = (e: unknown) => toast.error(e instanceof ApiError ? e.message : t('blocklist.removeFailed'))

  const remove = useMutation({ mutationFn: (id: number) => blocklist.remove(id), onSuccess: invalidate, onError: fail })
  const bulk = useMutation({
    mutationFn: (ids: number[]) => blocklist.bulkDelete(ids),
    onSuccess: async (count) => {
      toast.success(t('blocklist.removed', { count }))
      await invalidate()
    },
    onError: fail,
  })

  const data = list.data
  const pages = data ? Math.max(1, Math.ceil(data.total / pageSize)) : 1
  const ids = data?.items.map((e) => e.id) ?? []
  const allSelected = ids.length > 0 && ids.every((id) => selected.includes(id))
  const toggle = (id: number) => setSelected((s) => (s.includes(id) ? s.filter((x) => x !== id) : [...s, id]))

  return (
    <>
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="text-2xl font-semibold tracking-tight">{t('blocklist.title')}</h1>
        <div className="ml-auto flex items-center gap-2">
          <Button type="button" variant="outline" size="sm" onClick={() => bulk.mutate(selected)} disabled={selected.length === 0 || bulk.isPending} data-testid="blocklist-remove-selected">
            <Trash2Icon />
            {t('blocklist.removeSelected', { count: selected.length })}
          </Button>
        </div>
      </div>
      <p className="max-w-3xl text-sm text-muted-foreground">{t('blocklist.hint')}</p>

      {list.isPending && (
        <div className="flex flex-col gap-2">
          <Skeleton className="h-8 w-full" />
          <Skeleton className="h-8 w-full" />
        </div>
      )}

      {data && data.total === 0 && <p className="text-sm text-muted-foreground">{t('blocklist.empty')}</p>}

      {data && data.total > 0 && (
        <>
          <div className="overflow-x-auto rounded-lg border">
            <Table data-testid="blocklist-table">
              <TableHeader>
                <TableRow>
                  <TableHead className="w-8">
                    <Checkbox checked={allSelected} onCheckedChange={(v) => setSelected(v === true ? ids : [])} aria-label={t('blocklist.selectAll')} />
                  </TableHead>
                  <TableHead>{t('blocklist.movie')}</TableHead>
                  <TableHead>{t('blocklist.version')}</TableHead>
                  <TableHead>{t('blocklist.release')}</TableHead>
                  <TableHead>{t('blocklist.indexer')}</TableHead>
                  <TableHead>{t('blocklist.reason')}</TableHead>
                  <TableHead>{t('blocklist.date')}</TableHead>
                  <TableHead className="text-right">{t('versions.actions')}</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {data.items.map((entry) => (
                  <TableRow key={entry.id} data-testid={`blocklist-${entry.id}`}>
                    <TableCell className="align-top">
                      <Checkbox checked={selected.includes(entry.id)} onCheckedChange={() => toggle(entry.id)} aria-label={t('blocklist.select')} />
                    </TableCell>
                    <TableCell className="align-top">
                      {entry.movieId ? (
                        <Link to="/movies/$movieId" params={{ movieId: String(entry.movieId) }} className="flex items-center gap-3 underline-offset-4 hover:underline">
                          <Poster url={entry.posterUrl} title={entry.movieTitle ?? ''} size="small" />
                          <span className="font-medium">
                            {entry.movieTitle}
                            {entry.movieYear ? <span className="font-normal text-muted-foreground"> ({entry.movieYear})</span> : null}
                          </span>
                        </Link>
                      ) : (
                        <span className="text-muted-foreground">{t('blocklist.movieGone')}</span>
                      )}
                    </TableCell>
                    <TableCell className="align-top">{entry.versionName ?? t('common.none')}</TableCell>
                    <TableCell className="min-w-[20rem] align-top whitespace-normal">
                      <span className="break-all">{entry.sourceTitle}</span>
                      {entry.quality && (
                        <span className="mt-1 block">
                          <Badge variant="secondary">{entry.quality}</Badge>
                        </span>
                      )}
                    </TableCell>
                    <TableCell className="align-top whitespace-nowrap">{entry.indexerName ?? t('common.none')}</TableCell>
                    <TableCell className="min-w-[14rem] align-top text-sm whitespace-normal">{entry.reason ?? t('common.none')}</TableCell>
                    <TableCell className="align-top whitespace-nowrap">
                      <Tooltip>
                        <TooltipTrigger asChild>
                          <span>{formatAge(entry.createdAt)}</span>
                        </TooltipTrigger>
                        <TooltipContent>{formatDateTime(entry.createdAt)}</TooltipContent>
                      </Tooltip>
                    </TableCell>
                    <TableCell className="align-top">
                      <div className="flex justify-end">
                        <Tooltip>
                          <TooltipTrigger asChild>
                            <Button
                              type="button"
                              variant="ghost"
                              size="icon"
                              className="text-destructive hover:text-destructive"
                              onClick={() => remove.mutate(entry.id)}
                              disabled={remove.isPending}
                              aria-label={t('blocklist.remove')}
                            >
                              <Trash2Icon />
                            </Button>
                          </TooltipTrigger>
                          <TooltipContent>{t('blocklist.remove')}</TooltipContent>
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
