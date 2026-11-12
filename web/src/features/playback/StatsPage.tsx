import { useState, type ReactNode } from 'react'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { ActivityIcon, ClockIcon, UsersIcon, ZapIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { playback, type PlaybackStats, type TopEntry } from '@/api/client'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { formatDate, formatDateTime } from '@/i18n'
import { DecisionShare } from './DecisionShare'
import { StackedBars, StackedRows, type ChartRow, type SeriesKey } from './StatsCharts'
import { UserPicker } from './UserPicker'

const periods: (number | null)[] = [7, 30, 90, 365, null]

type Split = 'kind' | 'decision'

const splitSeries: Record<Split, SeriesKey[]> = { kind: ['movies', 'episodes'], decision: ['directPlay', 'directStream', 'transcode'] }

/** Monday first, like the API. */
const weekdayFormat = new Intl.DateTimeFormat('en-GB', { weekday: 'short' })
const weekdays = Array.from({ length: 7 }, (_, i) => weekdayFormat.format(new Date(2024, 0, 1 + i)))

/** 1558 h, 2.5 h, 42 min: hours read better than days at this scale. */
function hours(seconds: number) {
  const h = seconds / 3600
  return h >= 100 ? `${Math.round(h)} h` : h >= 1 ? `${h.toFixed(1)} h` : `${Math.round(seconds / 60)} min`
}

/** A bar's label: 09/11 for a day or a week (its Monday), 11/26 for a month. */
function bucketLabel(iso: string, bucket: PlaybackStats['bucket']) {
  const [y, m, d] = iso.split('-')
  return bucket === 'month' ? `${m}/${y.slice(2)}` : `${d}/${m}`
}

/**
 * The plays of the followed servers over a period, for everyone or one person: four headline numbers, then when people
 * watch (Activity), what they watch and how it is served (Library), and who watches on what (People).
 */
export function StatsPage() {
  const { t } = useTranslation()
  const [days, setDays] = useState<number | null>(30)
  const [userId, setUserId] = useState<number | null>(null)
  // Each chart has its own switch: films / episodes, or by decision.
  const [split, setSplit] = useState<Split>('kind')
  const [hourSplit, setHourSplit] = useState<Split>('kind')
  const [weekdaySplit, setWeekdaySplit] = useState<Split>('kind')
  const [usersBy, setUsersBy] = useState<'time' | 'plays'>('time')
  const [platformsBy, setPlatformsBy] = useState<'time' | 'plays' | 'decision'>('time')
  const users = useQuery({ queryKey: ['playback', 'users'], queryFn: playback.users })
  const stats = useQuery({ queryKey: ['playback', 'stats', days, userId], queryFn: () => playback.stats(days, userId), placeholderData: keepPreviousData })
  const s = stats.data

  const labels: Record<SeriesKey, string> = {
    movies: t('playback.kinds.movie'),
    episodes: t('playback.kinds.episode'),
    plays: t('playback.plays'),
    directPlay: t('playback.decisions.directPlay'),
    directStream: t('playback.decisions.directStream'),
    transcode: t('playback.decisions.transcode'),
  }

  return (
    <>
      <div className="flex flex-wrap items-center gap-2">
        <h1 className="mr-auto text-2xl font-semibold tracking-tight">{t('playback.statsTitle')}</h1>
        <UserPicker users={users.data ?? []} value={userId} onChange={setUserId} />
        <Segmented
          value={days}
          onChange={setDays}
          options={periods.map((p) => [p, p === null ? t('playback.allTime') : p === 365 ? t('playback.oneYear') : t('playback.daysCount', { count: p })])}
          testId="stats-period"
        />
      </div>

      {stats.isPending && <Skeleton className="h-96 w-full" />}
      {stats.error && (
        <Alert variant="destructive">
          <AlertDescription>{t('playback.loadError')}</AlertDescription>
        </Alert>
      )}
      {s && (
        <div className="flex flex-col gap-4" data-testid="stats">
          <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
            <Tile icon={<ActivityIcon />} label={t('playback.plays')} value={s.totals.plays.toLocaleString('en-GB')} hint={t('playback.filmsAndEpisodes', { movies: s.totals.movies, episodes: s.totals.episodes })} />
            <Tile icon={<ClockIcon />} label={t('playback.watchTime')} value={hours(s.totals.seconds)} />
            <Tile icon={<UsersIcon />} label={t('playback.activeUsers')} value={String(s.totals.users)} />
            <Tile icon={<ZapIcon />} label={t('playback.mostStreams')} value={String(s.concurrency.streams)} hint={s.concurrency.at ? formatDateTime(s.concurrency.at) : undefined} />
          </div>

          <SectionHeading title={t('playback.sections.activity')} hint={t('playback.sections.activityHint')} />
          <div className="grid gap-4 lg:grid-cols-[minmax(0,2fr)_minmax(0,1fr)]">
            <Section
              title={t(`playback.playsPer.${s.bucket}`)}
              action={<SplitSwitch value={split} onChange={setSplit} testId="stats-split" />}
            >
              <StackedBars rows={s.overTime.map((d) => ({ ...d, label: bucketLabel(d.date, s.bucket) }) as ChartRow)} series={splitSeries[split]} labels={labels} height={300} />
            </Section>
            <div className="flex flex-col gap-4">
              <Section title={t('playback.byHour')} action={<SplitSwitch value={hourSplit} onChange={setHourSplit} testId="stats-hour-split" />}>
                <StackedBars rows={s.byHour.map((h, hour) => ({ ...h, label: String(hour).padStart(2, '0') }) as ChartRow)} series={splitSeries[hourSplit]} labels={labels} height={140} />
              </Section>
              <Section title={t('playback.byWeekday')} action={<SplitSwitch value={weekdaySplit} onChange={setWeekdaySplit} testId="stats-weekday-split" />}>
                <StackedBars rows={s.byWeekday.map((d, i) => ({ ...d, label: weekdays[i] }) as ChartRow)} series={splitSeries[weekdaySplit]} labels={labels} height={140} />
              </Section>
            </div>
          </div>

          <SectionHeading title={t('playback.sections.library')} hint={t('playback.sections.libraryHint')} />
          <div className="grid gap-4 lg:grid-cols-2">
            <Section title={t('playback.topMovies')}>
              <RankList entries={s.topMovies.slice(0, 6)} link={movieLink} sub={(e) => t('playback.usersCount', { count: e.users })} />
            </Section>
            <Section title={t('playback.topShows')}>
              <RankList entries={s.topShows.slice(0, 6)} link={showLink} sub={(e) => t('playback.usersCount', { count: e.users })} />
            </Section>
          </div>
          <Section title={t('playback.howServed')}>
            <div className="grid gap-4 lg:grid-cols-[minmax(0,1fr)_minmax(0,2fr)]">
              <div className="flex flex-col gap-3">
                <DecisionShare decisions={s.decisions} />
                <p className="text-xs text-muted-foreground">{t('playback.howServedHint')}</p>
              </div>
              <TranscodedUhd stats={s} />
            </div>
          </Section>

          <SectionHeading title={t('playback.sections.people')} hint={t('playback.sections.peopleHint')} />
          <div className="grid gap-4 lg:grid-cols-2">
            <Section
              title={t('playback.topUsers')}
              action={
                <Segmented
                  value={usersBy}
                  onChange={setUsersBy}
                  options={[
                    ['time', t('playback.byTime')],
                    ['plays', t('playback.byPlays')],
                  ]}
                  testId="stats-users-by"
                />
              }
            >
              {usersBy === 'time' ? (
                <RankList entries={s.topUsersByTime.slice(0, 6)} link={userLink} value={(e) => e.seconds} format={hours} sub={(e) => t('playback.playsCount', { count: e.plays })} />
              ) : (
                <RankList entries={s.topUsers.slice(0, 6)} link={userLink} sub={(e) => hours(e.seconds)} />
              )}
            </Section>
            <Section
              title={t('playback.topPlatforms')}
              action={
                <Segmented
                  value={platformsBy}
                  onChange={setPlatformsBy}
                  options={[
                    ['time', t('playback.byTime')],
                    ['plays', t('playback.byPlays')],
                    ['decision', t('playback.split.decision')],
                  ]}
                  testId="stats-platforms-by"
                />
              }
            >
              {platformsBy === 'time' ? (
                <RankList entries={s.topPlatformsByTime.slice(0, 6)} value={(e) => e.seconds} format={hours} sub={(e) => t('playback.playsCount', { count: e.plays })} />
              ) : platformsBy === 'plays' ? (
                <RankList entries={s.topPlatforms.slice(0, 6)} sub={(e) => hours(e.seconds)} />
              ) : s.topPlatforms.length === 0 ? (
                <p className="text-sm text-muted-foreground">{t('playback.noPlays')}</p>
              ) : (
                // The platforms with the most plays, each bar split like the charts' decision view.
                <StackedRows rows={s.topPlatforms.slice(0, 6).map((e) => ({ label: e.title || t('common.none'), directPlay: e.directPlay, directStream: e.directStream, transcode: e.transcode }))} series={splitSeries.decision} labels={labels} />
              )}
            </Section>
          </div>
        </div>
      )}
    </>
  )
}

/** Films / episodes, or by decision: the same switch and colours on every chart of plays. */
function SplitSwitch({ value, onChange, testId }: { value: Split; onChange: (value: Split) => void; testId: string }) {
  const { t } = useTranslation()
  return (
    <Segmented
      value={value}
      onChange={onChange}
      options={[
        ['kind', t('playback.split.kind')],
        ['decision', t('playback.split.decision')],
      ]}
      testId={testId}
    />
  )
}

function Segmented<T extends string | number | null>({ value, options, onChange, testId }: { value: T; options: [T, string][]; onChange: (value: T) => void; testId?: string }) {
  return (
    <div className="flex rounded-md border p-0.5" role="tablist" data-testid={testId}>
      {options.map(([option, label]) => (
        <Button key={label} type="button" role="tab" aria-selected={value === option} size="xs" variant={value === option ? 'secondary' : 'ghost'} className="h-7 px-2.5" onClick={() => onChange(option)}>
          {label}
        </Button>
      ))}
    </div>
  )
}

function Tile({ icon, label, value, hint }: { icon: ReactNode; label: string; value: string; hint?: string }) {
  return (
    <Card className="gap-1 py-4">
      <CardContent className="flex flex-col gap-1 px-4">
        <span className="flex items-center gap-1.5 text-xs text-muted-foreground [&_svg]:size-3.5">
          {icon}
          {label}
        </span>
        <span className="text-2xl font-semibold tracking-tight tabular-nums">{value}</span>
        {hint && <span className="text-xs text-muted-foreground tabular-nums">{hint}</span>}
      </CardContent>
    </Card>
  )
}

function SectionHeading({ title, hint }: { title: string; hint: string }) {
  return (
    <div className="mt-2 flex items-baseline gap-2 border-b pb-1.5">
      <h2 className="text-lg font-semibold tracking-tight">{title}</h2>
      <span className="text-sm text-muted-foreground">{hint}</span>
    </div>
  )
}

function Section({ title, action, children }: { title: string; action?: ReactNode; children: ReactNode }) {
  return (
    <Card className="gap-3 py-4">
      <CardHeader className="flex flex-row flex-wrap items-center justify-between gap-2 px-4">
        <CardTitle className="text-sm font-medium">{title}</CardTitle>
        {action}
      </CardHeader>
      <CardContent className="px-4">{children}</CardContent>
    </Card>
  )
}

const movieLink = (e: TopEntry) =>
  e.movieId ? (
    <Link to="/movies/$movieId" params={{ movieId: String(e.movieId) }} className="underline-offset-4 hover:underline">
      {e.title}
    </Link>
  ) : (
    e.title
  )

/** A show Bigarr follows opens its series page; any other one, its plays. */
const showLink = (e: TopEntry) =>
  e.seriesId ? (
    <Link to="/series/$seriesId" params={{ seriesId: String(e.seriesId) }} className="underline-offset-4 hover:underline" data-testid="top-show-series">
      {e.title}
    </Link>
  ) : (
    <Link to="/playback/plays" search={{ kind: 'episode', q: e.title }} className="underline-offset-4 hover:underline" data-testid="top-show-plays">
      {e.title}
    </Link>
  )

const userLink = (e: TopEntry) =>
  e.userId ? (
    <Link to="/playback/users/$userId" params={{ userId: String(e.userId) }} className="underline-offset-4 hover:underline">
      {e.title}
    </Link>
  ) : (
    e.title
  )

/** A ranked list, a bar behind each row for its share of the first. */
function RankList({ entries, link, value = (e) => e.plays, format = (n) => String(n), sub }: { entries: TopEntry[]; link?: (e: TopEntry) => ReactNode; value?: (e: TopEntry) => number; format?: (n: number) => string; sub?: (e: TopEntry) => string }) {
  const { t } = useTranslation()
  if (entries.length === 0) return <p className="text-sm text-muted-foreground">{t('playback.noPlays')}</p>
  const max = Math.max(1, ...entries.map(value))
  return (
    <ol className="flex flex-col gap-1.5">
      {entries.map((e, i) => (
        <li key={`${e.movieId ?? ''}:${e.userId ?? ''}:${e.title}`} className="relative flex items-center gap-2 overflow-hidden rounded-md px-2 py-1 text-sm">
          <span className="absolute inset-y-0 left-0 rounded-md bg-muted" style={{ width: `${(value(e) / max) * 100}%` }} aria-hidden="true" />
          <span className="relative w-4 text-xs text-muted-foreground tabular-nums">{i + 1}</span>
          <span className="relative min-w-0 flex-1 truncate">{link ? link(e) : e.title}</span>
          {sub && <span className="relative text-xs text-muted-foreground tabular-nums">{sub(e)}</span>}
          <span className="relative font-medium tabular-nums">{format(value(e))}</span>
        </li>
      ))}
    </ol>
  )
}

/** The 4K files that went through a transcode: the players could not take them, a 1080p copy may be enough. */
function TranscodedUhd({ stats }: { stats: PlaybackStats }) {
  const { t } = useTranslation()
  if (stats.transcodedUhd.length === 0) return <p className="text-sm text-muted-foreground">{t('playback.noUhdTranscode')}</p>
  return (
    <Table data-testid="stats-uhd">
      <TableHeader>
        <TableRow>
          <TableHead>{t('playback.uhdTranscoded')}</TableHead>
          <TableHead>{t('playback.version')}</TableHead>
          <TableHead className="text-right">{t('playback.plays')}</TableHead>
          <TableHead className="text-right">{t('playback.transcodes')}</TableHead>
          <TableHead>{t('playback.lastPlayed')}</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {stats.transcodedUhd.slice(0, 6).map((v) => (
          <TableRow key={`${v.movieId}\n${v.versionName}`}>
            <TableCell>
              <Link to="/movies/$movieId" params={{ movieId: String(v.movieId) }} className="font-medium underline-offset-4 hover:underline">
                {v.year ? `${v.title} (${v.year})` : v.title}
              </Link>
            </TableCell>
            <TableCell>{v.versionName && <Badge variant="secondary">{v.versionName}</Badge>}</TableCell>
            <TableCell className="text-right tabular-nums">{v.plays}</TableCell>
            <TableCell className="text-right tabular-nums">
              {v.transcodes} <span className="text-muted-foreground">({Math.round((v.transcodes / v.plays) * 100)} %)</span>
            </TableCell>
            <TableCell className="text-sm text-muted-foreground">{formatDate(v.lastPlayedAt)}</TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  )
}
