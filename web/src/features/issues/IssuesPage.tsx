import { useEffect, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link, useNavigate, useSearch } from '@tanstack/react-router'
import { MessageSquareIcon, SearchIcon, XIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { auth, canManageRequests, type Issue, type IssueType, issues } from '@/api/client'
import { Poster } from '@/components/Poster'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import { formatDateTime } from '@/i18n'
import { ISSUE_TYPES, IssueStatusBadge, IssueTypeChip, scopeText } from './issueBits'
import { type IssuesSearch, STATUSES } from './issuesSearch'

const PAGE_SIZE = 20

/**
 * Issues: a user sees the ones they reported; an admin or a request manager sees everyone's. Open first by default,
 * filtered by status, type and title; the filters live in the address so a link opens on them.
 */
export function IssuesPage() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const search = useSearch({ strict: false }) as IssuesSearch
  const me = useQuery({ queryKey: ['me'], queryFn: auth.me, staleTime: 60_000 })
  const approver = canManageRequests(me.data)
  const status = search.status ?? 'open'
  const page = search.page ?? 1
  const list = useQuery({
    queryKey: ['issues', 'list', status, search.type, search.q, search.movieId, search.seriesId, page],
    queryFn: () => issues.list({ status, type: search.type, q: search.q, movieId: search.movieId, seriesId: search.seriesId, page, pageSize: PAGE_SIZE }),
    enabled: me.data !== undefined,
  })
  const go = (next: Partial<IssuesSearch>) => navigate({ to: '/issues', search: { ...search, page: undefined, ...next } })
  const pages = list.data ? Math.max(1, Math.ceil(list.data.totalRecords / PAGE_SIZE)) : 1

  // The title filter follows typing after a short pause, so the address and the list do not jump on every key.
  const [text, setText] = useState(search.q ?? '')
  useEffect(() => setText(search.q ?? ''), [search.q])
  useEffect(() => {
    const value = text.trim() || undefined
    if (value === search.q) return
    const timer = setTimeout(() => go({ q: value }), 300)
    return () => clearTimeout(timer)
  })

  return (
    <div className="flex min-w-0 flex-col gap-4" data-testid="issues-page">
      <h1 className="text-2xl font-semibold tracking-tight">{approver ? t('issues.title') : t('issues.mine')}</h1>

      <div className="flex flex-wrap items-center gap-2">
        <div className="relative min-w-0 flex-1 basis-48">
          <SearchIcon className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-muted-foreground" aria-hidden="true" />
          <Input value={text} onChange={(e) => setText(e.target.value)} placeholder={t('issues.searchPlaceholder')} className="pl-8" aria-label={t('issues.searchPlaceholder')} data-testid="issues-search" />
        </div>
        <Select value={search.type ?? 'all'} onValueChange={(v) => go({ type: v === 'all' ? undefined : (v as IssueType) })}>
          <SelectTrigger className="w-36" aria-label={t('issues.filters.type')} data-testid="issues-type">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="all">{t('issues.filters.allTypes')}</SelectItem>
            {ISSUE_TYPES.map((type) => (
              <SelectItem key={type} value={type}>
                {t(`issues.type.${type}`)}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>

      <nav className="-mx-4 flex gap-1 overflow-x-auto px-4 pb-1 md:mx-0 md:px-0" aria-label={t('issues.filters.status')} data-testid="issues-filters">
        {STATUSES.map((s) => (
          <button
            key={s}
            type="button"
            onClick={() => go({ status: s })}
            aria-pressed={status === s}
            className="min-h-9 shrink-0 rounded-full border px-3 text-sm text-muted-foreground transition-colors hover:text-foreground aria-pressed:border-primary aria-pressed:bg-primary aria-pressed:text-primary-foreground"
          >
            {t(`issues.filters.${s}`)}
          </button>
        ))}
        {(search.movieId || search.seriesId) && (
          <Button variant="ghost" size="sm" className="shrink-0" onClick={() => go({ movieId: undefined, seriesId: undefined })}>
            <XIcon />
            {t('issues.filters.allTitles')}
          </Button>
        )}
      </nav>

      {list.isPending && (
        <div className="flex flex-col gap-2">
          {[0, 1, 2].map((i) => (
            <Skeleton key={i} className="h-20 w-full" />
          ))}
        </div>
      )}
      {list.isError && <p className="text-sm text-destructive">{t('common.unexpectedError')}</p>}
      {list.data && list.data.records.length === 0 && <p className="py-8 text-center text-sm text-muted-foreground">{t(approver ? 'issues.empty' : 'issues.emptyMine')}</p>}

      {list.data && list.data.records.length > 0 && (
        <ul className="flex flex-col divide-y rounded-lg border" data-testid="issues-list">
          {list.data.records.map((issue) => (
            <IssueRow key={issue.id} issue={issue} approver={approver} />
          ))}
        </ul>
      )}

      {pages > 1 && (
        <div className="flex items-center justify-between gap-2 text-sm">
          <Button variant="outline" size="sm" disabled={page <= 1} onClick={() => navigate({ to: '/issues', search: { ...search, page: page - 1 } })}>
            {t('common.previous')}
          </Button>
          <span className="text-muted-foreground">{t('common.page', { page, pages })}</span>
          <Button variant="outline" size="sm" disabled={page >= pages} onClick={() => navigate({ to: '/issues', search: { ...search, page: page + 1 } })}>
            {t('common.next')}
          </Button>
        </div>
      )}
    </div>
  )
}

/** One issue: the whole row opens it. */
function IssueRow({ issue, approver }: { issue: Issue; approver: boolean }) {
  const { t } = useTranslation()
  return (
    <li data-testid={`issue-${issue.id}`}>
      <Link to="/issues/$issueId" params={{ issueId: String(issue.id) }} className="flex gap-3 p-3 outline-none hover:bg-accent/50 focus-visible:bg-accent/50">
        <Poster url={issue.posterUrl} title={issue.title} size="small" className="shrink-0" />
        <div className="flex min-w-0 flex-1 flex-col gap-1.5">
          <div className="flex flex-wrap items-baseline gap-x-2 gap-y-0.5">
            <span className="font-medium">{issue.title}</span>
            {issue.year && <span className="text-sm text-muted-foreground">({issue.year})</span>}
          </div>
          <div className="flex flex-wrap items-center gap-1.5">
            <IssueStatusBadge status={issue.status} />
            <IssueTypeChip type={issue.type} />
            <span className="min-w-0 truncate text-xs text-muted-foreground">{scopeText(t, issue)}</span>
          </div>
          <p className="line-clamp-2 text-sm">{issue.message}</p>
          <p className="flex flex-wrap items-center gap-x-2 text-xs text-muted-foreground">
            <span>{approver ? t('issues.reportedBy', { user: issue.reportedBy.username, date: formatDateTime(issue.createdAt) }) : t('issues.reportedOn', { date: formatDateTime(issue.createdAt) })}</span>
            {issue.commentCount > 0 && (
              <span className="inline-flex items-center gap-1">
                <MessageSquareIcon className="size-3.5" aria-hidden="true" />
                {t('issues.comments', { count: issue.commentCount })}
              </span>
            )}
          </p>
        </div>
      </Link>
    </li>
  )
}
