import { useDeferredValue, useMemo, useState } from 'react'
import { Link } from '@tanstack/react-router'
import { useQuery } from '@tanstack/react-query'
import { cn } from 'cn'
import { LayoutGridIcon, PlusIcon, SearchIcon, TableIcon } from 'lucide-react'
import { Trans, useTranslation } from 'react-i18next'
import { series as seriesApi, type SeriesSummary } from '@/api/client'
import { Poster } from '@/components/Poster'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { formatDate } from '@/i18n'
import { EpisodeProgress } from './EpisodeProgress'

type Mode = 'posters' | 'table'

const modeKey = 'bigarr.series.layout'
const grid = 'grid grid-cols-2 gap-4 sm:grid-cols-3 md:grid-cols-4 xl:grid-cols-5 2xl:grid-cols-6'

function readMode(): Mode {
  try {
    return localStorage.getItem(modeKey) === 'table' ? 'table' : 'posters'
  } catch {
    return 'posters'
  }
}

function matches(s: SeriesSummary, term: string) {
  return term === '' || s.title.toLowerCase().includes(term) || (s.network?.toLowerCase().includes(term) ?? false)
}

/** The default version speaks for the series in the library, as in the film library. */
function defaultVersion(s: SeriesSummary) {
  return s.versions.find((v) => v.isDefault) ?? s.versions[0]
}

export function SeriesLibraryPage() {
  const { t } = useTranslation()
  const library = useQuery({ queryKey: ['series'], queryFn: seriesApi.list })
  const [term, setTerm] = useState('')
  const search = useDeferredValue(term.trim().toLowerCase())
  const [mode, setMode] = useState<Mode>(readMode)
  const items = useMemo(() => library.data ?? [], [library.data])
  const shown = useMemo(() => items.filter((s) => matches(s, search)), [items, search])

  const changeMode = (next: Mode) => {
    setMode(next)
    try {
      localStorage.setItem(modeKey, next)
    } catch {
      /* private window: the choice lasts for the visit */
    }
  }

  if (library.error) {
    return (
      <Alert variant="destructive">
        <AlertDescription>{t('seriesLibrary.loadError')}</AlertDescription>
      </Alert>
    )
  }

  return (
    <>
      <div className="flex flex-wrap items-center gap-x-3 gap-y-2">
        <h1 className="text-2xl font-semibold tracking-tight">{t('seriesLibrary.title')}</h1>
        {library.data && (
          <span className="text-sm text-muted-foreground tabular-nums" data-testid="series-count">
            {search === '' ? t('seriesLibrary.count', { count: items.length }) : t('seriesLibrary.countShown', { count: items.length, shown: shown.length })}
          </span>
        )}
        <div className="ml-auto flex items-center gap-2">
          <Button asChild>
            <Link to="/series/add">
              <PlusIcon />
              {t('seriesLibrary.add')}
            </Link>
          </Button>
        </div>
      </div>

      {library.isPending && (
        <div className={grid}>
          {Array.from({ length: 10 }, (_, i) => (
            <div key={i} className="flex flex-col gap-2">
              <Skeleton className="aspect-[2/3] w-full rounded-xl" />
              <Skeleton className="h-4 w-3/4" />
            </div>
          ))}
        </div>
      )}

      {library.data && items.length === 0 && (
        <Card className="items-center py-12 text-center">
          <CardContent className="flex flex-col gap-2">
            <p className="font-medium">{t('seriesLibrary.empty')}</p>
            <p className="text-sm text-muted-foreground">
              <Trans i18nKey="seriesLibrary.emptyHint" components={{ add: <Link to="/series/add" className="text-foreground underline underline-offset-4" /> }} />
            </p>
          </CardContent>
        </Card>
      )}

      {items.length > 0 && (
        <div className="flex flex-wrap items-center gap-2">
          <div className="relative w-full max-w-sm">
            <SearchIcon className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground" />
            <Input className="pl-9" placeholder={t('seriesLibrary.search')} value={term} onChange={(e) => setTerm(e.target.value)} aria-label={t('seriesLibrary.search')} />
          </div>
          <div className="ml-auto flex rounded-md border p-0.5" role="group" aria-label={t('seriesLibrary.layout')}>
            <Button type="button" size="sm" variant={mode === 'posters' ? 'secondary' : 'ghost'} aria-pressed={mode === 'posters'} onClick={() => changeMode('posters')}>
              <LayoutGridIcon />
              {t('seriesLibrary.posters')}
            </Button>
            <Button type="button" size="sm" variant={mode === 'table' ? 'secondary' : 'ghost'} aria-pressed={mode === 'table'} onClick={() => changeMode('table')}>
              <TableIcon />
              {t('seriesLibrary.table')}
            </Button>
          </div>
        </div>
      )}

      {items.length > 0 && shown.length === 0 && <p className="text-sm text-muted-foreground">{t('seriesLibrary.noMatch')}</p>}

      {shown.length > 0 && mode === 'posters' && (
        <div className={grid} data-testid="series-grid">
          {shown.map((s) => (
            <SeriesCard key={s.id} series={s} />
          ))}
        </div>
      )}
      {shown.length > 0 && mode === 'table' && <SeriesTable items={shown} />}
    </>
  )
}

function SeriesCard({ series: s }: { series: SeriesSummary }) {
  const { t } = useTranslation()
  const version = defaultVersion(s)
  return (
    <div className="group relative" data-testid="series-card">
      <Card className="relative h-full gap-0 overflow-hidden p-0 transition-colors group-hover:border-ring/40 group-hover:bg-accent/40">
        <Poster url={s.posterUrl} title={s.title} size="grid" className="rounded-none" />
        <CardContent className="flex flex-col gap-1.5 px-3 py-2.5">
          <span className="line-clamp-2 text-sm leading-snug font-medium">{s.title}</span>
          <span className="truncate text-xs text-muted-foreground">
            {[s.year, s.network, t('seriesLibrary.seasons', { count: s.seasonCount })].filter(Boolean).join(' · ')}
          </span>
          {version && <EpisodeProgress statistics={version.statistics} monitored={version.monitored} />}
          <div className="flex flex-wrap gap-1">
            {s.seriesType !== 'standard' && <Badge variant="secondary">{t(`seriesType.${s.seriesType}`)}</Badge>}
            {s.versions.map((v) => (
              <Badge key={v.id} variant="outline" className={cn(!v.monitored && 'opacity-60')}>
                {v.name}
              </Badge>
            ))}
          </div>
        </CardContent>
      </Card>
      <Link
        to="/series/$seriesId"
        params={{ seriesId: String(s.id) }}
        aria-label={s.title}
        title={s.title}
        className="absolute inset-0 rounded-xl focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
      />
    </div>
  )
}

function SeriesTable({ items }: { items: SeriesSummary[] }) {
  const { t } = useTranslation()
  return (
    <Card className="py-0">
      <Table data-testid="series-table">
        <TableHeader>
          <TableRow>
            <TableHead className="w-12" />
            <TableHead>{t('seriesLibrary.columns.title')}</TableHead>
            <TableHead>{t('seriesLibrary.columns.year')}</TableHead>
            <TableHead>{t('seriesLibrary.columns.network')}</TableHead>
            <TableHead>{t('seriesLibrary.columns.type')}</TableHead>
            <TableHead>{t('seriesLibrary.columns.status')}</TableHead>
            <TableHead className="text-right">{t('seriesLibrary.columns.seasons')}</TableHead>
            <TableHead className="w-44">{t('seriesLibrary.columns.episodes')}</TableHead>
            <TableHead>{t('seriesLibrary.columns.nextAiring')}</TableHead>
            <TableHead>{t('seriesLibrary.columns.versions')}</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {items.map((s) => {
            const version = defaultVersion(s)
            return (
              <TableRow key={s.id} className="relative">
                <TableCell className="py-1.5">
                  <Poster url={s.posterUrl} title={s.title} size="thumbMedium" />
                </TableCell>
                <TableCell className="font-medium">
                  <Link to="/series/$seriesId" params={{ seriesId: String(s.id) }} className="after:absolute after:inset-0 hover:underline">
                    {s.title}
                  </Link>
                </TableCell>
                <TableCell className="tabular-nums">{s.year ?? t('common.none')}</TableCell>
                <TableCell>{s.network ?? t('common.none')}</TableCell>
                <TableCell>{t(`seriesType.${s.seriesType}`)}</TableCell>
                <TableCell>{t(`seriesStatus.${s.status}`)}</TableCell>
                <TableCell className="text-right tabular-nums">{s.seasonCount}</TableCell>
                <TableCell>{version && <EpisodeProgress statistics={version.statistics} monitored={version.monitored} />}</TableCell>
                <TableCell className="tabular-nums">{s.nextAiring ? formatDate(s.nextAiring) : t('common.none')}</TableCell>
                <TableCell>
                  <div className="flex flex-wrap gap-1">
                    {s.versions.map((v) => (
                      <Badge key={v.id} variant="outline" className={cn(!v.monitored && 'opacity-60')}>
                        {v.name}
                      </Badge>
                    ))}
                  </div>
                </TableCell>
              </TableRow>
            )
          })}
        </TableBody>
      </Table>
    </Card>
  )
}
