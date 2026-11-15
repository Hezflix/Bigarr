import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { BanIcon, CircleCheckIcon, InfoIcon, KeyRoundIcon, PencilIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ApiError, users, type UserPermissions, type UserRole, type UserSummary } from '@/api/client'
import { Alert, AlertDescription } from '@/components/ui/alert'
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Checkbox } from '@/components/ui/checkbox'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { formatDateTime } from '@/i18n'

const PERMISSIONS = ['requestFilms', 'requestSeries', 'request4K', 'autoApproveFilms', 'autoApproveSeries', 'reportIssues'] as const
const ROLES: UserRole[] = ['admin', 'requestManager', 'user']

/** Settings → Users: who signs in, with which role and permissions, and what a new user starts with. */
export function UsersPage() {
  const { t } = useTranslation()
  const list = useQuery({ queryKey: ['users'], queryFn: users.list })
  const [editing, setEditing] = useState<UserSummary | null>(null)
  const [disabling, setDisabling] = useState<UserSummary | null>(null)

  return (
    <>
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="text-2xl font-semibold tracking-tight">{t('users.title')}</h1>
      </div>
      <p className="max-w-3xl text-sm text-muted-foreground">{t('users.hint')}</p>

      <NewUsersCard />

      <Card data-testid="users-list">
        <CardHeader>
          <CardTitle>{t('users.listTitle')}</CardTitle>
          <CardDescription>{t('users.listHint')}</CardDescription>
        </CardHeader>
        <CardContent>
          {list.isPending && <p className="text-sm text-muted-foreground">{t('common.loading')}</p>}
          {list.isError && <p className="text-sm text-destructive">{t('common.unexpectedError')}</p>}
          {list.data && (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>{t('users.user')}</TableHead>
                  <TableHead>{t('users.role')}</TableHead>
                  <TableHead className="hidden md:table-cell">{t('users.signsInWith')}</TableHead>
                  <TableHead className="hidden lg:table-cell">{t('users.lastLogin')}</TableHead>
                  <TableHead className="text-right">
                    <span className="sr-only">{t('users.actions')}</span>
                  </TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {list.data.map((user) => (
                  <UserRow key={user.id} user={user} onEdit={() => setEditing(user)} onDisable={() => setDisabling(user)} />
                ))}
              </TableBody>
            </Table>
          )}
        </CardContent>
      </Card>

      {editing && <EditUserDialog user={editing} onClose={() => setEditing(null)} />}
      <DisableDialog user={disabling} onClose={() => setDisabling(null)} />
    </>
  )
}

function UserRow({ user, onEdit, onDisable }: { user: UserSummary; onEdit: () => void; onDisable: () => void }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const enable = useMutation({
    mutationFn: () => users.enable(user.id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['users'] }),
    onError: (e) => toast.error(e instanceof ApiError ? e.message : t('common.unexpectedError')),
  })
  const plex = user.logins.find((l) => l.provider === 'plex')
  const email = user.logins.find((l) => l.email)?.email
  const disabled = user.disabledAt !== null

  return (
    <TableRow data-testid={`user-${user.username}`} className={disabled ? 'text-muted-foreground' : undefined}>
      <TableCell>
        <div className="flex min-w-0 items-center gap-3">
          <Avatar name={user.username} url={user.logins.find((l) => l.thumbUrl)?.thumbUrl ?? null} dimmed={disabled} />
          <div className="min-w-0">
            <p className="flex flex-wrap items-center gap-2 font-medium">
              <span className="truncate">{user.username}</span>
              {user.isYou && <Badge variant="outline">{t('users.you')}</Badge>}
              {disabled && <Badge variant="secondary">{t('users.disabled')}</Badge>}
            </p>
            {email && <p className="truncate text-xs text-muted-foreground">{email}</p>}
          </div>
        </div>
      </TableCell>
      <TableCell>
        <Badge variant={user.role === 'admin' ? 'default' : 'secondary'}>{t(`users.roles.${user.role}`)}</Badge>
      </TableCell>
      <TableCell className="hidden md:table-cell">
        <div className="flex flex-wrap gap-1.5">
          {user.hasPassword && (
            <Badge variant="outline">
              <KeyRoundIcon />
              {t('users.password')}
            </Badge>
          )}
          {plex && <Badge variant="outline">{t('users.plexAccount', { account: plex.externalUsername ?? '' })}</Badge>}
        </div>
      </TableCell>
      <TableCell className="hidden whitespace-nowrap lg:table-cell">
        {user.lastLoginAt ? formatDateTime(user.lastLoginAt) : t('users.never')}
      </TableCell>
      <TableCell>
        <div className="flex justify-end gap-1">
          <Button type="button" size="sm" variant="ghost" onClick={onEdit} data-testid={`user-${user.username}-edit`}>
            <PencilIcon />
            <span className="hidden sm:inline">{t('users.edit')}</span>
          </Button>
          {!user.isYou &&
            (disabled ? (
              <Button type="button" size="sm" variant="ghost" disabled={enable.isPending} onClick={() => enable.mutate()}>
                <CircleCheckIcon />
                <span className="hidden sm:inline">{t('users.enable')}</span>
              </Button>
            ) : (
              <Button type="button" size="sm" variant="ghost" onClick={onDisable} data-testid={`user-${user.username}-disable`}>
                <BanIcon />
                <span className="hidden sm:inline">{t('users.disable')}</span>
              </Button>
            ))}
        </div>
      </TableCell>
    </TableRow>
  )
}

/** The Plex picture over the initials, so a picture that does not load leaves the initials showing. */
function Avatar({ name, url, dimmed }: { name: string; url: string | null; dimmed?: boolean }) {
  return (
    <span
      aria-hidden
      className={`relative flex size-9 shrink-0 items-center justify-center overflow-hidden rounded-full bg-muted text-xs font-semibold uppercase ${dimmed ? 'opacity-50 grayscale' : ''}`}
    >
      {name.slice(0, 2)}
      {url && <img src={url} alt="" referrerPolicy="no-referrer" className="absolute inset-0 size-full object-cover" />}
    </span>
  )
}

function PermissionChecks({
  idPrefix,
  value,
  onChange,
  disabled,
}: {
  idPrefix: string
  value: UserPermissions
  onChange: (next: UserPermissions) => void
  disabled?: boolean
}) {
  const { t } = useTranslation()
  return (
    <div className="grid gap-3 sm:grid-cols-2">
      {PERMISSIONS.map((key) => (
        <div key={key} className="flex items-start gap-3">
          <Checkbox
            id={`${idPrefix}-${key}`}
            checked={disabled ? true : value[key]}
            disabled={disabled}
            onCheckedChange={(v) => onChange({ ...value, [key]: v === true })}
            className="mt-0.5"
          />
          <div className="grid gap-0.5">
            <Label htmlFor={`${idPrefix}-${key}`}>{t(`users.permissions.${key}`)}</Label>
            <p className="text-xs text-muted-foreground">{t(`users.permissionHints.${key}`)}</p>
          </div>
        </div>
      ))}
    </div>
  )
}

function NewUsersCard() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const settings = useQuery({ queryKey: ['users', 'settings'], queryFn: users.settings })
  const save = useMutation({
    mutationFn: ({ open, defaults }: { open: boolean; defaults: UserPermissions }) => users.updateSettings(open, defaults),
    onSuccess: (data) => {
      queryClient.setQueryData(['users', 'settings'], data)
      void queryClient.invalidateQueries({ queryKey: ['auth'] })
    },
    onError: (e) => toast.error(e instanceof ApiError ? e.message : t('common.unexpectedError')),
  })
  // Shows the change at once, before the server confirms it.
  const data =
    settings.data && save.isPending && save.variables
      ? { ...settings.data, plexNewAccounts: save.variables.open, defaultPermissions: save.variables.defaults }
      : settings.data

  return (
    <Card data-testid="users-new">
      <CardHeader>
        <CardTitle>{t('users.newTitle')}</CardTitle>
        <CardDescription>{t('users.newHint')}</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-5">
        {settings.isPending && <p className="text-sm text-muted-foreground">{t('common.loading')}</p>}
        {data && (
          <>
            <div className="flex items-start gap-3">
              <Checkbox
                id="users-plex-new"
                checked={data.plexNewAccounts}
                onCheckedChange={(v) => save.mutate({ open: v === true, defaults: data.defaultPermissions })}
                className="mt-0.5"
                data-testid="users-plex-new"
              />
              <div className="grid gap-1">
                <Label htmlFor="users-plex-new">{t('users.plexNewAccounts')}</Label>
                <p className="text-sm text-muted-foreground">{t('users.plexNewAccountsHint')}</p>
              </div>
            </div>
            {data.plexNewAccounts && !data.plexServerConfigured && (
              <Alert>
                <InfoIcon />
                <AlertDescription>
                  <span>
                    {t('users.noPlexServer')}{' '}
                    <Link to="/settings/mediaservers" className="underline underline-offset-4">
                      {t('nav.settingsMediaServers')}
                    </Link>
                  </span>
                </AlertDescription>
              </Alert>
            )}
            <div className="grid gap-3">
              <p className="text-sm font-medium">{t('users.defaultPermissions')}</p>
              <PermissionChecks
                idPrefix="users-default"
                value={data.defaultPermissions}
                onChange={(defaults) => save.mutate({ open: data.plexNewAccounts, defaults })}
              />
            </div>
            <p className="text-sm text-muted-foreground">
              {t('users.signInButtonNote')}{' '}
              <Link to="/settings/security" className="underline underline-offset-4">
                {t('nav.settingsSecurity')}
              </Link>
            </p>
          </>
        )}
      </CardContent>
    </Card>
  )
}

function EditUserDialog({ user, onClose }: { user: UserSummary; onClose: () => void }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [role, setRole] = useState<UserRole>(user.role)
  const [permissions, setPermissions] = useState<UserPermissions>(user.permissions)
  const save = useMutation({
    mutationFn: () => users.update(user.id, role, permissions),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['users'] })
      toast.success(t('users.saved', { name: user.username }))
      onClose()
    },
    onError: (e) => toast.error(e instanceof ApiError ? e.message : t('common.unexpectedError')),
  })

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="sm:max-w-xl" data-testid="user-edit-dialog">
        <DialogHeader>
          <DialogTitle>{t('users.editTitle', { name: user.username })}</DialogTitle>
          <DialogDescription>{t('users.editHint')}</DialogDescription>
        </DialogHeader>
        <div className="grid gap-5">
          <div className="grid gap-2">
            <Label htmlFor="user-role">{t('users.role')}</Label>
            <Select value={role} onValueChange={(v) => setRole(v as UserRole)} disabled={user.isYou}>
              <SelectTrigger id="user-role" className="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {ROLES.map((r) => (
                  <SelectItem key={r} value={r}>
                    {t(`users.roles.${r}`)}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            <p className="text-xs text-muted-foreground">{user.isYou ? t('users.ownRole') : t(`users.roleHints.${role}`)}</p>
          </div>
          <div className="grid gap-3">
            <p className="text-sm font-medium">{t('users.permissionsTitle')}</p>
            {role === 'admin' && <p className="text-xs text-muted-foreground">{t('users.adminHasAll')}</p>}
            <PermissionChecks idPrefix="user-edit" value={permissions} onChange={setPermissions} disabled={role === 'admin'} />
          </div>
        </div>
        <DialogFooter>
          <Button type="button" variant="outline" onClick={onClose}>
            {t('common.cancel')}
          </Button>
          <Button type="button" disabled={save.isPending} onClick={() => save.mutate()} data-testid="user-edit-save">
            {t('common.save')}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

function DisableDialog({ user, onClose }: { user: UserSummary | null; onClose: () => void }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const disable = useMutation({
    mutationFn: (id: number) => users.disable(id),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['users'] })
      onClose()
    },
    onError: (e) => toast.error(e instanceof ApiError ? e.message : t('common.unexpectedError')),
  })

  return (
    <AlertDialog open={user !== null} onOpenChange={(open) => !open && onClose()}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{t('users.disableTitle', { name: user?.username ?? '' })}</AlertDialogTitle>
          <AlertDialogDescription>{t('users.disableBody')}</AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel>{t('common.cancel')}</AlertDialogCancel>
          <AlertDialogAction onClick={() => user && disable.mutate(user.id)} disabled={disable.isPending}>
            {t('users.disable')}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  )
}
