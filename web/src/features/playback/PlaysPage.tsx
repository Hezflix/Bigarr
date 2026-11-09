import { useState } from 'react'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { playback, type MediaItemKind } from '@/api/client'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import { PlaysTable } from './PlaysTable'

const pageSize = 50

/** Every play followed on the media servers, newest first, by user and kind. */
export function PlaysPage() {
  const { t } = useTranslation()
  const [page, setPage] = useState(1)
  const [userId, setUserId] = useState('all')
  const [kind, setKind] = useState<'all' | MediaItemKind>('all')
  const users = useQuery({ queryKey: ['playback', 'users'], queryFn: playback.users })
  const query = useQuery({
    queryKey: ['playback', 'plays', page, userId, kind],
    queryFn: () => playback.plays({ page, pageSize, userId: userId === 'all' ? undefined : Number(userId), kind: kind === 'all' ? undefined : kind }),
    placeholderData: keepPreviousData,
  })
  const data = query.data
  const pages = data ? Math.max(1, Math.ceil(data.totalRecords / pageSize)) : 1

  return (
    <>
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="text-2xl font-semibold tracking-tight">{t('playback.playsTitle')}</h1>
        <div className="ml-auto flex flex-wrap items-center gap-2">
          <Label htmlFor="plays-user" className="text-muted-foreground">
            {t('playback.user')}
          </Label>
          <Select
            value={userId}
            onValueChange={(v) => {
              setUserId(v)
              setPage(1)
            }}
          >
            <SelectTrigger id="plays-user" className="min-w-40">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">{t('playback.allUsers')}</SelectItem>
              {users.data?.map((u) => (
                <SelectItem key={u.id} value={String(u.id)}>
                  {u.name}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
          <Select
            value={kind}
            onValueChange={(v) => {
              setKind(v as 'all' | MediaItemKind)
              setPage(1)
            }}
          >
            <SelectTrigger className="min-w-32" aria-label={t('playback.kind')}>
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">{t('playback.allKinds')}</SelectItem>
              <SelectItem value="movie">{t('playback.kinds.movie')}</SelectItem>
              <SelectItem value="episode">{t('playback.kinds.episode')}</SelectItem>
            </SelectContent>
          </Select>
        </div>
      </div>

      {query.isPending && <Skeleton className="h-40 w-full" />}
      {query.error && (
        <Alert variant="destructive">
          <AlertDescription>{t('playback.loadError')}</AlertDescription>
        </Alert>
      )}
      {data && data.records.length === 0 && <p className="text-sm text-muted-foreground">{t('playback.noPlays')}</p>}
      {data && data.records.length > 0 && (
        <>
          <PlaysTable plays={data.records} />
          <div className="flex items-center justify-end gap-2 text-sm text-muted-foreground">
            <span className="tabular-nums">{t('playback.pageOf', { page, pages, total: data.totalRecords })}</span>
            <Button variant="outline" size="sm" disabled={page <= 1} onClick={() => setPage(page - 1)}>
              {t('playback.previous')}
            </Button>
            <Button variant="outline" size="sm" disabled={page >= pages} onClick={() => setPage(page + 1)}>
              {t('playback.next')}
            </Button>
          </div>
        </>
      )}
    </>
  )
}
