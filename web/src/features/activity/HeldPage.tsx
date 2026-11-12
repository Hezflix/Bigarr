import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { DownloadIcon, XIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ApiError, heldReleases, type HeldRelease } from '@/api/client'
import { Poster } from '@/components/Poster'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { formatAge, formatBytes, formatDateTime } from '@/i18n'

/** Releases RSS or a search matched on a show's alternative order only: Activity → Held for review. */
export function HeldPage() {
  const { t } = useTranslation()
  const list = useQuery({ queryKey: ['held'], queryFn: () => heldReleases.list(), refetchInterval: 60_000 })

  return (
    <>
      <h1 className="text-2xl font-semibold tracking-tight">{t('held.title')}</h1>
      <p className="max-w-3xl text-sm text-muted-foreground">{t('held.hint')}</p>
      {list.isPending && <Skeleton className="h-24 w-full" />}
      {list.data && list.data.length === 0 && <p className="text-sm text-muted-foreground">{t('held.empty')}</p>}
      {list.data && list.data.length > 0 && <HeldReleasesTable releases={list.data} showSeries />}
    </>
  )
}

/** The held releases of one show, on its page; nothing when there are none. */
export function HeldReleasesSection({ seriesId }: { seriesId: number }) {
  const { t } = useTranslation()
  const list = useQuery({ queryKey: ['held', seriesId], queryFn: () => heldReleases.list(seriesId) })
  if (!list.data || list.data.length === 0) return null

  return (
    <section className="flex flex-col gap-2" data-testid="held-releases">
      <h2 className="text-lg font-semibold tracking-tight">{t('held.sectionTitle', { count: list.data.length })}</h2>
      <p className="max-w-3xl text-xs text-muted-foreground">{t('held.sectionHint')}</p>
      <HeldReleasesTable releases={list.data} showSeries={false} />
    </section>
  )
}

function HeldReleasesTable({ releases, showSeries }: { releases: HeldRelease[]; showSeries: boolean }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const invalidate = () => {
    void queryClient.invalidateQueries({ queryKey: ['held'] })
    void queryClient.invalidateQueries({ queryKey: ['queue'] })
    void queryClient.invalidateQueries({ queryKey: ['health'] })
  }
  const fail = (e: unknown) => toast.error(e instanceof ApiError ? e.message : t('held.failed'))
  const grab = useMutation({
    mutationFn: (release: HeldRelease) => heldReleases.grab(release.id),
    onSuccess: (result, release) => {
      toast.success(result.shadowPickId ? t('held.picked', { title: release.title }) : t('held.grabbed', { title: release.title }))
      invalidate()
    },
    onError: fail,
  })
  const dismiss = useMutation({ mutationFn: (release: HeldRelease) => heldReleases.dismiss(release.id), onSuccess: invalidate, onError: fail })

  return (
    <div className="overflow-x-auto rounded-lg border">
      <Table data-testid="held-table">
        <TableHeader>
          <TableRow>
            {showSeries && <TableHead>{t('held.series')}</TableHead>}
            <TableHead>{t('held.episodes')}</TableHead>
            <TableHead>{t('held.release')}</TableHead>
            <TableHead>{t('held.reason')}</TableHead>
            <TableHead>{t('held.heldAt')}</TableHead>
            <TableHead className="text-right">{t('versions.actions')}</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {releases.map((release) => (
            <TableRow key={release.id} data-testid={`held-${release.id}`}>
              {showSeries && (
                <TableCell className="align-top">
                  <Link to="/series/$seriesId" params={{ seriesId: String(release.seriesId) }} className="flex items-center gap-3 underline-offset-4 hover:underline">
                    <Poster url={release.posterUrl} title={release.seriesTitle} size="small" />
                    <span className="font-medium">
                      {release.seriesTitle}
                      {release.seriesYear ? <span className="font-normal text-muted-foreground"> ({release.seriesYear})</span> : null}
                    </span>
                  </Link>
                </TableCell>
              )}
              <TableCell className="align-top whitespace-nowrap">
                <span className="font-medium tabular-nums">{release.episodes}</span>
                <span className="block text-xs text-muted-foreground">{release.versionName}</span>
              </TableCell>
              <TableCell className="min-w-[20rem] align-top whitespace-normal">
                <span className="break-all">{release.title}</span>
                <span className="mt-1 flex flex-wrap items-center gap-1.5 text-xs text-muted-foreground">
                  {release.quality && <Badge variant="secondary">{release.quality}</Badge>}
                  <span className="tabular-nums">{formatBytes(release.size)}</span>
                  {release.indexer && <span>· {release.indexer}</span>}
                  {release.seeders !== null && <span className="tabular-nums">· {t('held.seeders', { count: release.seeders })}</span>}
                </span>
              </TableCell>
              <TableCell className="min-w-[16rem] align-top text-sm whitespace-normal">
                {release.reason}
                <span className="block text-xs text-muted-foreground">{t('held.source', { source: release.source })}</span>
              </TableCell>
              <TableCell className="align-top whitespace-nowrap">
                <Tooltip>
                  <TooltipTrigger asChild>
                    <span>{formatAge(release.heldAt)}</span>
                  </TooltipTrigger>
                  <TooltipContent>{formatDateTime(release.heldAt)}</TooltipContent>
                </Tooltip>
              </TableCell>
              <TableCell className="align-top">
                <div className="flex justify-end gap-1">
                  <Button type="button" size="sm" variant="outline" onClick={() => grab.mutate(release)} disabled={grab.isPending} data-testid={`held-grab-${release.id}`}>
                    <DownloadIcon />
                    {t('held.grab')}
                  </Button>
                  <Button
                    type="button"
                    size="icon"
                    variant="ghost"
                    onClick={() => dismiss.mutate(release)}
                    disabled={dismiss.isPending}
                    aria-label={t('held.dismiss')}
                    title={t('held.dismiss')}
                    data-testid={`held-dismiss-${release.id}`}
                  >
                    <XIcon />
                  </Button>
                </div>
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  )
}
