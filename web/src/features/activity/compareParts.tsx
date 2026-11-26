import { useState } from 'react'
import { cn } from 'cn'
import { useTranslation } from 'react-i18next'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'

/**
 * What the Radarr and the Sonarr Compare pages draw the same way: the period, version and window selects, the outcome
 * tiles, the table per version and the pager. The rows and their cards differ (a film, or a show and its episodes) and
 * stay with each page.
 */

export const comparePageSize = 50
const periods = [1, 7, 30, 90, 0] as const
const windows = [6, 24, 72] as const

/** The page shown, and a wrapper for the filters' setters: a filter that changes goes back to the first page. */
export function useComparePage() {
  const [page, setPage] = useState(1)
  const resetPage =
    <T,>(set: (value: T) => void) =>
    (value: T) => {
      set(value)
      setPage(1)
    }
  return { page, setPage, resetPage }
}

/** How far back to compare, in days; 0 is everything. */
export function ComparePeriodSelect({ value, onChange, testId }: { value: number; onChange: (days: number) => void; testId?: string }) {
  const { t } = useTranslation()
  return (
    <Select value={String(value)} onValueChange={(v) => onChange(Number(v))}>
      <SelectTrigger size="sm" aria-label={t('compare.period')} data-testid={testId}>
        <SelectValue />
      </SelectTrigger>
      <SelectContent>
        {periods.map((days) => (
          <SelectItem key={days} value={String(days)}>
            {days === 0 ? t('compare.periodAll') : t('compare.periodDays', { count: days })}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  )
}

/** One version by its name, or all of them (`all`). */
export function CompareVersionSelect({ value, onChange, versions, testId }: { value: string; onChange: (version: string) => void; versions: readonly string[] | undefined; testId?: string }) {
  const { t } = useTranslation()
  return (
    <Select value={value} onValueChange={onChange}>
      <SelectTrigger size="sm" aria-label={t('compare.version')} data-testid={testId}>
        <SelectValue />
      </SelectTrigger>
      <SelectContent>
        <SelectItem value="all">{t('compare.allVersions')}</SelectItem>
        {versions?.map((name) => (
          <SelectItem key={name} value={name}>
            {name}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  )
}

/** How many hours apart a grab and a pick may be and still be the same decision. */
export function CompareWindowSelect({ value, onChange }: { value: number; onChange: (hours: number) => void }) {
  const { t } = useTranslation()
  return (
    <Select value={String(value)} onValueChange={(v) => onChange(Number(v))}>
      <SelectTrigger size="sm" aria-label={t('compare.window')}>
        <SelectValue />
      </SelectTrigger>
      <SelectContent>
        {windows.map((hours) => (
          <SelectItem key={hours} value={String(hours)}>
            {t('compare.windowHours', { hours })}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  )
}

/**
 * One tile per outcome: its count and what it means. A click filters the rows on it, a second click shows them all
 * again. `agreement` is the sentence under the tiles; `className` sets how many tiles a wide screen puts on a row.
 */
export function CompareSummary<O extends string>({
  outcomes,
  counts,
  active,
  onPick,
  styles,
  label,
  hint,
  agreement,
  className,
  testIdPrefix,
}: {
  outcomes: readonly O[]
  counts: Record<O, number>
  active: O | 'all'
  onPick: (value: O | 'all') => void
  styles: Record<O, string>
  label: (outcome: O) => string
  hint: (outcome: O) => string
  agreement: string
  className: string
  testIdPrefix: string
}) {
  return (
    <div className="flex flex-col gap-2" data-testid={`${testIdPrefix}-summary`}>
      <dl className={cn('grid grid-cols-2 gap-3', className)}>
        {outcomes.map((o) => (
          <button
            key={o}
            type="button"
            onClick={() => onPick(active === o ? 'all' : o)}
            className={cn('rounded-lg border p-3 text-left transition-colors hover:bg-muted/50', active === o && 'ring-2 ring-ring')}
            aria-pressed={active === o}
          >
            <dt className="flex items-center gap-2 text-xs text-muted-foreground">
              <span className={cn('inline-block size-2 rounded-full border', styles[o])} />
              {label(o)}
            </dt>
            <dd className="text-2xl font-semibold tabular-nums">{counts[o]}</dd>
            <dd className="text-xs text-muted-foreground">{hint(o)}</dd>
          </button>
        ))}
      </dl>
      <p className="text-sm text-muted-foreground" data-testid={`${testIdPrefix}-agreement`}>
        {agreement}
      </p>
    </div>
  )
}

/** The outcome counts per version, one line each; the pages show it once there is more than one version. */
export function CompareByVersion<O extends string>({
  rows,
  outcomes,
  label,
  testId,
}: {
  rows: readonly { versionName: string; counts: Record<O, number> }[]
  outcomes: readonly O[]
  label: (outcome: O) => string
  testId: string
}) {
  const { t } = useTranslation()
  return (
    <div className="overflow-x-auto rounded-lg border sm:max-w-2xl" data-testid={testId}>
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>{t('compare.version')}</TableHead>
            {outcomes.map((o) => (
              <TableHead key={o} className="text-right">
                {label(o)}
              </TableHead>
            ))}
          </TableRow>
        </TableHeader>
        <TableBody>
          {rows.map((v) => (
            <TableRow key={v.versionName}>
              <TableCell>
                <Badge variant="secondary">{v.versionName}</Badge>
              </TableCell>
              {outcomes.map((o) => (
                <TableCell key={o} className="text-right tabular-nums">
                  {v.counts[o]}
                </TableCell>
              ))}
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  )
}

/** Previous, "page 2 of 5", next; nothing while everything fits on one page. */
export function ComparePager({ page, pages, onPage }: { page: number; pages: number; onPage: (page: number) => void }) {
  const { t } = useTranslation()
  if (pages <= 1) return null
  return (
    <div className="flex items-center gap-2">
      <Button type="button" variant="outline" size="sm" onClick={() => onPage(Math.max(1, page - 1))} disabled={page <= 1}>
        {t('compare.previous')}
      </Button>
      <span className="text-sm text-muted-foreground tabular-nums">{t('compare.page', { page, pages })}</span>
      <Button type="button" variant="outline" size="sm" onClick={() => onPage(Math.min(pages, page + 1))} disabled={page >= pages}>
        {t('compare.next')}
      </Button>
    </div>
  )
}
