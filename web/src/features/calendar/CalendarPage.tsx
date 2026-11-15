import { useMemo, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { cn } from 'cn'
import { CalendarDaysIcon, ChevronLeftIcon, ChevronRightIcon, Disc3Icon, MonitorPlayIcon, TicketIcon, type LucideIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { auth, calendar, type CalendarEpisode, type CalendarMovie, type ReleaseType, type VersionStatus } from '@/api/client'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Label } from '@/components/ui/label'
import { Skeleton } from '@/components/ui/skeleton'
import { ToneDot } from '@/features/library/VersionChip'
import { formatTime } from '@/i18n'
import { type CalendarEntry, type CalendarView, calendarKinds, entriesByDay, localDay, useCalendarPrefs } from './calendarModel'

/** The library legend's colours as a left bar and a tint: the same green, amber, blue and grey as the chips. */
const toneStyles: Record<VersionStatus, string> = {
  available: 'border-l-emerald-500 bg-emerald-500/10',
  missing: 'border-l-amber-500 bg-amber-500/10',
  downloading: 'border-l-blue-500 bg-blue-500/10',
  pending: 'border-l-zinc-400 bg-muted/70 dark:border-l-zinc-500',
}

const tones: VersionStatus[] = ['pending', 'missing', 'downloading', 'available']

/** An unmonitored entry reads as "left alone", like an unmonitored chip in the library. */
const entryClass = (entry: CalendarEntry) => cn('rounded-sm border-l-4 px-1.5 py-1 text-xs leading-tight hover:brightness-95', toneStyles[entry.tone], !entry.monitored && 'border-dashed opacity-60')

export const releaseIcons: Record<ReleaseType, LucideIcon> = { inCinemas: TicketIcon, digital: MonitorPlayIcon, physical: Disc3Icon }

const releaseTypes: ReleaseType[] = ['inCinemas', 'digital', 'physical']

const dayKey = (date: Date) => localDay(date)

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
function range(view: CalendarView, anchor: Date): Date[] {
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

/**
 * Radarr's and Sonarr's calendars in one: the films' cinema, digital and physical dates and the episodes' air times,
 * this week or this month, per version, in the library legend's colours.
 */
export function CalendarPage() {
  const { t, i18n } = useTranslation()
  const me = useQuery({ queryKey: ['me'], queryFn: auth.me })
  const [prefs, updatePrefs] = useCalendarPrefs(me.data?.id)
  const { kind, view, unmonitored } = prefs
  const [anchor, setAnchor] = useState(() => new Date())
  const days = useMemo(() => range(view, anchor), [view, anchor])
  const start = days[0]
  const end = addDays(days[days.length - 1], 1)
  const episodes = useQuery({
    queryKey: ['calendar', start.toISOString(), end.toISOString(), unmonitored],
    queryFn: () => calendar.list(start, end, unmonitored),
    refetchInterval: 60_000,
    enabled: kind !== 'films',
  })
  const films = useQuery({
    queryKey: ['calendar', 'movies', localDay(start), localDay(end), unmonitored],
    queryFn: () => calendar.movies(localDay(start), localDay(end), unmonitored),
    refetchInterval: 60_000,
    enabled: kind !== 'series',
  })

  const pending = (kind !== 'films' && episodes.isPending) || (kind !== 'series' && films.isPending)
  const byDay = useMemo(
    () => entriesByDay(kind === 'series' ? [] : (films.data ?? []), kind === 'films' ? [] : (episodes.data ?? [])),
    [kind, films.data, episodes.data],
  )

  const title = new Intl.DateTimeFormat(i18n.language, view === 'month' ? { month: 'long', year: 'numeric' } : { day: 'numeric', month: 'long', year: 'numeric' }).format(view === 'month' ? anchor : start)
  const weekday = new Intl.DateTimeFormat(i18n.language, { weekday: 'short' })
  const today = dayKey(new Date())
  const move = (direction: number) => setAnchor((a) => (view === 'week' ? addDays(a, 7 * direction) : new Date(a.getFullYear(), a.getMonth() + direction, 1)))

  return (
    <>
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="text-2xl font-semibold tracking-tight">{t('calendar.title')}</h1>
        <div className="flex w-full flex-wrap items-center gap-2 sm:ml-auto sm:w-auto">
          <Button type="button" variant="outline" size="sm" onClick={() => setAnchor(new Date())} data-testid="calendar-today">
            <CalendarDaysIcon />
            {t('calendar.today')}
          </Button>
          <div className="flex items-center">
            <Button type="button" variant="ghost" size="icon" onClick={() => move(-1)} aria-label={t('common.previous')}>
              <ChevronLeftIcon />
            </Button>
            <span className="min-w-36 text-center text-sm font-medium sm:min-w-40" data-testid="calendar-range">
              {view === 'week' ? t('calendar.weekOf', { date: title }) : title}
            </span>
            <Button type="button" variant="ghost" size="icon" onClick={() => move(1)} aria-label={t('common.next')}>
              <ChevronRightIcon />
            </Button>
          </div>
          <div className="flex rounded-md border p-0.5" role="tablist" aria-label={t('calendar.kind')}>
            {calendarKinds.map((value) => (
              <Button key={value} type="button" role="tab" aria-selected={kind === value} variant={kind === value ? 'secondary' : 'ghost'} size="sm" onClick={() => updatePrefs({ kind: value })} data-testid={`calendar-kind-${value}`}>
                {t(`calendar.kinds.${value}`)}
              </Button>
            ))}
          </div>
          <div className="flex rounded-md border p-0.5" role="tablist" aria-label={t('calendar.view')}>
            {(['week', 'month'] as const).map((value) => (
              <Button key={value} type="button" role="tab" aria-selected={view === value} variant={view === value ? 'secondary' : 'ghost'} size="sm" onClick={() => updatePrefs({ view: value })} data-testid={`calendar-view-${value}`}>
                {t(`calendar.views.${value}`)}
              </Button>
            ))}
          </div>
        </div>
      </div>

      <div className="flex flex-wrap items-center gap-x-4 gap-y-2 text-xs" data-testid="calendar-legend">
        {tones.map((tone) => (
          <span key={tone} className="flex items-center gap-1.5">
            <ToneDot tone={tone} />
            {t(`library.legend.${tone}.label`)}
          </span>
        ))}
        <span className="flex items-center gap-1.5">
          <ToneDot tone="unmonitored" />
          {t('library.legend.unmonitored.label')}
        </span>
        {kind !== 'series' && (
          <span className="flex flex-wrap items-center gap-x-3 gap-y-1 text-muted-foreground">
            {releaseTypes.map((type) => {
              const Icon = releaseIcons[type]
              return (
                <span key={type} className="flex items-center gap-1">
                  <Icon className="size-3.5" aria-hidden="true" />
                  {t(`calendar.release.${type}`)}
                </span>
              )
            })}
          </span>
        )}
        <span className="ml-auto flex items-center gap-2">
          <Checkbox id="calendar-unmonitored" checked={unmonitored} onCheckedChange={(v) => updatePrefs({ unmonitored: v === true })} />
          <Label htmlFor="calendar-unmonitored" className="text-xs font-normal">
            {t('calendar.showUnmonitored')}
          </Label>
        </span>
      </div>

      {pending && <Skeleton className="h-64 w-full" />}
      {!pending && (
        <div className={cn('grid gap-px overflow-hidden rounded-lg border bg-border', 'grid-cols-1 sm:grid-cols-7')} data-testid="calendar-grid">
          {days.map((day) => {
            const key = dayKey(day)
            const entries = byDay.get(key) ?? []
            const outside = view === 'month' && day.getMonth() !== anchor.getMonth()
            return (
              <div
                key={key}
                className={cn(
                  'flex flex-col gap-1 bg-background p-1.5 sm:min-h-24',
                  view === 'week' && 'sm:min-h-72',
                  outside && 'bg-muted/40 max-sm:hidden',
                  // On a phone the days stack: an empty day of the month is only a gap, today always shows.
                  view === 'month' && entries.length === 0 && key !== today && 'max-sm:hidden',
                )}
                data-testid={`calendar-day-${day.getDate()}`}
              >
                <div className={cn('flex items-baseline justify-between px-0.5 text-xs text-muted-foreground', key === today && 'font-semibold text-foreground')}>
                  <span>{weekday.format(day)}</span>
                  <span className={cn('tabular-nums', key === today && 'rounded bg-primary px-1 text-primary-foreground')}>{day.getDate()}</span>
                </div>
                <DayEntries entries={entries} />
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

/** The films first, like all-day events, then the episodes by air time. */
function DayEntries({ entries }: { entries: CalendarEntry[] }) {
  return entries.map((entry) => (entry.kind === 'film' ? <FilmCard key={entry.key} entry={entry} film={entry.film} /> : <EpisodeCard key={entry.key} entry={entry} episode={entry.episode} />))
}

function useFilmTitle(film: CalendarMovie) {
  const { t } = useTranslation()
  return `${film.title}${film.year ? ` (${film.year})` : ''} – ${t(`calendar.release.${film.releaseType}`)} · ${film.versionName} · ${t(`library.legend.${film.status}.label`)}${film.quality ? ` · ${film.quality}` : ''}${film.monitored ? '' : ` · ${t('library.legend.unmonitored.label')}`}`
}

/** The icon and the second line say which date it is, the colour where the version stands. */
function FilmCard({ entry, film }: { entry: CalendarEntry; film: CalendarMovie }) {
  const { t } = useTranslation()
  const Icon = releaseIcons[film.releaseType]
  return (
    <Link
      to="/movies/$movieId"
      params={{ movieId: String(film.movieId) }}
      className={cn(entryClass(entry), 'flex flex-col')}
      title={useFilmTitle(film)}
      data-testid={`calendar-film-${film.versionId}-${film.releaseType}`}
    >
      <span className="flex items-baseline gap-1">
        <Icon className="size-3 shrink-0 translate-y-0.5" aria-hidden="true" />
        <span className="truncate font-medium">{film.title}</span>
      </span>
      <span className="truncate text-muted-foreground">{t(`calendar.release.${film.releaseType}`)}</span>
      {!film.isDefaultVersion && <span className="block text-[10px] text-muted-foreground">{film.versionName}</span>}
    </Link>
  )
}

function EpisodeCard({ entry, episode }: { entry: CalendarEntry; episode: CalendarEpisode }) {
  const { t } = useTranslation()
  return (
    <Link
      to="/series/$seriesId"
      params={{ seriesId: String(episode.seriesId) }}
      className={entryClass(entry)}
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
  )
}
