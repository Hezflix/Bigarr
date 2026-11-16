import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useSearch } from '@tanstack/react-router'
import { cn } from 'cn'
import { CheckIcon, RotateCwIcon, SendIcon, TriangleAlertIcon, UndoIcon, XIcon } from 'lucide-react'
import type { TFunction } from 'i18next'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ApiError, auth, canManageRequests, type ForwardStatus, isAdmin, type MediaRequest, type RequestMediaType, requests } from '@/api/client'
import { Poster } from '@/components/Poster'
import { Button } from '@/components/ui/button'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import { formatDateTime } from '@/i18n'
import { ApproveDialog, DeclineDialog } from './ApproveDialog'
import { FILTERS, type RequestsSearch } from './requestsSearch'
import { RequestStateBadge } from './requestState'

const PAGE_SIZE = 20

/**
 * Requests: a user sees theirs (and cancels one still pending); an admin or a request manager sees everyone's, filters
 * them and approves (with edits), declines or retries. Filters live in the address, so a link opens on them.
 */
export function RequestsPage() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const search = useSearch({ strict: false }) as RequestsSearch
  const me = useQuery({ queryKey: ['me'], queryFn: auth.me, staleTime: 60_000 })
  const approver = canManageRequests(me.data)
  const filter = search.filter ?? (approver ? 'pending' : 'all')
  const page = search.page ?? 1
  const list = useQuery({
    queryKey: ['requests', 'list', filter, search.type, search.user, page],
    queryFn: () => requests.list({ filter, mediaType: search.type, userId: search.user, page, pageSize: PAGE_SIZE }),
    enabled: me.data !== undefined,
  })
  const [approving, setApproving] = useState<MediaRequest | null>(null)
  const [declining, setDeclining] = useState<MediaRequest | null>(null)
  const go = (next: Partial<RequestsSearch>) => navigate({ to: '/requests', search: { ...search, page: undefined, ...next } })
  const pages = list.data ? Math.max(1, Math.ceil(list.data.totalRecords / PAGE_SIZE)) : 1
  const userName = search.user ? list.data?.records.find((r) => r.requestedBy?.id === search.user)?.requestedBy?.username : undefined

  return (
    <div className="flex min-w-0 flex-col gap-4" data-testid="requests-page">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-semibold tracking-tight">{approver ? t('requests.title') : t('requests.mine')}</h1>
        <Select value={search.type ?? 'all'} onValueChange={(v) => go({ type: v === 'all' ? undefined : (v as RequestMediaType) })}>
          <SelectTrigger className="w-40" aria-label={t('requests.filters.type')} data-testid="requests-type">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="all">{t('requests.filters.allTypes')}</SelectItem>
            <SelectItem value="movie">{t('discover.movies')}</SelectItem>
            <SelectItem value="series">{t('discover.series')}</SelectItem>
          </SelectContent>
        </Select>
      </div>

      <nav className="-mx-4 flex gap-1 overflow-x-auto px-4 pb-1 md:mx-0 md:px-0" aria-label={t('requests.filters.state')} data-testid="requests-filters">
        {FILTERS.map((f) => (
          <button
            key={f}
            type="button"
            onClick={() => go({ filter: f })}
            aria-pressed={filter === f}
            className="min-h-9 shrink-0 rounded-full border px-3 text-sm text-muted-foreground transition-colors hover:text-foreground aria-pressed:border-primary aria-pressed:bg-primary aria-pressed:text-primary-foreground"
          >
            {t(`requests.filters.${f}`)}
          </button>
        ))}
      </nav>

      {search.user && (
        <div className="flex items-center gap-2 text-sm">
          <span className="text-muted-foreground">{t('requests.filters.byUser', { user: userName ?? `#${search.user}` })}</span>
          <Button variant="ghost" size="sm" onClick={() => go({ user: undefined })}>
            <XIcon />
            {t('requests.filters.clearUser')}
          </Button>
        </div>
      )}

      {list.isPending && (
        <div className="flex flex-col gap-2">
          {[0, 1, 2].map((i) => (
            <Skeleton key={i} className="h-24 w-full" />
          ))}
        </div>
      )}
      {list.isError && <p className="text-sm text-destructive">{t('common.unexpectedError')}</p>}
      {list.data && list.data.records.length === 0 && <p className="py-8 text-center text-sm text-muted-foreground">{t(approver ? 'requests.empty' : 'requests.emptyMine')}</p>}

      {list.data && list.data.records.length > 0 && (
        <ul className="flex flex-col divide-y rounded-lg border" data-testid="requests-list">
          {list.data.records.map((r) => (
            <RequestRow key={r.id} request={r} admin={isAdmin(me.data)} approver={approver} onApprove={() => setApproving(r)} onDecline={() => setDeclining(r)} onUser={(id) => go({ user: id })} />
          ))}
        </ul>
      )}

      {pages > 1 && (
        <div className="flex items-center justify-between gap-2 text-sm">
          <Button variant="outline" size="sm" disabled={page <= 1} onClick={() => navigate({ to: '/requests', search: { ...search, page: page - 1 } })}>
            {t('common.previous')}
          </Button>
          <span className="text-muted-foreground">{t('common.page', { page, pages })}</span>
          <Button variant="outline" size="sm" disabled={page >= pages} onClick={() => navigate({ to: '/requests', search: { ...search, page: page + 1 } })}>
            {t('common.next')}
          </Button>
        </div>
      )}

      {approving && <ApproveDialog request={approving} onClose={() => setApproving(null)} />}
      {declining && <DeclineDialog request={declining} onClose={() => setDeclining(null)} />}
    </div>
  )
}

function RequestRow({
  request: r,
  admin,
  approver,
  onApprove,
  onDecline,
  onUser,
}: {
  request: MediaRequest
  admin: boolean
  approver: boolean
  onApprove: () => void
  onDecline: () => void
  onUser: (id: number) => void
}) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const done = async () => {
    await Promise.all([queryClient.invalidateQueries({ queryKey: ['requests'] }), queryClient.invalidateQueries({ queryKey: ['discover'] })])
  }
  const failed = (e: Error) => toast.error(e instanceof ApiError ? e.message : t('common.unexpectedError'))
  const cancel = useMutation({ mutationFn: () => requests.cancel(r.id), onSuccess: done, onError: failed })
  const retry = useMutation({ mutationFn: () => requests.retry(r.id), onSuccess: done, onError: failed })
  const forwardFailed = r.forwardStatus === 'failed'
  // A series only TVDB has (added by the admin) has no Discover page: its title is plain text.
  const titleLink =
    r.tmdbId == null ? (
      <span className="font-medium">{r.title}</span>
    ) : r.mediaType === 'movie' ? (
      <Link to="/discover/movie/$tmdbId" params={{ tmdbId: String(r.tmdbId) }} className="font-medium hover:underline">
        {r.title}
      </Link>
    ) : (
      <Link to="/discover/tv/$tmdbId" params={{ tmdbId: String(r.tmdbId) }} className="font-medium hover:underline">
        {r.title}
      </Link>
    )

  return (
    <li className="flex gap-3 p-3" data-testid={`request-${r.id}`}>
      <Poster url={r.posterUrl} title={r.title} size="small" className="shrink-0" />
      <div className="flex min-w-0 flex-1 flex-col gap-1.5">
        <div className="flex flex-wrap items-baseline gap-x-2 gap-y-1">
          {titleLink}
          {r.year && <span className="text-sm text-muted-foreground">({r.year})</span>}
        </div>
        <div className="flex flex-wrap items-center gap-1.5">
          <RequestStateBadge state={r.state ?? r.status} />
          <span className="rounded-md border px-1.5 py-0.5 text-xs">{r.fourK ? t('discover.version.fourK') : t('discover.version.standard')}</span>
          <span className="text-xs text-muted-foreground">{r.mediaType === 'movie' ? t('requests.film') : seasonsText(t, r.seasons)}</span>
          {r.autoApproved && <span className="text-xs text-muted-foreground">· {t('requests.autoApproved')}</span>}
        </div>
        <p className="text-xs text-muted-foreground">
          {r.requestedBy ? (
            <>
              <button type="button" className="font-medium text-foreground hover:underline" onClick={() => r.requestedBy && onUser(r.requestedBy.id)}>
                {r.requestedBy.username}
              </button>{' '}
              · {formatDateTime(r.createdAt)}
            </>
          ) : (
            t('requests.askedOn', { date: formatDateTime(r.createdAt) })
          )}
          {r.decidedBy && <> · {t(r.status === 'declined' ? 'requests.declinedBy' : 'requests.approvedBy', { user: r.decidedBy.username })}</>}
        </p>
        {r.declineReason && <p className="text-xs">“{r.declineReason}”</p>}
        {approver && (r.qualityProfile || r.rootFolder) && (
          <p className="text-xs text-muted-foreground" data-testid="request-target">
            {[r.qualityProfile, r.rootFolder].filter(Boolean).join(' · ')}
          </p>
        )}
        {approver && r.error && (
          <p className="flex items-start gap-1 text-xs text-destructive">
            <TriangleAlertIcon className="mt-0.5 size-3.5 shrink-0" aria-hidden="true" />
            {r.error}
          </p>
        )}
        {approver && r.forwardStatus && r.forwardStatus !== 'none' && <ForwardLine status={r.forwardStatus} to={r.forwardedTo} error={admin ? r.forwardError : null} />}
      </div>
      <div className="flex shrink-0 flex-col items-end gap-1.5 sm:flex-row sm:items-start">
        {approver && r.status === 'pending' && (
          <>
            <Button size="sm" onClick={onApprove} data-testid="request-approve">
              <CheckIcon />
              <span className="max-sm:sr-only">{t('requests.actions.approve')}</span>
            </Button>
            <Button size="sm" variant="outline" onClick={onDecline} data-testid="request-decline">
              <XIcon />
              <span className="max-sm:sr-only">{t('requests.actions.decline')}</span>
            </Button>
          </>
        )}
        {approver && (r.status === 'failed' || forwardFailed) && (
          <Button size="sm" variant="outline" onClick={() => retry.mutate()} disabled={retry.isPending} data-testid="request-retry">
            <RotateCwIcon className={cn(retry.isPending && 'animate-spin')} />
            <span className="max-sm:sr-only">{t(forwardFailed ? 'requests.actions.retryForward' : 'requests.actions.retry')}</span>
          </Button>
        )}
        {r.canCancel && (
          <Button size="sm" variant="ghost" onClick={() => cancel.mutate()} disabled={cancel.isPending} data-testid="request-cancel">
            <UndoIcon />
            <span className="max-sm:sr-only">{t('requests.actions.cancel')}</span>
          </Button>
        )}
      </div>
    </li>
  )
}

function seasonsText(t: TFunction, seasons: number[]) {
  return seasons.length === 1 ? t('requests.season', { number: seasons[0] }) : t('requests.seasons', { list: seasons.join(', ') })
}

/** Where forward mode sent it: "Sent to Radarr 1080p", or that it failed (the error text for the admin only). */
function ForwardLine({ status, to, error }: { status: ForwardStatus; to: string | null; error: string | null }) {
  const { t } = useTranslation()
  const text = t(`requests.forward.${status}`, { name: to ?? '' })
  const line = (
    <span className={cn('inline-flex items-center gap-1 text-xs', status === 'failed' ? 'text-destructive' : 'text-muted-foreground')} data-testid={`forward-${status}`}>
      {status === 'failed' ? <TriangleAlertIcon className="size-3.5" aria-hidden="true" /> : <SendIcon className="size-3.5" aria-hidden="true" />}
      {text}
    </span>
  )
  if (!error) return line
  return (
    <div className="flex flex-col gap-0.5">
      {line}
      <span className="text-xs break-words text-muted-foreground" data-testid="forward-error">
        {error}
      </span>
    </div>
  )
}
