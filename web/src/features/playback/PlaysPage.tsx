import { useDeferredValue, useState } from 'react'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { useSearch } from '@tanstack/react-router'
import { SearchIcon, XIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { playback, type MediaItemKind, type Play, type PlaySortKey, type StreamDecision } from '@/api/client'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import { PlayDetailDialog } from './PlayDetailDialog'
import { PlaysTable, type PlaySort } from './PlaysTable'
import { UserPicker } from './UserPicker'

const pageSize = 50

/** Text columns start A→Z, the others biggest or newest first. */
const firstDirection: Record<PlaySortKey, 'asc' | 'desc'> = { startedAt: 'desc', user: 'asc', title: 'asc', version: 'asc', played: 'desc' }

const periods = ['any', '1', '7', '30', '90', '365', 'custom'] as const
type Period = (typeof periods)[number]

const decisions: StreamDecision[] = ['directPlay', 'directStream', 'transcode']

type Filters = {
  search: string
  userId: number | null
  /** `unmatched`: the film plays linked to no Bigarr film. */
  kind: 'all' | MediaItemKind | 'unmatched'
  version: string
  decision: 'all' | StreamDecision
  period: Period
  /** `YYYY-MM-DD` from the date inputs, local days, both included. */
  from: string
  to: string
}

const noFilters: Filters = { search: '', userId: null, kind: 'all', version: 'all', decision: 'all', period: 'any', from: '', to: '' }

/** The start of a local day as an instant, `days` days after `date`. */
function localDay(date: string, days = 0): string {
  const [y, m, d] = date.split('-').map(Number)
  return new Date(y, m - 1, d + days).toISOString()
}

/** The period as the API's bounds: the last N days end now; a custom range covers its local days, the last one included. */
function bounds(filters: Filters): { from?: string; to?: string } {
  if (filters.period === 'any') return {}
  if (filters.period === 'custom') return { from: filters.from ? localDay(filters.from) : undefined, to: filters.to ? localDay(filters.to, 1) : undefined }
  return { from: new Date(Date.now() - Number(filters.period) * 86_400_000).toISOString() }
}

/** Every play followed on the media servers, newest first, filtered by title, user, kind, version, decision and period. */
export function PlaysPage() {
  const { t } = useTranslation()
  const [page, setPage] = useState(1)
  // `?kind=unmatched` (the Tautulli import report's link) opens the page on that filter.
  const initialKind = useSearch({ from: '/app/playback/plays' }).kind
  const [filters, setFilters] = useState<Filters>(() => ({ ...noFilters, kind: initialKind ?? 'all' }))
  const [sort, setSort] = useState<PlaySort>({ key: 'startedAt', direction: 'desc' })
  const [selected, setSelected] = useState<Play | null>(null)
  const search = useDeferredValue(filters.search.trim())
  const users = useQuery({ queryKey: ['playback', 'users'], queryFn: playback.users })
  const versions = useQuery({ queryKey: ['playback', 'playVersions'], queryFn: playback.playVersions })
  // The relative periods are worked out once per change, not on every render: the query key stays put.
  const [range, setRange] = useState(() => bounds(noFilters))
  const query = useQuery({
    queryKey: ['playback', 'plays', page, filters.userId, filters.kind, filters.version, filters.decision, range, search, sort],
    queryFn: () =>
      playback.plays({
        page,
        pageSize,
        userId: filters.userId ?? undefined,
        kind: filters.kind === 'all' || filters.kind === 'unmatched' ? undefined : filters.kind,
        unmatched: filters.kind === 'unmatched' || undefined,
        version: filters.version === 'all' ? undefined : filters.version,
        decision: filters.decision === 'all' ? undefined : filters.decision,
        q: search || undefined,
        ...range,
        sort: sort.key,
        direction: sort.direction,
      }),
    placeholderData: keepPreviousData,
  })
  const data = query.data
  const pages = data ? Math.max(1, Math.ceil(data.totalRecords / pageSize)) : 1
  const filtered = JSON.stringify(filters) !== JSON.stringify(noFilters)
  const set = (patch: Partial<Filters>) => {
    const next = { ...filters, ...patch }
    setFilters(next)
    setRange(bounds(next))
    setPage(1)
  }
  const onSort = (key: PlaySortKey) => {
    setSort((current) => (current.key === key ? { key, direction: current.direction === 'asc' ? 'desc' : 'asc' } : { key, direction: firstDirection[key] }))
    setPage(1)
  }

  return (
    <>
      <h1 className="text-2xl font-semibold tracking-tight">{t('playback.playsTitle')}</h1>
      <div className="flex flex-wrap items-center gap-2" data-testid="plays-filters">
        <div className="relative w-full sm:w-64">
          <SearchIcon className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-muted-foreground" aria-hidden="true" />
          <Input
            type="search"
            value={filters.search}
            onChange={(e) => set({ search: e.target.value })}
            placeholder={t('playback.searchTitle')}
            aria-label={t('playback.searchTitle')}
            className="pl-8"
            data-testid="plays-search"
          />
        </div>
        <UserPicker users={users.data ?? []} value={filters.userId} onChange={(id) => set({ userId: id })} />
        <Select value={filters.kind} onValueChange={(v) => set({ kind: v as Filters['kind'] })}>
          <SelectTrigger className="min-w-32" aria-label={t('playback.kind')}>
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="all">{t('playback.allKinds')}</SelectItem>
            <SelectItem value="movie">{t('playback.kinds.movie')}</SelectItem>
            <SelectItem value="episode">{t('playback.kinds.episode')}</SelectItem>
            <SelectItem value="unmatched">{t('playback.kinds.unmatched')}</SelectItem>
          </SelectContent>
        </Select>
        <Select value={filters.version} onValueChange={(v) => set({ version: v })}>
          <SelectTrigger className="min-w-32" aria-label={t('playback.version')} data-testid="plays-version">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="all">{t('playback.allVersions')}</SelectItem>
            {(versions.data ?? []).map((name) => (
              <SelectItem key={name} value={name}>
                {name}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
        <Select value={filters.decision} onValueChange={(v) => set({ decision: v as Filters['decision'] })}>
          <SelectTrigger className="min-w-36" aria-label={t('playback.decision')} data-testid="plays-decision">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="all">{t('playback.allDecisions')}</SelectItem>
            {decisions.map((decision) => (
              <SelectItem key={decision} value={decision}>
                {t(`playback.decisions.${decision}`)}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
        <Select value={filters.period} onValueChange={(v) => set({ period: v as Period })}>
          <SelectTrigger className="min-w-36" aria-label={t('playback.period')} data-testid="plays-period">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {periods.map((period) => (
              <SelectItem key={period} value={period}>
                {period === 'any' ? t('playback.anyTime') : period === 'custom' ? t('playback.customRange') : t('playback.lastDays', { count: Number(period) })}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
        {filters.period === 'custom' && (
          <span className="flex items-center gap-1.5 text-sm text-muted-foreground">
            <Input type="date" value={filters.from} max={filters.to || undefined} onChange={(e) => set({ from: e.target.value })} aria-label={t('playback.from')} className="w-36" data-testid="plays-from" />
            {t('playback.to')}
            <Input type="date" value={filters.to} min={filters.from || undefined} onChange={(e) => set({ to: e.target.value })} aria-label={t('playback.until')} className="w-36" data-testid="plays-to" />
          </span>
        )}
        {filtered && (
          <Button variant="ghost" size="sm" onClick={() => set(noFilters)} data-testid="plays-clear">
            <XIcon />
            {t('playback.clearFilters')}
          </Button>
        )}
      </div>

      {query.isPending && <Skeleton className="h-40 w-full" />}
      {query.error && (
        <Alert variant="destructive">
          <AlertDescription>{t('playback.loadError')}</AlertDescription>
        </Alert>
      )}
      {data && data.records.length === 0 && <p className="text-sm text-muted-foreground">{filtered ? t('playback.noMatch') : t('playback.noPlays')}</p>}
      {data && data.records.length > 0 && (
        <>
          <PlaysTable plays={data.records} sort={sort} onSort={onSort} onSelect={setSelected} />
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
    </>
  )
}
