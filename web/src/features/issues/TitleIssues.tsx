import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { FlagIcon, TriangleAlertIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { issues, type RequestMediaType } from '@/api/client'
import { Button } from '@/components/ui/button'
import { ReportIssueDialog } from './ReportIssueDialog'

/**
 * Issues on a Discover title page: the open ones the reader may see (their own; everyone's for an approver), linked to
 * the issue or the filtered list, and "Report an issue" when the title is in the library and they may report.
 */
export function TitleIssues({ mediaType, tmdbId, tvdbId, title }: { mediaType: RequestMediaType; tmdbId: number; tvdbId?: number | null; title: string }) {
  const { t } = useTranslation()
  const [reporting, setReporting] = useState(false)
  const query = useQuery({ queryKey: ['issues', 'title', mediaType, tmdbId], queryFn: () => issues.title(mediaType, tmdbId, tvdbId) })
  const data = query.data
  if (!data || (!data.canReport && data.open.length === 0)) return null
  const open = data.open

  return (
    <>
      {open.length > 0 &&
        (open.length === 1 ? (
          <Button asChild variant="outline" size="sm" className="border-amber-500/50 text-amber-700 dark:text-amber-300">
            <Link to="/issues/$issueId" params={{ issueId: String(open[0].id) }} data-testid="title-open-issues">
              <TriangleAlertIcon />
              {t('issues.openOnTitle', { count: 1 })}
            </Link>
          </Button>
        ) : (
          <Button asChild variant="outline" size="sm" className="border-amber-500/50 text-amber-700 dark:text-amber-300">
            <Link to="/issues" search={{ status: 'open', q: title }} data-testid="title-open-issues">
              <TriangleAlertIcon />
              {t('issues.openOnTitle', { count: open.length })}
            </Link>
          </Button>
        ))}
      {data.canReport && (
        <Button variant="outline" size="sm" onClick={() => setReporting(true)} data-testid="report-issue">
          <FlagIcon />
          {t('issues.report')}
        </Button>
      )}
      {reporting && <ReportIssueDialog mediaType={mediaType} tmdbId={tmdbId} tvdbId={tvdbId} title={title} options={data} onClose={() => setReporting(false)} />}
    </>
  )
}

/** On the admin's film or series page: how many issues are open on it, linking to them. Nothing when none. */
export function LibraryIssuesLink({ movieId, seriesId }: { movieId?: number; seriesId?: number }) {
  const { t } = useTranslation()
  const query = useQuery({
    queryKey: ['issues', 'library', movieId ?? null, seriesId ?? null],
    queryFn: () => issues.list({ status: 'open', movieId, seriesId, pageSize: 1 }),
  })
  const count = query.data?.totalRecords ?? 0
  if (count === 0) return null
  const first = query.data?.records[0]
  return (
    <div className="flex">
    <Button asChild variant="outline" size="sm" className="border-amber-500/50 text-amber-700 dark:text-amber-300">
      {count === 1 && first ? (
        <Link to="/issues/$issueId" params={{ issueId: String(first.id) }} data-testid="library-open-issues">
          <TriangleAlertIcon />
          {t('issues.library.open', { count })}
        </Link>
      ) : (
        <Link to="/issues" search={{ status: 'open', movieId, seriesId }} data-testid="library-open-issues">
          <TriangleAlertIcon />
          {t('issues.library.open', { count })}
        </Link>
      )}
    </Button>
    </div>
  )
}
