import { useMemo, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { cn } from 'cn'
import { CalendarDaysIcon, ChevronLeftIcon, ChevronRightIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { calendar, type CalendarEpisode, type CalendarStatus } from '@/api/client'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Label } from '@/components/ui/label'
import { Skeleton } from '@/components/ui/skeleton'
import { formatTime } from '@/i18n'

type View = 'week' | 'month'

/** Sonarr's calendar colours, one per status of the legend. */
export const statusStyles: Record<CalendarStatus, string> = {
  downloaded: 'border-l-emerald-500 bg-emerald-500/10',
  downloading: 'border-l-violet-500 bg-violet-500/10',
  missing: 'border-l-red-500 bg-red-500/10',
  onAir: 'border-l-amber-500 bg-amber-500/10',
  unaired: 'border-l-sky-500 bg-sky-500/10',
  unmonitored: 'border-l-zinc-400 bg-zinc-500/10 text-muted-foreground',
}

const statuses: CalendarStatus[] = ['unaired', 'onAir', 'missing', 'downloading', 'downloaded', 'unmonitored']

const dayKey = (date: Date) => `${date.getFullYear()}-${date.getMonth()}-${date.getDate()}`

/** Monday 00:00 local of the week holding <paramref name="date"/>. */
function startOfWeek(date: Date) {
  const d = new Date(date.getFullYear(), date.getMonth(), date.getDate())
  d.setDate(d.getDate() - ((d.getDay() + 6) % 7))
  return d
}

function addDays(date: Date, days: number) {
  const d = new Date(date)
  d.setDate(d.getDate() + days)
  return d
}

/** The days the grid shows: a week, or the weeks a month touches (Monday first). */
function range(view: View, anchor: Date): Date[] {
  if (view === 'week') {
    const start = startOfWeek(anchor)
    return Array.from({ length: 7 }, (_, i) => addDays(start, i))
  }

  const first = new Date(anchor.getFullYear(), anchor.getMonth(), 1)
  const last = new Date(anchor.getFullYear(), anchor.getMonth() + 1, 0)
  const start = startOfWeek(first)
  const end = addDays(startOfWeek(last), 7)
  const days: Date[] = []
  for (let d = start; d < end; d = addDays(d, 1)) days.push(d)
  return days
}

const code = (episode: CalendarEpisode) =>
  episode.seriesType === 'daily' ? '' : `S${String(episode.seasonNumber).padStart(2, '0')}E${String(episode.episodeNumber).padStart(2, '0')}`

/** Sonarr's main daily view: what airs this week or this month, per version, with its status. */
export function CalendarPage() {
  const { t, i18n } = useTranslation()
  const [view, setView] = useState<View>('week')
  const [anchor, setAnchor] = useState(() => new Date())
  const [unmonitored, setUnmonitored] = useState(false)
  const days = useMemo(() => range(view, anchor), [view, anchor])
  const start = days[0]
  const end = addDays(days[days.length - 1], 1)
  const list = useQuery({
    queryKey: ['calendar', start.toISOString(), end.toISOString(), unmonitored],
    queryFn: () => calendar.list(start, end, unmonitored),
    refetchInterval: 60_000,
  })

  const byDay = useMemo(() => {
    const map = new Map<string, CalendarEpisode[]>()
    for (const episode of list.data ?? []) {
      if (!episode.airDateUtc) continue
      const key = dayKey(new Date(episode.airDateUtc))
      map.set(key, [...(map.get(key) ?? []), episode])
    }
    return map
  }, [list.data])

  const title = new Intl.DateTimeFormat(i18n.language, view === 'month' ? { month: 'long', year: 'numeric' } : { day: 'numeric', month: 'long', year: 'numeric' }).format(view === 'month' ? anchor : start)
  const weekday = new Intl.DateTimeFormat(i18n.language, { weekday: 'short' })
  const today = dayKey(new Date())
  const move = (direction: number) => setAnchor((a) => (view === 'week' ? addDays(a, 7 * direction) : new Date(a.getFullYear(), a.getMonth() + direction, 1)))

  return (
    <>
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="text-2xl font-semibold tracking-tight">{t('calendar.title')}</h1>
        <div className="ml-auto flex flex-wrap items-center gap-2">
          <Button type="button" variant="outline" size="sm" onClick={() => setAnchor(new Date())} data-testid="calendar-today">
            <CalendarDaysIcon />
            {t('calendar.today')}
          </Button>
          <div className="flex items-center">
            <Button type="button" variant="ghost" size="icon" onClick={() => move(-1)} aria-label={t('common.previous')}>
              <ChevronLeftIcon />
            </Button>
            <span className="min-w-40 text-center text-sm font-medium" data-testid="calendar-range">
              {view === 'week' ? t('calendar.weekOf', { date: title }) : title}
            </span>
            <Button type="button" variant="ghost" size="icon" onClick={() => move(1)} aria-label={t('common.next')}>
              <ChevronRightIcon />
            </Button>
          </div>
          <div className="flex rounded-md border p-0.5" role="tablist" aria-label={t('calendar.view')}>
            {(['week', 'month'] as const).map((value) => (
              <Button key={value} type="button" role="tab" aria-selected={view === value} variant={view === value ? 'secondary' : 'ghost'} size="sm" onClick={() => setView(value)} data-testid={`calendar-view-${value}`}>
                {t(`calendar.views.${value}`)}
              </Button>
            ))}
          </div>
        </div>
      </div>

      <div className="flex flex-wrap items-center gap-x-4 gap-y-2 text-xs" data-testid="calendar-legend">
        {statuses.map((status) => (
          <span key={status} className="flex items-center gap-1.5">
            <span className={cn('h-3 w-3 rounded-sm border-l-4', statusStyles[status])} />
            {t(`calendar.status.${status}`)}
          </span>
        ))}
        <span className="ml-auto flex items-center gap-2">
          <Checkbox id="calendar-unmonitored" checked={unmonitored} onCheckedChange={(v) => setUnmonitored(v === true)} />
          <Label htmlFor="calendar-unmonitored" className="text-xs font-normal">
            {t('calendar.showUnmonitored')}
          </Label>
        </span>
      </div>

      {list.isPending && <Skeleton className="h-64 w-full" />}
      {list.data && (
        <div className={cn('grid gap-px overflow-hidden rounded-lg border bg-border', 'grid-cols-1 sm:grid-cols-7')} data-testid="calendar-grid">
          {days.map((day) => {
            const episodes = byDay.get(dayKey(day)) ?? []
            const outside = view === 'month' && day.getMonth() !== anchor.getMonth()
            return (
              <div key={dayKey(day)} className={cn('flex min-h-24 flex-col gap-1 bg-background p-1.5', view === 'week' && 'sm:min-h-72', outside && 'bg-muted/40')} data-testid={`calendar-day-${day.getDate()}`}>
                <div className={cn('flex items-baseline justify-between px-0.5 text-xs text-muted-foreground', dayKey(day) === today && 'font-semibold text-foreground')}>
                  <span>{weekday.format(day)}</span>
                  <span className={cn('tabular-nums', dayKey(day) === today && 'rounded bg-primary px-1 text-primary-foreground')}>{day.getDate()}</span>
                </div>
                {episodes.map((episode) => (
                  <Link
                    key={`${episode.versionId}-${episode.episodeId}`}
                    to="/series/$seriesId"
                    params={{ seriesId: String(episode.seriesId) }}
                    className={cn('rounded-sm border-l-4 px-1.5 py-1 text-xs leading-tight hover:brightness-95', statusStyles[episode.status])}
                    title={`${episode.seriesTitle} ${code(episode)} – ${episode.title} · ${t(`calendar.status.${episode.status}`)}${episode.quality ? ` · ${episode.quality}` : ''}`}
                    data-testid={`calendar-episode-${episode.versionId}-${episode.episodeId}`}
                  >
                    <span className="flex items-baseline justify-between gap-1">
                      <span className="truncate font-medium">{episode.seriesTitle}</span>
                      <span className="shrink-0 text-muted-foreground tabular-nums">{episode.airDateUtc ? formatTime(episode.airDateUtc) : ''}</span>
                    </span>
                    <span className="flex items-baseline gap-1">
                      {code(episode) && <span className="shrink-0 tabular-nums">{code(episode)}</span>}
                      <span className="truncate text-muted-foreground">{episode.title}</span>
                    </span>
                    {!episode.isDefaultVersion && <span className="block text-[10px] text-muted-foreground">{episode.versionName}</span>}
                  </Link>
                ))}
              </div>
            )
          })}
        </div>
      )}
      <p className="text-xs text-muted-foreground">
        {t('calendar.icalHint')} <code className="break-all">{calendar.icsUrl}?apikey=…</code>
      </p>
    </>
  )
}
