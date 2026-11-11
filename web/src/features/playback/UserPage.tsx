import { useState, type ReactNode } from 'react'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { Link, useParams } from '@tanstack/react-router'
import { ArrowLeftIcon, PencilIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { playback, type Play, type PlaybackUserStats } from '@/api/client'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { formatDate, formatDateTime } from '@/i18n'
import { DecisionShare } from './DecisionShare'
import { formatPlayed, locationText } from './format'
import { PlayDetailDialog } from './PlayDetailDialog'
import { PlaysTable } from './PlaysTable'
import { Avatar, EditUserDialog } from './UsersPage'

const pageSize = 25

/**
 * One person: watch time, players, how their plays were served, where they played from, and their plays. A stack of
 * sections, so more can come (what they requested) without reshuffling the page.
 */
export function UserPage() {
  const { t } = useTranslation()
  const { userId } = useParams({ from: '/app/playback/users/$userId' })
  const id = Number(userId)
  const [editing, setEditing] = useState(false)
  const user = useQuery({ queryKey: ['playback', 'user', id], queryFn: () => playback.user(id) })

  if (user.isPending) return <Skeleton className="h-64 w-full" />
  if (user.error || !user.data) {
    return (
      <Alert variant="destructive">
        <AlertDescription>{t('playback.loadError')}</AlertDescription>
      </Alert>
    )
  }

  const u = user.data
  const shown = u.displayName ?? u.name
  return (
    <>
      <Link to="/playback/users" className="flex w-fit items-center gap-1 text-sm text-muted-foreground hover:text-foreground">
        <ArrowLeftIcon className="size-3.5" />
        {t('playback.usersTitle')}
      </Link>
      <div className="flex flex-wrap items-center gap-3" data-testid="user-header">
        <Avatar user={{ name: shown, thumbUrl: u.thumbUrl }} className="size-14 text-xl" />
        <div className="flex min-w-0 flex-col gap-0.5">
          <span className="flex flex-wrap items-center gap-2">
            <h1 className="truncate text-2xl font-semibold tracking-tight">{shown}</h1>
            {u.isOwner && <Badge variant="secondary">{t('playback.owner')}</Badge>}
            {!u.keepHistory && <Badge variant="outline">{t('playback.notKept')}</Badge>}
          </span>
          <span className="text-sm text-muted-foreground">
            {u.displayName && `${t('playback.onServer', { name: u.name })} · `}
            {t('playback.firstSeen', { date: formatDate(u.firstSeenAt) })} · {u.lastSeenAt ? t('playback.lastSeenAt', { date: formatDateTime(u.lastSeenAt) }) : t('playback.neverSeen')}
          </span>
        </div>
        <Button variant="outline" size="sm" className="ml-auto" onClick={() => setEditing(true)} data-testid="edit-user">
          <PencilIcon />
          {t('playback.edit')}
        </Button>
      </div>

      <div className="grid grid-cols-2 gap-3 lg:grid-cols-4" data-testid="watch-time">
        {u.watchTime.map((w) => (
          <Card key={w.days ?? 'all'} className="gap-1 py-4">
            <CardContent className="flex flex-col gap-1 px-4">
              <span className="text-xs text-muted-foreground">{w.days === null ? t('playback.allTime') : w.days === 1 ? t('playback.last24h') : t('playback.lastDays', { count: w.days })}</span>
              <span className="text-2xl font-semibold tracking-tight tabular-nums">{formatPlayed(w.seconds)}</span>
              <span className="text-xs text-muted-foreground">{t('playback.playsCount', { count: w.plays })}</span>
            </CardContent>
          </Card>
        ))}
      </div>

      <div className="grid gap-4 lg:grid-cols-2">
        <Section title={t('playback.players')}>
          <PlayersTable user={u} />
        </Section>
        <div className="flex flex-col gap-4">
          <Section title={t('playback.decisionShare')}>
            <DecisionShare decisions={u.decisions} />
          </Section>
          <Section title={t('playback.addresses')}>
            <AddressesTable userId={id} />
          </Section>
        </div>
      </div>

      <UserPlays userId={id} />
      {editing && <EditUserDialog user={{ id: u.id, name: shown, serverName: u.name, keepHistory: u.keepHistory }} onClose={() => setEditing(false)} />}
    </>
  )
}

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <Card className="gap-3 py-4">
      <CardHeader className="px-4">
        <CardTitle className="text-sm font-medium">{title}</CardTitle>
      </CardHeader>
      <CardContent className="px-4">{children}</CardContent>
    </Card>
  )
}

function PlayersTable({ user }: { user: PlaybackUserStats }) {
  const { t } = useTranslation()
  if (user.players.length === 0) return <p className="text-sm text-muted-foreground">{t('playback.noPlays')}</p>
  return (
    <Table data-testid="user-players">
      <TableHeader>
        <TableRow>
          <TableHead>{t('playback.player')}</TableHead>
          <TableHead className="text-right">{t('playback.plays')}</TableHead>
          <TableHead className="text-right">{t('playback.played')}</TableHead>
          <TableHead>{t('playback.lastUsed')}</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {user.players.map((p) => (
          <TableRow key={`${p.player}\n${p.platform}`}>
            <TableCell>
              {p.player || t('common.none')}
              {p.platform && <span className="text-muted-foreground"> · {p.platform}</span>}
            </TableCell>
            <TableCell className="text-right tabular-nums">{p.plays}</TableCell>
            <TableCell className="text-right tabular-nums">{formatPlayed(p.seconds)}</TableCell>
            <TableCell className="text-sm text-muted-foreground">{formatDate(p.lastPlayedAt)}</TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  )
}

/**
 * Where the user played from, newest first, from the addresses endpoint: the place of each public address is looked up
 * when the page asks (and remembered), a LAN address says "Local network", an unknown place stays blank.
 */
function AddressesTable({ userId }: { userId: number }) {
  const { t } = useTranslation()
  const addresses = useQuery({ queryKey: ['playback', 'user', userId, 'addresses'], queryFn: () => playback.addresses(userId) })
  if (addresses.isPending) return <Skeleton className="h-16 w-full" />
  if (!addresses.data || addresses.data.length === 0) return <p className="text-sm text-muted-foreground">{t('playback.noAddresses')}</p>
  return (
    <Table data-testid="user-addresses">
      <TableHeader>
        <TableRow>
          <TableHead>{t('playback.ip')}</TableHead>
          <TableHead>{t('playback.location')}</TableHead>
          <TableHead className="text-right">{t('playback.streamsColumn')}</TableHead>
          <TableHead>{t('playback.lastSeen')}</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {addresses.data.map((a) => (
          <TableRow key={a.ipAddress}>
            <TableCell className="font-mono text-xs">{a.ipAddress}</TableCell>
            <TableCell className="text-sm" title={locationText(a.location, t('playback.localNetwork')) ?? undefined}>
              {locationText(a.location, t('playback.localNetwork'), true)}
            </TableCell>
            <TableCell className="text-right tabular-nums">{a.streams}</TableCell>
            <TableCell className="text-sm text-muted-foreground" title={t('playback.firstSeen', { date: formatDate(a.firstSeenAt) })}>
              {formatDateTime(a.lastSeenAt)}
            </TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  )
}

function UserPlays({ userId }: { userId: number }) {
  const { t } = useTranslation()
  const [page, setPage] = useState(1)
  const [selected, setSelected] = useState<Play | null>(null)
  const plays = useQuery({ queryKey: ['playback', 'plays', 'user', userId, page], queryFn: () => playback.plays({ userId, page, pageSize }), placeholderData: keepPreviousData })
  const data = plays.data
  const pages = data ? Math.max(1, Math.ceil(data.totalRecords / pageSize)) : 1
  return (
    <section className="flex flex-col gap-3">
      <h2 className="text-lg font-semibold tracking-tight">{t('playback.playsTitle')}</h2>
      {data && data.records.length === 0 && <p className="text-sm text-muted-foreground">{t('playback.noPlays')}</p>}
      {data && data.records.length > 0 && (
        <>
          <PlaysTable plays={data.records} showUser={false} onSelect={setSelected} />
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
      <PlayDetailDialog play={selected} onClose={() => setSelected(null)} />
    </section>
  )
}
