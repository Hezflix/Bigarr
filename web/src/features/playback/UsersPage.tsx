import { useMemo, useState, type ReactNode } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { cn } from 'cn'
import { ArrowDownIcon, ArrowUpDownIcon, ArrowUpIcon, PencilIcon, SearchIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ApiError, playback, type PlaybackUser } from '@/api/client'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { formatDateTime } from '@/i18n'

type SortKey = 'name' | 'plays' | 'lastSeen' | 'access'
type Sort = { key: SortKey; direction: 'asc' | 'desc' }

/** Names sort A to Z first; numbers, dates and access the most first (on the server before off). */
const firstDirection = (key: SortKey): Sort['direction'] => (key === 'name' ? 'asc' : 'desc')

/** On, then off, then not known: the unknown last whichever the direction. */
const accessRank = (u: PlaybackUser) => (u.onServer === null ? null : u.onServer ? 1 : 0)

function compare(a: PlaybackUser, b: PlaybackUser, sort: Sort): number {
  const sign = sort.direction === 'asc' ? 1 : -1
  const byName = a.name.localeCompare(b.name)
  switch (sort.key) {
    case 'name':
      return sign * byName
    case 'plays':
      return sign * (a.plays - b.plays) || byName
    case 'lastSeen':
      return sign * a.lastSeenAt.localeCompare(b.lastSeenAt) || byName
    case 'access': {
      const ra = accessRank(a)
      const rb = accessRank(b)
      if (ra === rb) return b.plays - a.plays || byName
      if (ra === null) return 1
      if (rb === null) return -1
      return sign * (ra - rb)
    }
  }
}

/** The people who play from the followed servers, the most active first, with the name Bigarr shows and whether their plays are kept. */
export function UsersPage() {
  const { t } = useTranslation()
  const [search, setSearch] = useState('')
  const [editing, setEditing] = useState<PlaybackUser | null>(null)
  const [sort, setSort] = useState<Sort>({ key: 'plays', direction: 'desc' })
  const users = useQuery({ queryKey: ['playback', 'users'], queryFn: playback.users })
  const shown = useMemo(() => {
    const needle = search.trim().toLowerCase()
    return (users.data ?? []).filter((u) => u.name.toLowerCase().includes(needle) || u.serverName.toLowerCase().includes(needle)).sort((a, b) => compare(a, b, sort))
  }, [users.data, search, sort])
  const onSort = (key: SortKey) => setSort((current) => (current.key === key ? { key, direction: current.direction === 'asc' ? 'desc' : 'asc' } : { key, direction: firstDirection(key) }))

  return (
    <>
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="text-2xl font-semibold tracking-tight">{t('playback.usersTitle')}</h1>
        <div className="relative ml-auto w-full sm:w-64">
          <SearchIcon className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-muted-foreground" aria-hidden="true" />
          <Input type="search" value={search} onChange={(e) => setSearch(e.target.value)} placeholder={t('playback.searchUser')} aria-label={t('playback.searchUser')} className="pl-8" />
        </div>
      </div>
      {users.isPending && <Skeleton className="h-40 w-full" />}
      {users.error && (
        <Alert variant="destructive">
          <AlertDescription>{t('playback.loadError')}</AlertDescription>
        </Alert>
      )}
      {users.data && users.data.length === 0 && <p className="text-sm text-muted-foreground">{t('playback.noUsers')}</p>}
      {shown.length > 0 && (
        <div className="overflow-x-auto rounded-lg border">
          <Table data-testid="users-table">
            <TableHeader>
              <TableRow>
                <SortHead column="name" sort={sort} onSort={onSort}>
                  {t('playback.user')}
                </SortHead>
                <SortHead column="plays" sort={sort} onSort={onSort} className="text-right">
                  {t('playback.plays')}
                </SortHead>
                <SortHead column="lastSeen" sort={sort} onSort={onSort}>
                  {t('playback.lastSeen')}
                </SortHead>
                <SortHead column="access" sort={sort} onSort={onSort}>
                  {t('playback.access')}
                </SortHead>
                <TableHead className="w-12" />
              </TableRow>
            </TableHeader>
            <TableBody>
              {shown.map((user) => (
                <TableRow key={user.id} data-user={user.id}>
                  <TableCell>
                    <span className="flex items-center gap-2.5">
                      <Avatar user={user} />
                      <span className="flex min-w-0 flex-col leading-tight">
                        <Link to="/playback/users/$userId" params={{ userId: String(user.id) }} className="truncate font-medium underline-offset-4 hover:underline">
                          {user.name}
                        </Link>
                        {user.name !== user.serverName && <span className="truncate text-xs text-muted-foreground">{user.serverName}</span>}
                      </span>
                      {user.isOwner && <Badge variant="secondary">{t('playback.owner')}</Badge>}
                      {!user.keepHistory && <Badge variant="outline">{t('playback.historyOff')}</Badge>}
                    </span>
                  </TableCell>
                  <TableCell className="text-right tabular-nums">{user.plays}</TableCell>
                  <TableCell className="whitespace-nowrap text-sm text-muted-foreground">{formatDateTime(user.lastSeenAt)}</TableCell>
                  <TableCell>
                    <Access user={user} />
                  </TableCell>
                  <TableCell>
                    <Button variant="ghost" size="icon" onClick={() => setEditing(user)} aria-label={t('playback.editUser', { name: user.name })} data-testid="edit-user">
                      <PencilIcon />
                    </Button>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}
      {editing && <EditUserDialog user={editing} onClose={() => setEditing(null)} />}
    </>
  )
}

/** A header that sorts its column, the library table's way: the sorted one shows its direction, the others a hint on hover. */
function SortHead({ column, sort, onSort, className, children }: { column: SortKey; sort: Sort; onSort: (key: SortKey) => void; className?: string; children: ReactNode }) {
  const { t } = useTranslation()
  const active = sort.key === column
  const Arrow = !active ? ArrowUpDownIcon : sort.direction === 'asc' ? ArrowUpIcon : ArrowDownIcon
  const right = className?.includes('text-right')
  return (
    <TableHead className={className} aria-sort={active ? (sort.direction === 'asc' ? 'ascending' : 'descending') : 'none'}>
      <button
        type="button"
        className={cn(
          'group/sort -mx-1.5 inline-flex items-center gap-1 rounded-md px-1.5 py-1 outline-none transition-colors hover:bg-accent hover:text-accent-foreground focus-visible:ring-2 focus-visible:ring-ring/50',
          right && 'flex-row-reverse',
          active && 'text-foreground',
        )}
        onClick={() => onSort(column)}
        aria-label={t('playback.sortBy', { column: typeof children === 'string' ? children : column })}
        data-testid={`sort-${column}`}
      >
        {children}
        <Arrow className={cn('size-3 shrink-0', !active && 'opacity-0 group-hover/sort:opacity-50 group-focus-visible/sort:opacity-50')} aria-hidden="true" />
      </button>
    </TableHead>
  )
}

/** On or off the server, with where Bigarr knows it from on hover: plex.tv and when, or the Tautulli import. */
function Access({ user }: { user: PlaybackUser }) {
  const { t } = useTranslation()
  if (user.onServer === null) return <span className="text-sm text-muted-foreground">{t('playback.accessUnknown')}</span>
  const label = user.onServer ? <span className="text-sm">{t('playback.accessOn')}</span> : <Badge variant="outline">{t('playback.accessOff')}</Badge>
  if (user.isOwner && user.onServerCheckedAt === null) return label
  return (
    <Tooltip>
      <TooltipTrigger asChild>
        <span className="cursor-default" data-testid="access">
          {label}
        </span>
      </TooltipTrigger>
      <TooltipContent>{user.onServerCheckedAt ? t('playback.accessChecked', { date: formatDateTime(user.onServerCheckedAt) }) : t('playback.accessFromImport')}</TooltipContent>
    </Tooltip>
  )
}

export function Avatar({ user, className }: { user: Pick<PlaybackUser, 'name' | 'thumbUrl'>; className?: string }) {
  const [broken, setBroken] = useState(false)
  if (user.thumbUrl && !broken) return <img src={user.thumbUrl} alt="" className={cn('size-7 shrink-0 rounded-full object-cover', className)} onError={() => setBroken(true)} />
  return (
    <span className={cn('flex size-7 shrink-0 items-center justify-center rounded-full bg-muted text-xs font-medium text-muted-foreground uppercase', className)} aria-hidden="true">
      {user.name.slice(0, 1)}
    </span>
  )
}

/** A centered dialog: the name Bigarr shows (blank goes back to the server's) and whether new plays are recorded. */
export function EditUserDialog({ user, onClose }: { user: Pick<PlaybackUser, 'id' | 'name' | 'serverName' | 'keepHistory'>; onClose: () => void }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [displayName, setDisplayName] = useState(user.name === user.serverName ? '' : user.name)
  const [keepHistory, setKeepHistory] = useState(user.keepHistory)
  const save = useMutation({
    mutationFn: () => playback.updateUser(user.id, { displayName: displayName.trim() || null, keepHistory }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['playback'] })
      toast.success(t('playback.userSaved'))
      onClose()
    },
    onError: (e) => toast.error(e instanceof ApiError ? e.message : t('playback.userFailed')),
  })

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="sm:max-w-md">
        <form
          className="flex flex-col gap-4"
          onSubmit={(e) => {
            e.preventDefault()
            save.mutate()
          }}
        >
          <DialogHeader>
            <DialogTitle>{t('playback.editUserTitle')}</DialogTitle>
            <DialogDescription>{t('playback.editUserHint', { name: user.serverName })}</DialogDescription>
          </DialogHeader>
          <div className="flex flex-col gap-2">
            <Label htmlFor="display-name">{t('playback.displayName')}</Label>
            <Input id="display-name" value={displayName} onChange={(e) => setDisplayName(e.target.value)} placeholder={user.serverName} maxLength={256} autoFocus data-testid="display-name" />
          </div>
          <div className="flex items-start gap-2.5">
            <Checkbox id="keep-history" checked={keepHistory} onCheckedChange={(checked) => setKeepHistory(checked === true)} className="mt-0.5" data-testid="keep-history" />
            <div className="flex flex-col gap-1">
              <Label htmlFor="keep-history">{t('playback.keepHistory')}</Label>
              <p className="text-sm text-muted-foreground">{t('playback.keepHistoryHint')}</p>
            </div>
          </div>
          <DialogFooter>
            <Button type="button" variant="outline" onClick={onClose}>
              {t('common.cancel')}
            </Button>
            <Button type="submit" disabled={save.isPending} data-testid="save-user">
              {t('common.save')}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
