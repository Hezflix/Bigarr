import type { ReactNode } from 'react'
import { SearchIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import type { WantedFilter } from '@/api/client'
import { Poster } from '@/components/Poster'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { formatAge, formatDateTime } from '@/i18n'

/** What the film and series Wanted pages share: the frame around the table, and the columns about the version and its searches. */

export const wantedPageSize = 50

export type WantedFilterChoice = WantedFilter | 'all'

const kindStyles: Record<WantedFilter, string> = {
  missing: 'border-amber-600/30 bg-amber-500/15 text-amber-700 dark:border-amber-400/30 dark:text-amber-300',
  cutoffUnmet: 'border-blue-600/30 bg-blue-500/15 text-blue-700 dark:border-blue-400/30 dark:text-blue-300',
}

/** The fields both kinds of wanted line carry. */
export type WantedCommon = {
  versionName: string
  qualityProfile: string
  kind: WantedFilter
  currentQuality: string | null
  currentScore: number | null
  cutoffQuality: string
  lastSearchAt: string | null
  searchAttempts: number
}

/** Title, filter tabs, "search all", hint, then the table (or its loading and empty states) and the pager. */
export function WantedFrame({
  testId,
  title,
  hint,
  empty,
  count,
  filter,
  onFilter,
  page,
  onPage,
  total,
  loading,
  onSearchAll,
  searchingAll,
  head,
  children,
}: {
  /** Prefix of every test id of the page. */
  testId: string
  title: string
  hint: string
  empty: string
  count: string
  filter: WantedFilterChoice
  onFilter: (filter: WantedFilterChoice) => void
  page: number
  onPage: (update: (page: number) => number) => void
  /** Undefined until the first page has loaded. */
  total: number | undefined
  loading: boolean
  onSearchAll: () => void
  searchingAll: boolean
  head: ReactNode
  children: ReactNode
}) {
  const { t } = useTranslation()
  const pages = total !== undefined ? Math.max(1, Math.ceil(total / wantedPageSize)) : 1

  return (
    <>
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="text-2xl font-semibold tracking-tight">{title}</h1>
        <div className="ml-auto flex flex-wrap items-center gap-2">
          <div className="flex rounded-md border p-0.5" role="tablist" aria-label={t('wanted.filter')}>
            {(['all', 'missing', 'cutoffUnmet'] as const).map((value) => (
              <Button
                key={value}
                type="button"
                role="tab"
                aria-selected={filter === value}
                variant={filter === value ? 'secondary' : 'ghost'}
                size="sm"
                onClick={() => onFilter(value)}
                data-testid={`${testId}-filter-${value}`}
              >
                {t(`wanted.filters.${value}`)}
              </Button>
            ))}
          </div>
          <Button type="button" size="sm" onClick={onSearchAll} disabled={searchingAll || !total} data-testid={`${testId}-search-all`}>
            <SearchIcon />
            {t('wanted.searchAll')}
          </Button>
        </div>
      </div>
      <p className="max-w-3xl text-sm text-muted-foreground">{hint}</p>

      {loading && (
        <div className="flex flex-col gap-2">
          <Skeleton className="h-8 w-full" />
          <Skeleton className="h-8 w-full" />
        </div>
      )}

      {total === 0 && <p className="text-sm text-muted-foreground">{empty}</p>}

      {total !== undefined && total > 0 && (
        <>
          <p className="text-sm text-muted-foreground" data-testid={`${testId}-count`}>
            {count}
          </p>
          <div className="overflow-x-auto rounded-lg border">
            <Table data-testid={`${testId}-table`}>
              <TableHeader>
                <TableRow>{head}</TableRow>
              </TableHeader>
              <TableBody>{children}</TableBody>
            </Table>
          </div>
          {pages > 1 && (
            <div className="flex items-center justify-end gap-2 text-sm">
              <Button type="button" variant="outline" size="sm" onClick={() => onPage((p) => Math.max(1, p - 1))} disabled={page <= 1}>
                {t('common.previous')}
              </Button>
              <span className="text-muted-foreground">{t('common.page', { page, pages })}</span>
              <Button type="button" variant="outline" size="sm" onClick={() => onPage((p) => Math.min(pages, p + 1))} disabled={page >= pages}>
                {t('common.next')}
              </Button>
            </div>
          )}
        </>
      )}
    </>
  )
}

/** Poster, title and year: what goes inside the link to the film or the series. */
export function WantedTitle({ posterUrl, title, year }: { posterUrl: string | null; title: string; year: number | null }) {
  return (
    <>
      <Poster url={posterUrl} title={title} size="small" />
      <span className="font-medium">
        {title}
        {year ? <span className="font-normal text-muted-foreground"> ({year})</span> : null}
      </span>
    </>
  )
}

/** Headers of {@link WantedVersionCells}, then "next search" (hidden when the scheduler is off: none will come) and the actions. */
export function WantedVersionHeads({ schedulerOff }: { schedulerOff: boolean }) {
  const { t } = useTranslation()
  return (
    <>
      <TableHead>{t('wanted.version')}</TableHead>
      <TableHead>{t('wanted.kind')}</TableHead>
      <TableHead>{t('wanted.current')}</TableHead>
      <TableHead>{t('wanted.cutoff')}</TableHead>
      <TableHead>{t('wanted.lastSearch')}</TableHead>
      {!schedulerOff && <TableHead>{t('wanted.nextSearch')}</TableHead>}
      <TableHead className="text-right">{t('versions.actions')}</TableHead>
    </>
  )
}

/** Version and profile, missing or cutoff unmet, the file's quality and score, the cutoff, the last search. */
export function WantedVersionCells({ item }: { item: WantedCommon }) {
  const { t } = useTranslation()
  return (
    <>
      <TableCell className="align-top">
        <span className="font-medium">{item.versionName}</span>
        <span className="block text-xs text-muted-foreground">{item.qualityProfile}</span>
      </TableCell>
      <TableCell className="align-top">
        <Badge variant="outline" className={kindStyles[item.kind]}>
          {t(`wanted.filters.${item.kind}`)}
        </Badge>
      </TableCell>
      <TableCell className="align-top whitespace-nowrap">
        {item.currentQuality ? (
          <>
            {item.currentQuality}
            {item.currentScore !== null && <span className="block text-xs text-muted-foreground">{t('versions.score', { score: item.currentScore })}</span>}
          </>
        ) : (
          <span className="text-muted-foreground">{t('common.none')}</span>
        )}
      </TableCell>
      <TableCell className="align-top whitespace-nowrap">{item.cutoffQuality}</TableCell>
      <TableCell className="align-top whitespace-nowrap">
        {item.lastSearchAt ? (
          <Tooltip>
            <TooltipTrigger asChild>
              <span>
                {formatAge(item.lastSearchAt)}
                {item.searchAttempts > 0 && <span className="block text-xs text-muted-foreground">{t('wanted.attempts', { count: item.searchAttempts })}</span>}
              </span>
            </TooltipTrigger>
            <TooltipContent>{formatDateTime(item.lastSearchAt)}</TooltipContent>
          </Tooltip>
        ) : (
          <span className="text-muted-foreground">{t('wanted.never')}</span>
        )}
      </TableCell>
    </>
  )
}
