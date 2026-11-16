import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useParams } from '@tanstack/react-router'
import { cn } from 'cn'
import {
  ArrowLeftIcon,
  BanIcon,
  CircleCheckIcon,
  CompassIcon,
  LibraryIcon,
  Loader2Icon,
  PencilIcon,
  RotateCcwIcon,
  SearchIcon,
  SendIcon,
  Trash2Icon,
  WrenchIcon,
} from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ApiError, type Issue, type IssueComment, type IssueFile, issues } from '@/api/client'
import { Poster } from '@/components/Poster'
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
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Label } from '@/components/ui/label'
import { Skeleton } from '@/components/ui/skeleton'
import { formatBytes, formatDateTime } from '@/i18n'
import { IssueStatusBadge, IssueTypeChip, scopeText } from './issueBits'

const textarea =
  'min-h-20 w-full rounded-md border border-input bg-transparent px-3 py-2 text-base shadow-xs outline-none placeholder:text-muted-foreground focus-visible:border-ring focus-visible:ring-[3px] focus-visible:ring-ring/50 md:text-sm'

/**
 * One issue: what was reported, the conversation, resolve / reopen. An approver gets the actions (search again, blocklist
 * the release, delete the file); the admin also sees which file and release it is about.
 */
export function IssuePage() {
  const { t } = useTranslation()
  const { issueId } = useParams({ from: '/shell/issues/$issueId' })
  const id = Number(issueId)
  const queryClient = useQueryClient()
  const query = useQuery({ queryKey: ['issues', 'detail', id], queryFn: () => issues.get(id) })

  // Every change answers with the issue as it is now: put it in place, refresh the lists and counts.
  const apply = async (issue: Issue) => {
    queryClient.setQueryData(['issues', 'detail', id], issue)
    await queryClient.invalidateQueries({ queryKey: ['issues'], predicate: (q) => q.queryKey[1] !== 'detail' })
  }
  const failed = (e: Error) => toast.error(e instanceof ApiError ? e.message : t('common.unexpectedError'))
  const resolve = useMutation({ mutationFn: () => issues.resolve(id), onSuccess: apply, onError: failed })
  const reopen = useMutation({ mutationFn: () => issues.reopen(id), onSuccess: apply, onError: failed })

  if (query.isError) {
    return (
      <div className="flex flex-col gap-3">
        <BackLink />
        <p className="text-sm text-muted-foreground">{query.error instanceof ApiError && query.error.status === 404 ? t('issues.detail.notFound') : t('common.unexpectedError')}</p>
      </div>
    )
  }
  if (!query.data) return <Skeleton className="h-64 w-full" />
  const issue = query.data

  return (
    <div className="mx-auto flex w-full max-w-3xl min-w-0 flex-col gap-5" data-testid="issue-page">
      <BackLink />

      <header className="flex gap-4">
        <Poster url={issue.posterUrl} title={issue.title} size="small" className="shrink-0 sm:w-24" />
        <div className="flex min-w-0 flex-1 flex-col gap-2">
          <h1 className="text-xl font-semibold tracking-tight sm:text-2xl" data-testid="issue-title">
            {issue.title}
            {issue.year && <span className="ml-2 text-base font-normal text-muted-foreground">({issue.year})</span>}
          </h1>
          <div className="flex flex-wrap items-center gap-1.5">
            <IssueStatusBadge status={issue.status} />
            <IssueTypeChip type={issue.type} />
            <span className="rounded-md border px-1.5 py-0.5 text-xs">{scopeText(t, issue)}</span>
            <span className="rounded-md border px-1.5 py-0.5 text-xs text-muted-foreground">{t('issues.version', { name: issue.versionName })}</span>
          </div>
          <p className="text-xs text-muted-foreground">{t('issues.reportedBy', { user: issue.reportedBy.username, date: formatDateTime(issue.createdAt) })}</p>
          <div className="flex flex-wrap gap-2">
            {issue.tmdbId != null && (
              <Button asChild variant="outline" size="sm">
                {issue.mediaType === 'movie' ? (
                  <Link to="/discover/movie/$tmdbId" params={{ tmdbId: String(issue.tmdbId) }}>
                    <CompassIcon />
                    {t('issues.detail.openInDiscover')}
                  </Link>
                ) : (
                  <Link to="/discover/tv/$tmdbId" params={{ tmdbId: String(issue.tmdbId) }}>
                    <CompassIcon />
                    {t('issues.detail.openInDiscover')}
                  </Link>
                )}
              </Button>
            )}
            {issue.libraryId != null && (
              <Button asChild variant="outline" size="sm">
                {issue.mediaType === 'movie' ? (
                  <Link to="/movies/$movieId" params={{ movieId: String(issue.libraryId) }} data-testid="issue-library-link">
                    <LibraryIcon />
                    {t('issues.detail.openInLibrary')}
                  </Link>
                ) : (
                  <Link to="/series/$seriesId" params={{ seriesId: String(issue.libraryId) }} data-testid="issue-library-link">
                    <LibraryIcon />
                    {t('issues.detail.openInLibrary')}
                  </Link>
                )}
              </Button>
            )}
          </div>
        </div>
      </header>

      <section className="rounded-lg border bg-muted/30 p-3" aria-label={t('issues.detail.message')}>
        <p className="text-sm whitespace-pre-wrap break-words" data-testid="issue-message">
          {issue.message}
        </p>
      </section>

      {issue.libraryId != null && <FileCard issue={issue} />}
      {issue.actions && <ActionsCard issue={issue} actions={issue.actions} onDone={apply} />}

      <Thread issue={issue} onChange={apply} />

      <div className="flex flex-wrap justify-end gap-2">
        {issue.canResolve && (
          <Button variant="outline" onClick={() => resolve.mutate()} disabled={resolve.isPending} data-testid="issue-resolve">
            {resolve.isPending ? <Loader2Icon className="animate-spin" /> : <CircleCheckIcon />}
            {t('issues.detail.resolve')}
          </Button>
        )}
        {issue.canReopen && (
          <Button variant="outline" onClick={() => reopen.mutate()} disabled={reopen.isPending} data-testid="issue-reopen">
            {reopen.isPending ? <Loader2Icon className="animate-spin" /> : <RotateCcwIcon />}
            {t('issues.detail.reopen')}
          </Button>
        )}
      </div>
    </div>
  )
}

function BackLink() {
  const { t } = useTranslation()
  return (
    <Link to="/issues" className="inline-flex w-fit items-center gap-1 text-sm text-muted-foreground hover:text-foreground">
      <ArrowLeftIcon className="size-4" aria-hidden="true" />
      {t('issues.back')}
    </Link>
  )
}

/** The admin's: the file reported and, when it changed since, the one there now. */
function FileCard({ issue }: { issue: Issue }) {
  const { t } = useTranslation()
  const noSingleFile = issue.scope === 'season' || issue.scope === 'series'
  return (
    <section className="flex flex-col gap-3 rounded-lg border p-3" data-testid="issue-file">
      {noSingleFile ? (
        <p className="text-sm text-muted-foreground">{t('issues.detail.noFile')}</p>
      ) : (
        <>
          <FileFacts heading={t('issues.detail.file')} file={issue.file} empty={t('issues.detail.noFileAtReport')} />
          {issue.fileChanged && <FileFacts heading={t('issues.detail.currentFile')} file={issue.currentFile} empty={t('issues.detail.fileGone')} />}
        </>
      )}
    </section>
  )
}

function FileFacts({ heading, file, empty }: { heading: string; file: IssueFile | null; empty: string }) {
  const { t } = useTranslation()
  return (
    <div className="flex flex-col gap-1.5">
      <h2 className="text-sm font-medium">{heading}</h2>
      {file ? (
        <dl className="grid grid-cols-[auto_minmax(0,1fr)] gap-x-3 gap-y-1 text-sm">
          <dt className="text-muted-foreground">{t('issues.detail.path')}</dt>
          <dd className="font-mono text-xs break-all">{file.path}</dd>
          <dt className="text-muted-foreground">{t('issues.detail.quality')}</dt>
          <dd>
            {file.quality ?? t('issues.detail.unknown')} · {formatBytes(file.size)}
          </dd>
          <dt className="text-muted-foreground">{t('issues.detail.release')}</dt>
          <dd className="font-mono text-xs break-all">{file.releaseTitle ?? t('issues.detail.unknown')}</dd>
          <dt className="text-muted-foreground">{t('issues.detail.indexer')}</dt>
          <dd>{file.indexerName ?? t('issues.detail.unknown')}</dd>
        </dl>
      ) : (
        <p className="text-sm text-muted-foreground">{empty}</p>
      )}
    </div>
  )
}

type Confirm = 'blocklist' | 'delete' | null

/** The approvers': search again, blocklist the release (and delete), delete the file; each confirmed when it removes something. */
function ActionsCard({ issue, actions, onDone }: { issue: Issue; actions: NonNullable<Issue['actions']>; onDone: (issue: Issue) => Promise<void> }) {
  const { t } = useTranslation()
  const [confirm, setConfirm] = useState<Confirm>(null)
  const [alsoDelete, setAlsoDelete] = useState(false)
  const [searchAfter, setSearchAfter] = useState(true)
  const done = async (updated: Issue) => {
    await onDone(updated)
    const outcome = updated.comments?.filter((c) => c.kind === 'action').at(-1)?.outcome
    if (outcome) toast.success(outcome)
    setConfirm(null)
  }
  const failed = (e: Error) => toast.error(e instanceof ApiError ? e.message : t('common.unexpectedError'))
  const search = useMutation({ mutationFn: () => issues.search(issue.id), onSuccess: done, onError: failed })
  const blocklist = useMutation({ mutationFn: () => issues.blocklist(issue.id, alsoDelete), onSuccess: done, onError: failed })
  const remove = useMutation({ mutationFn: () => issues.deleteFile(issue.id, searchAfter), onSuccess: done, onError: failed })
  const busy = search.isPending || blocklist.isPending || remove.isPending
  const fileScope = issue.scope === 'movie' || issue.scope === 'episode'
  const note = actions.managedBy
    ? actions.managedBy === 'shadow'
      ? t('issues.actions.managedShadow')
      : t('issues.actions.managedBy', { name: actions.managedBy })
    : !actions.search
      ? t('issues.actions.notInLibrary')
      : fileScope && actions.deleteFile && !actions.blocklist
        ? t('issues.actions.noRelease')
        : null

  return (
    <section className="flex flex-col gap-3 rounded-lg border p-3" data-testid="issue-actions">
      <h2 className="flex items-center gap-1.5 text-sm font-medium">
        <WrenchIcon className="size-4" aria-hidden="true" />
        {t('issues.actions.title')}
      </h2>
      <div className="grid gap-2 sm:flex sm:flex-wrap">
        <Button variant="outline" onClick={() => search.mutate()} disabled={!actions.search || busy} data-testid="issue-action-search">
          {search.isPending ? <Loader2Icon className="animate-spin" /> : <SearchIcon />}
          {t('issues.actions.search')}
        </Button>
        {fileScope && (
          <>
            <Button variant="outline" onClick={() => setConfirm('blocklist')} disabled={!actions.blocklist || busy} data-testid="issue-action-blocklist">
              <BanIcon />
              {t('issues.actions.blocklist')}
            </Button>
            <Button variant="outline" className="text-destructive hover:text-destructive" onClick={() => setConfirm('delete')} disabled={!actions.deleteFile || busy} data-testid="issue-action-delete">
              <Trash2Icon />
              {t('issues.actions.deleteFile')}
            </Button>
          </>
        )}
      </div>
      {note && (
        <p className="text-xs text-muted-foreground" data-testid="issue-actions-note">
          {note}
        </p>
      )}

      <AlertDialog open={confirm !== null} onOpenChange={(open) => !open && !busy && setConfirm(null)}>
        <AlertDialogContent data-testid="issue-action-confirm">
          <AlertDialogHeader>
            <AlertDialogTitle>{t(confirm === 'delete' ? 'issues.actions.deleteTitle' : 'issues.actions.blocklistTitle')}</AlertDialogTitle>
            <AlertDialogDescription>{t(confirm === 'delete' ? 'issues.actions.deleteBody' : 'issues.actions.blocklistBody')}</AlertDialogDescription>
          </AlertDialogHeader>
          {issue.currentFile ?? issue.file ? <p className="font-mono text-xs break-all text-muted-foreground">{(issue.fileChanged ? issue.currentFile : issue.file)?.path}</p> : null}
          <div className="flex items-start gap-2">
            {confirm === 'delete' ? (
              <>
                <Checkbox id="issue-search-after" checked={searchAfter} onCheckedChange={(v) => setSearchAfter(v === true)} />
                <Label htmlFor="issue-search-after" className="leading-snug font-normal">
                  {t('issues.actions.deleteSearch')}
                </Label>
              </>
            ) : (
              <>
                <Checkbox id="issue-also-delete" checked={alsoDelete} onCheckedChange={(v) => setAlsoDelete(v === true)} />
                <Label htmlFor="issue-also-delete" className="leading-snug font-normal">
                  {t('issues.actions.blocklistDelete')}
                </Label>
              </>
            )}
          </div>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={busy}>{t('common.cancel')}</AlertDialogCancel>
            <AlertDialogAction
              className={cn(confirm === 'delete' && 'bg-destructive text-white hover:bg-destructive/90')}
              disabled={busy}
              onClick={(e) => {
                e.preventDefault()
                if (confirm === 'delete') remove.mutate()
                else blocklist.mutate()
              }}
              data-testid="issue-action-confirm-submit"
            >
              {(blocklist.isPending || remove.isPending) && <Loader2Icon className="animate-spin" />}
              {t('issues.actions.confirm')}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </section>
  )
}

/** The conversation: comments, and the lines the app writes for actions, resolves and reopens; then the reply box. */
function Thread({ issue, onChange }: { issue: Issue; onChange: (issue: Issue) => Promise<void> }) {
  const { t } = useTranslation()
  const [text, setText] = useState('')
  const send = useMutation({
    mutationFn: () => issues.comment(issue.id, text.trim()),
    onSuccess: async (updated) => {
      setText('')
      await onChange(updated)
    },
    onError: (e: Error) => toast.error(e instanceof ApiError ? e.message : t('common.unexpectedError')),
  })
  const comments = issue.comments ?? []

  return (
    <section className="flex flex-col gap-3" aria-label={t('issues.detail.thread')}>
      <h2 className="text-lg font-semibold tracking-tight">{t('issues.detail.thread')}</h2>
      {comments.length === 0 ? (
        <p className="text-sm text-muted-foreground">{t('issues.detail.noComments')}</p>
      ) : (
        <ol className="flex flex-col gap-2" data-testid="issue-thread">
          {comments.map((c) => (c.kind === 'comment' ? <CommentItem key={c.id} issueId={issue.id} comment={c} reporterId={issue.reportedBy.id} onChange={onChange} /> : <EventItem key={c.id} comment={c} />))}
        </ol>
      )}
      {issue.canComment && (
        <form
          className="flex flex-col gap-2"
          onSubmit={(e) => {
            e.preventDefault()
            if (text.trim()) send.mutate()
          }}
        >
          <textarea className={textarea} value={text} onChange={(e) => setText(e.target.value)} maxLength={4000} placeholder={t('issues.detail.commentPlaceholder')} aria-label={t('issues.detail.commentPlaceholder')} data-testid="issue-comment-input" />
          <Button type="submit" className="self-end" disabled={!text.trim() || send.isPending} data-testid="issue-comment-send">
            {send.isPending ? <Loader2Icon className="animate-spin" /> : <SendIcon />}
            {t('issues.detail.send')}
          </Button>
        </form>
      )}
    </section>
  )
}

function CommentItem({ issueId, comment: c, reporterId, onChange }: { issueId: number; comment: IssueComment; reporterId: number; onChange: (issue: Issue) => Promise<void> }) {
  const { t } = useTranslation()
  const [editing, setEditing] = useState<string | null>(null)
  const failed = (e: Error) => toast.error(e instanceof ApiError ? e.message : t('common.unexpectedError'))
  const save = useMutation({
    mutationFn: () => issues.editComment(issueId, c.id, (editing ?? '').trim()),
    onSuccess: async (updated) => {
      setEditing(null)
      await onChange(updated)
    },
    onError: failed,
  })
  const remove = useMutation({ mutationFn: () => issues.deleteComment(issueId, c.id), onSuccess: onChange, onError: failed })
  const byReporter = c.author?.id === reporterId

  return (
    <li className={cn('flex flex-col gap-1 rounded-lg border p-3', !byReporter && 'border-primary/30 bg-primary/5')} data-testid={`issue-comment-${c.id}`}>
      <div className="flex flex-wrap items-baseline gap-x-2 text-xs text-muted-foreground">
        <span className="font-medium text-foreground">{c.author?.username ?? t('issues.detail.someone')}</span>
        <span>{formatDateTime(c.createdAt)}</span>
        {c.editedAt && <span>· {t('issues.detail.edited')}</span>}
        {(c.canEdit || c.canDelete) && editing === null && (
          <span className="ml-auto flex gap-1">
            {c.canEdit && (
              <Button variant="ghost" size="icon-sm" onClick={() => setEditing(c.message)} aria-label={t('issues.detail.edit')}>
                <PencilIcon />
              </Button>
            )}
            {c.canDelete && (
              <Button variant="ghost" size="icon-sm" onClick={() => remove.mutate()} disabled={remove.isPending} aria-label={t('issues.detail.delete')}>
                <Trash2Icon />
              </Button>
            )}
          </span>
        )}
      </div>
      {editing === null ? (
        <p className="text-sm whitespace-pre-wrap break-words">{c.message}</p>
      ) : (
        <form
          className="flex flex-col gap-2"
          onSubmit={(e) => {
            e.preventDefault()
            if (editing.trim()) save.mutate()
          }}
        >
          <textarea className={textarea} value={editing} onChange={(e) => setEditing(e.target.value)} maxLength={4000} aria-label={t('issues.detail.edit')} />
          <div className="flex justify-end gap-2">
            <Button type="button" variant="ghost" size="sm" onClick={() => setEditing(null)}>
              {t('common.cancel')}
            </Button>
            <Button type="submit" size="sm" disabled={!editing.trim() || save.isPending}>
              {t('issues.detail.saveEdit')}
            </Button>
          </div>
        </form>
      )}
    </li>
  )
}

/** A line the app wrote: an action with its outcome (and, for the admin, its details), a resolve or a reopen. */
function EventItem({ comment: c }: { comment: IssueComment }) {
  const { t } = useTranslation()
  const user = c.author?.username ?? t('issues.detail.someone')
  const Icon = c.kind === 'resolved' ? CircleCheckIcon : c.kind === 'reopened' ? RotateCcwIcon : c.action === 'deleteFile' ? Trash2Icon : c.action === 'blocklistAndSearch' ? BanIcon : SearchIcon
  const text =
    c.kind === 'resolved' ? t('issues.detail.resolvedBy', { user }) : c.kind === 'reopened' ? t('issues.detail.reopenedBy', { user }) : t(`issues.actions.line.${c.action ?? 'search'}`, { user })
  return (
    <li className="flex gap-2 px-1 py-1 text-sm" data-testid={`issue-event-${c.id}`}>
      <Icon className="mt-0.5 size-4 shrink-0 text-muted-foreground" aria-hidden="true" />
      <div className="flex min-w-0 flex-col gap-0.5">
        <p>
          <span className="font-medium">{text}</span> <span className="text-xs text-muted-foreground">· {formatDateTime(c.createdAt)}</span>
        </p>
        {c.outcome && <p className="text-muted-foreground">{c.outcome}</p>}
        {c.detail && <p className="font-mono text-xs break-all text-muted-foreground">{c.detail}</p>}
      </div>
    </li>
  )
}
