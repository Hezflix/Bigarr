import { useMemo, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { cn } from 'cn'
import { CalendarDaysIcon, ChevronLeftIcon, ChevronRightIcon, Disc3Icon, MonitorPlayIcon, TicketIcon, type LucideIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { auth, calendar, type CalendarEpisode, type CalendarMovie, type ReleaseType } from '@/api/client'
import { episodeCode } from '@/lib/episodeCode'
import { Button } from '@/components/ui/button'
import { Poster } from '@/components/Poster'
import { Checkbox } from '@/components/ui/checkbox'
import { Label } from '@/components/ui/label'
import { Skeleton } from '@/components/ui/skeleton'
import { type ChipTone, ToneDot } from '@/features/library/VersionChip'
import { formatTime } from '@/i18n'
import { CalendarSubscription } from './CalendarSubscription'
import { type CalendarDensity, type CalendarEntry, type CalendarView, calendarDensities, calendarKinds, entriesByDay, filmTone, localDay, useCalendarPrefs } from './calendarModel'

/** The library legend's colours as a left bar and a tint: the same green, violet, amber, blue and grey as the chips. */
const toneStyles: Record<ChipTone, string> = {
  available: 'border-l-emerald-500 bg-emerald-500/10',
  cutoffUnmet: 'border-l-violet-500 bg-violet-500/10',
  missing: 'border-l-amber-500 bg-amber-500/10',
  downloading: 'border-l-blue-500 bg-blue-500/10',
  pending: 'border-l-zinc-400 bg-muted/70 dark:border-l-zinc-500',
}

const tones: ChipTone[] = ['available', 'cutoffUnmet', 'missing', 'downloading', 'pending']

/** An unmonitored entry reads as "left alone", like an unmonitored chip in the library. */
const entryClass = (entry: CalendarEntry) =>
  cn('flex min-w-0 items-start gap-1.5 rounded-sm border-l-4 px-1.5 py-1 text-xs leading-tight hover:brightness-95', toneStyles[entry.tone], !entry.monitored && 'border-dashed opacity-60')

/**
 * Comfortable entries carry the poster and their titles wrap onto two lines, between words: beside a poster, a seventh
 * of the page is too narrow for one. Compact ones are text only and truncate.
 */
type Look = { density: CalendarDensity }

const titleClass = (look: Look) => (look.density === 'comfortable' ? 'line-clamp-2 min-w-0' : 'truncate')

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
  episode.seriesType === 'daily' ? '' : episodeCode(episode.seasonNumber, episode.episodeNumber)

/**
 * Radarr's and Sonarr's calendars in one: the films' cinema, digital and physical dates and the episodes' air times,
 * this week or this month, per version, in the library legend's colours.
 */
export function CalendarPage() {
  const { t, i18n } = useTranslation()
  const me = useQuery({ queryKey: ['me'], queryFn: auth.me })
  const [prefs, updatePrefs] = useCalendarPrefs(me.data?.id)
  const { kind, view, density, unmonitored } = prefs
  const look: Look = { density }
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
          <div className="flex rounded-md border p-0.5" role="tablist" aria-label={t('library.view.density')}>
            {calendarDensities.map((value) => (
              <Button key={value} type="button" role="tab" aria-selected={density === value} variant={density === value ? 'secondary' : 'ghost'} size="sm" onClick={() => updatePrefs({ density: value })} data-testid={`calendar-density-${value}`}>
                {t(`library.view.densities.${value}`)}
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
                <DayEntries entries={entries} look={look} />
              </div>
            )
          })}
        </div>
      )}
      <CalendarSubscription unmonitored={unmonitored} />
    </>
  )
}

/** The films first, like all-day events, then the episodes by air time. */
function DayEntries({ entries, look }: { entries: CalendarEntry[]; look: Look }) {
  return entries.map((entry) =>
    entry.kind === 'film' ? <FilmCard key={entry.key} entry={entry} film={entry.film} look={look} /> : <EpisodeCard key={entry.key} entry={entry} episode={entry.episode} look={look} />,
  )
}

function useFilmTitle(film: CalendarMovie) {
  const { t } = useTranslation()
  return `${film.title}${film.year ? ` (${film.year})` : ''} – ${t(`calendar.release.${film.releaseType}`)} · ${film.versionName} · ${t(`library.legend.${filmTone(film)}.label`)}${film.quality ? ` · ${film.quality}` : ''}${film.monitored ? '' : ` · ${t('library.legend.unmonitored.label')}`}`
}

/** The icon and the second line say which date it is, the colour where the version stands. */
function FilmCard({ entry, film, look }: { entry: CalendarEntry; film: CalendarMovie; look: Look }) {
  const { t } = useTranslation()
  const Icon = releaseIcons[film.releaseType]
  return (
    <Link
      to="/movies/$movieId"
      params={{ movieId: String(film.movieId) }}
      className={entryClass(entry)}
      title={useFilmTitle(film)}
      data-testid={`calendar-film-${film.versionId}-${film.releaseType}`}
    >
      {look.density === 'comfortable' && <Poster url={film.posterUrl ?? null} title={film.title} size="calendar" className="rounded-sm" />}
      <span className="flex min-w-0 flex-col">
        <span className="flex items-baseline gap-1">
          <Icon className="size-3 shrink-0 translate-y-0.5" aria-hidden="true" />
          <span className={cn(titleClass(look), 'font-medium')}>{film.title}</span>
        </span>
        <span className="truncate text-muted-foreground">{t(`calendar.release.${film.releaseType}`)}</span>
        {!film.isDefaultVersion && <span className="block text-[10px] text-muted-foreground">{film.versionName}</span>}
      </span>
    </Link>
  )
}

function EpisodeCard({ entry, episode, look }: { entry: CalendarEntry; episode: CalendarEpisode; look: Look }) {
  const { t } = useTranslation()
  const time = episode.airDateUtc ? formatTime(episode.airDateUtc) : ''
  return (
    <Link
      to="/series/$seriesId"
      params={{ seriesId: String(episode.seriesId) }}
      className={entryClass(entry)}
      title={`${episode.seriesTitle} ${code(episode)} – ${episode.title} · ${t(`calendar.status.${episode.status}`)}${entry.tone === 'cutoffUnmet' ? ` · ${t('library.legend.cutoffUnmet.label')}` : ''}${episode.quality ? ` · ${episode.quality}` : ''}`}
      data-testid={`calendar-episode-${episode.versionId}-${episode.episodeId}`}
    >
      {look.density === 'comfortable' && <Poster url={episode.posterUrl ?? null} title={episode.seriesTitle} size="calendar" className="rounded-sm" />}
      <span className="flex min-w-0 flex-1 flex-col">
        {/* Beside a poster the time goes under the episode, as the title needs the whole width. */}
        <span className="flex items-baseline justify-between gap-1">
          <span className={cn(titleClass(look), 'font-medium')}>{episode.seriesTitle}</span>
          {look.density === 'compact' && time && <span className="shrink-0 text-muted-foreground tabular-nums">{time}</span>}
        </span>
        <span className="flex items-baseline gap-1">
          {code(episode) && <span className="shrink-0 tabular-nums">{code(episode)}</span>}
          <span className="truncate text-muted-foreground">{episode.title}</span>
        </span>
        {look.density === 'comfortable' && time && <span className="text-muted-foreground tabular-nums">{time}</span>}
        {!episode.isDefaultVersion && <span className="block text-[10px] text-muted-foreground">{episode.versionName}</span>}
      </span>
    </Link>
  )
}
