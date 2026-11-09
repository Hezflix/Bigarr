import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { AudioLinesIcon, CaptionsIcon, ChevronDownIcon, FilmIcon, GaugeIcon, LayoutGridIcon, ListIcon, LoaderIcon, PackageIcon, PauseIcon, PlayIcon } from 'lucide-react'
import { cn } from 'cn'
import { useTranslation } from 'react-i18next'
import { playback, type PlaybackSession, type PlaybackServerStatus, type PlexTranscode } from '@/api/client'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Progress } from '@/components/ui/progress'
import { Skeleton } from '@/components/ui/skeleton'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { formatDateTime, formatTime } from '@/i18n'
import { DecisionBadge, formatCodec, formatOffset, itemLabel } from './format'
import { StreamTracks } from './StreamTracks'

type View = 'cards' | 'rows'

const viewKey = 'bigarr.playback.view'

function storedView(): View {
  try {
    return localStorage.getItem(viewKey) === 'rows' ? 'rows' : 'cards'
  } catch {
    return 'cards'
  }
}

/** 0: fine, 1: a transcode that keeps up, 2: a transcode slower than real time (the player will buffer). */
export function severity(session: PlaybackSession): 0 | 1 | 2 {
  const t = session.transcode
  if (t && t.speed !== null && t.speed < 1 && !t.throttled && !t.complete) return 2
  return session.details.decision === 'transcode' ? 1 : 0
}

const stripes = ['bg-emerald-500', 'bg-amber-500', 'bg-red-500'] as const
const severityKeys = ['fine', 'transcoding', 'slow'] as const

const remainingMs = (s: PlaybackSession) => Math.max(0, (s.item.durationMs ?? 0) - s.viewOffsetMs)
const progressOf = (s: PlaybackSession) => (s.item.durationMs ? Math.min(100, (s.viewOffsetMs / s.item.durationMs) * 100) : 0)
/** TMDB serves smaller files under the same path: a blurred backdrop needs no more than w300. */
const tmdbSize = (url: string, width: number) => url.replace('/original/', `/w${width}/`)

/**
 * The streams playing now on the followed Plex servers, refreshed every 10 s. Cards (poster, tracks, transcoder) or
 * one row per stream for busy evenings; the choice is remembered in this browser.
 */
export function NowPlayingPage() {
  const { t } = useTranslation()
  const query = useQuery({ queryKey: ['playback', 'now'], queryFn: playback.nowPlaying, refetchInterval: 10_000 })
  const data = query.data
  const [view, setView] = useState<View>(storedView)
  const choose = (next: View) => {
    setView(next)
    try {
      localStorage.setItem(viewKey, next)
    } catch {
      /* private window: the choice lasts for this visit */
    }
  }

  return (
    <>
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="text-2xl font-semibold tracking-tight">{t('playback.nowTitle')}</h1>
        {data && data.sessions.length > 0 && <Badge variant="secondary">{t('playback.streams', { count: data.sessions.length })}</Badge>}
        <div className="ml-auto flex rounded-md border p-0.5" role="group" aria-label={t('playback.view')}>
          <Button size="sm" variant={view === 'cards' ? 'secondary' : 'ghost'} className="h-7 gap-1.5" aria-pressed={view === 'cards'} onClick={() => choose('cards')} data-testid="view-cards">
            <LayoutGridIcon className="size-3.5" />
            {t('playback.viewCards')}
          </Button>
          <Button size="sm" variant={view === 'rows' ? 'secondary' : 'ghost'} className="h-7 gap-1.5" aria-pressed={view === 'rows'} onClick={() => choose('rows')} data-testid="view-rows">
            <ListIcon className="size-3.5" />
            {t('playback.viewRows')}
          </Button>
        </div>
      </div>

      {query.isPending && <Skeleton className="h-40 w-full" />}
      {query.error && (
        <Alert variant="destructive">
          <AlertDescription>{t('playback.loadError')}</AlertDescription>
        </Alert>
      )}
      {data && data.servers.length === 0 && (
        <p className="text-sm text-muted-foreground">
          {t('playback.noServer')}{' '}
          <Link to="/settings/mediaservers" className="underline underline-offset-4">
            {t('nav.settingsMediaServers')}
          </Link>
        </p>
      )}
      {data?.servers.map((server) => <ServerLine key={server.id} server={server} />)}
      {data && data.servers.length > 0 && data.sessions.length === 0 && <p className="text-sm text-muted-foreground">{t('playback.nothingPlaying')}</p>}
      {data && data.sessions.length > 0 && view === 'cards' && (
        <div className="grid grid-cols-[repeat(auto-fill,minmax(min(100%,30rem),1fr))] gap-4" data-testid="now-playing">
          {data.sessions.map((session) => (
            <SessionCard key={session.id} session={session} />
          ))}
        </div>
      )}
      {data && data.sessions.length > 0 && view === 'rows' && <SessionRows sessions={data.sessions} />}
    </>
  )
}

function ServerLine({ server }: { server: PlaybackServerStatus }) {
  const { t } = useTranslation()
  if (server.connected) return null
  return (
    <Alert variant={server.error ? 'destructive' : 'default'}>
      <AlertDescription>{server.error ? t('playback.serverError', { name: server.name, error: server.error }) : t('playback.serverConnecting', { name: server.name })}</AlertDescription>
    </Alert>
  )
}

function StateIcon({ session, className }: { session: PlaybackSession; className?: string }) {
  const { t } = useTranslation()
  const Icon = session.state === 'paused' ? PauseIcon : session.state === 'buffering' ? LoaderIcon : PlayIcon
  return <Icon className={cn('shrink-0 text-muted-foreground', className)} aria-label={t(`playback.states.${session.state}`)} />
}

function Title({ session }: { session: PlaybackSession }) {
  return session.item.movieId ? (
    <Link to="/movies/$movieId" params={{ movieId: String(session.item.movieId) }} className="underline-offset-4 hover:underline">
      {itemLabel(session.item)}
    </Link>
  ) : (
    <span>{itemLabel(session.item)}</span>
  )
}

function PosterImage({ session, width, className }: { session: PlaybackSession; width: number; className: string }) {
  return session.posterUrl ? (
    <img src={tmdbSize(session.posterUrl, width)} alt="" className={cn('aspect-[2/3] shrink-0 rounded-md object-cover shadow-sm', className)} />
  ) : (
    <div className={cn('flex aspect-[2/3] shrink-0 items-center justify-center rounded-md bg-muted text-xs font-semibold text-muted-foreground', className)} aria-hidden="true">
      {session.item.title.slice(0, 2).toUpperCase()}
    </div>
  )
}

/** The stripe on the left edge: the one colour that says whether a stream is in trouble. */
function Stripe({ session }: { session: PlaybackSession }) {
  const { t } = useTranslation()
  const level = severity(session)
  return <div className={cn('absolute inset-y-0 left-0 z-10 w-1.5', stripes[level])} title={t(`playback.severity.${severityKeys[level]}`)} data-severity={severityKeys[level]} />
}

/** "Started 09/11/2026, 21:05 · ends 23:51" on hover of the position or the time left. */
function Times({ session, children, className }: { session: PlaybackSession; children: React.ReactNode; className?: string }) {
  const { t } = useTranslation()
  const ends = session.state !== 'paused' && session.item.durationMs ? formatTime(Date.now() + remainingMs(session)) : null
  return (
    <Tooltip>
      <TooltipTrigger asChild>
        <span className={cn('cursor-default', className)}>{children}</span>
      </TooltipTrigger>
      <TooltipContent>
        {t('playback.startedAt', { time: formatDateTime(session.startedAt) })}
        {ends ? ` · ${t('playback.ends', { time: ends })}` : session.state === 'paused' ? ` · ${t('playback.states.paused')}` : ''}
      </TooltipContent>
    </Tooltip>
  )
}

const channelsOf = (count: number | null) => (count === null ? null : count === 6 ? '5.1' : count === 8 ? '7.1' : count === 2 ? '2.0' : `${count} ch`)
const join = (...parts: (string | number | null | undefined)[]) => parts.filter((p) => p !== null && p !== undefined && p !== '').join(' ')
const converted = (decision: string | null) => ['transcode', 'burn'].includes(decision?.toLowerCase() ?? '')
const amber = 'font-medium text-amber-700 dark:text-amber-300'

/** One cell of the facts grid: an icon, then the value, amber when the track is converted. Cut short, whole on hover. */
function Fact({ icon: Icon, label, text, conversion = false, wide = false }: { icon: typeof FilmIcon; label: string; text: string; conversion?: boolean; wide?: boolean }) {
  return (
    <div className={cn('flex min-w-0 items-center gap-2 text-xs', wide && 'col-span-2')} title={`${label}: ${text}`}>
      <Icon className="size-3.5 shrink-0 text-muted-foreground" aria-label={label} />
      <span className={cn('truncate', conversion && amber)}>{text}</span>
    </div>
  )
}

/**
 * What the file is and what the player gets: video | audio, container | subtitles, then the transcoder across.
 * Amber says "converted", so a cell shows only formats: source, and "→ stream" when it changes.
 */
function FactsGrid({ session }: { session: PlaybackSession }) {
  const { t } = useTranslation()
  const d = session.details
  const tr = session.transcode
  const videoConverted = converted(d.videoDecision)
  const audioConverted = converted(d.audioDecision)
  const video =
    join(formatCodec(d.sourceVideoCodec), formatCodec(d.sourceVideoResolution), d.sourceDynamicRange) +
    (videoConverted ? ` → ${join(formatCodec(d.streamVideoCodec), formatCodec(d.streamVideoResolution))}${d.hardwareTranscode ? ` (${t('playback.hw')})` : ''}` : '')
  const audio = join(formatCodec(d.sourceAudioCodec), channelsOf(d.sourceAudioChannels)) + (audioConverted ? ` → ${join(formatCodec(d.streamAudioCodec), channelsOf(d.streamAudioChannels))}` : '')
  const container = d.streamContainer && d.sourceContainer && d.streamContainer !== d.sourceContainer ? `${d.sourceContainer.toUpperCase()} → ${d.streamContainer.toUpperCase()}` : (d.sourceContainer ?? d.streamContainer ?? '').toUpperCase()
  const behind = severity(session) === 2
  const transcoder = tr
    ? [
        tr.speed !== null ? t('playback.speed', { value: tr.speed.toFixed(1) }) : null,
        tr.complete ? t('playback.transcodeComplete') : tr.throttled ? t('playback.throttled') : null,
        tr.bufferedMs !== null && !tr.complete ? t('playback.ahead', { time: formatOffset(tr.bufferedMs) }) : null,
        behind ? t('playback.cannotKeepUp') : null,
        tr.hardwareEncoding ?? tr.hardwareDecoding,
      ]
        .filter(Boolean)
        .join(' · ')
    : null

  return (
    <div className="grid grid-cols-[minmax(0,3fr)_minmax(0,2fr)] gap-x-4 gap-y-1.5" data-testid="facts">
      <Fact icon={FilmIcon} label={t('playback.video')} text={video || t('common.none')} conversion={videoConverted} />
      <Fact icon={AudioLinesIcon} label={t('playback.audio')} text={audio || t('common.none')} conversion={audioConverted} />
      <Fact icon={PackageIcon} label={t('playback.container')} text={container || t('common.none')} />
      <Fact
        icon={CaptionsIcon}
        label={t('playback.subtitles')}
        text={d.subtitleDecision ? t(`playback.trackDecisions.${d.subtitleDecision.toLowerCase()}`, { defaultValue: d.subtitleDecision }) : t('playback.noSubtitles')}
        conversion={converted(d.subtitleDecision)}
      />
      {transcoder && <Fact icon={GaugeIcon} label={t('playback.transcoder')} text={transcoder} conversion={behind} wide />}
    </div>
  )
}

/**
 * A stream as a tile: the film's backdrop as a banner with the poster, the title, the user, the player and its
 * platform, the decision and the version on it; the facts grid under it; the progress pinned to the bottom so it lines
 * up across a row whatever the number of facts.
 */
function SessionCard({ session }: { session: PlaybackSession }) {
  const { t } = useTranslation()
  const d = session.details
  const player = [d.playerTitle ?? d.playerProduct, d.playerPlatform].filter(Boolean).join(' · ')

  return (
    <Card className="relative h-full gap-0 overflow-hidden p-0" data-session={session.id} data-severity={severityKeys[severity(session)]}>
      <div className="relative isolate h-28">
        {session.fanartUrl ? (
          <img src={tmdbSize(session.fanartUrl, 780)} alt="" aria-hidden="true" className="absolute inset-0 -z-10 size-full object-cover" />
        ) : (
          <div className="absolute inset-0 -z-10 bg-muted" />
        )}
        <div className="absolute inset-0 -z-10 bg-gradient-to-t from-card via-card/80 to-card/20" />
        <div className="absolute inset-x-0 bottom-0 flex items-end gap-3 px-5 pb-2">
          <PosterImage session={session} width={185} className="w-16 shadow-lg" />
          <div className="min-w-0 flex-1 pb-1">
            <div className="flex items-center gap-1.5 text-lg font-semibold">
              <StateIcon session={session} className="size-4" />
              <span className="truncate">
                <Title session={session} />
              </span>
            </div>
            <div className="flex min-w-0 items-center gap-1.5 text-sm text-muted-foreground">
              <Avatar name={session.user.name} url={session.user.thumbUrl} className="size-4 text-[9px]" />
              <span className="truncate" title={player}>
                {session.user.name}
                {player && ` · ${player}`}
              </span>
            </div>
          </div>
          <div className="flex shrink-0 items-center gap-1.5 pb-1">
            <DecisionBadge decision={d.decision} />
            {session.versionName && <Badge variant="secondary">{session.versionName}</Badge>}
          </div>
        </div>
      </div>
      <div className="flex flex-1 flex-col gap-3 px-5 pt-3 pb-4">
        <FactsGrid session={session} />
        {/* Pinned to the bottom: the bar sits at the same height on every tile of a row. */}
        <div className="mt-auto flex flex-col gap-1">
          <Progress value={progressOf(session)} aria-label={t('playback.progress')} />
          <div className="flex flex-wrap justify-between gap-x-3 text-xs tabular-nums text-muted-foreground">
            <Times session={session}>
              {formatOffset(session.viewOffsetMs)} / {session.item.durationMs ? formatOffset(session.item.durationMs) : '?'}
              {session.state !== 'paused' && session.item.durationMs ? ` · ${t('playback.ends', { time: formatTime(Date.now() + remainingMs(session)) })}` : ''}
            </Times>
            {/* LAN or WAN shows in the address. */}
            <span>{[d.bandwidth !== null ? t('playback.mbps', { value: (d.bandwidth / 1000).toFixed(1) }) : null, d.ipAddress].filter(Boolean).join(' · ')}</span>
          </div>
        </div>
      </div>
    </Card>
  )
}

/** The Plex account picture, or the name's initial. */
function Avatar({ name, url, className }: { name: string; url: string | null; className?: string }) {
  const [broken, setBroken] = useState(false)
  return url && !broken ? (
    <img src={url} alt="" className={cn('size-8 shrink-0 rounded-full object-cover', className)} onError={() => setBroken(true)} />
  ) : (
    <span className={cn('flex size-8 shrink-0 items-center justify-center rounded-full bg-muted text-xs font-semibold text-muted-foreground', className)} aria-hidden="true">
      {name.slice(0, 1).toUpperCase()}
    </span>
  )
}

/** One line per stream, for evenings with many; a click opens the same details as a card under the row. */
function SessionRows({ sessions }: { sessions: PlaybackSession[] }) {
  const { t } = useTranslation()
  const [open, setOpen] = useState<number | null>(null)

  return (
    <div className="divide-y overflow-hidden rounded-lg border" data-testid="now-playing-rows">
      {sessions.map((session) => {
        const d = session.details
        const expanded = open === session.id
        return (
          <div key={session.id} className="relative" data-session={session.id}>
            <Stripe session={session} />
            <div
              role="button"
              tabIndex={0}
              onClick={(e) => !(e.target as HTMLElement).closest('a') && setOpen(expanded ? null : session.id)}
              onKeyDown={(e) => {
                if ((e.key === 'Enter' || e.key === ' ') && e.target === e.currentTarget) {
                  e.preventDefault()
                  setOpen(expanded ? null : session.id)
                }
              }}
              aria-expanded={expanded}
              className="flex w-full cursor-pointer items-center gap-3 py-2 pr-3 pl-5 text-left text-sm hover:bg-muted/50"
            >
              <PosterImage session={session} width={92} className="w-7 rounded-sm" />
              <StateIcon session={session} className="size-3.5" />
              <span className="w-24 shrink-0 truncate">{session.user.name}</span>
              <span className="min-w-0 flex-1 truncate font-medium">
                <Title session={session} />
              </span>
              {session.versionName && <Badge variant="secondary">{session.versionName}</Badge>}
              <span className="hidden w-28 sm:block">
                <DecisionBadge decision={d.decision} />
              </span>
              <span className="hidden w-36 lg:block">
                <Progress value={progressOf(session)} className="h-1.5" aria-label={t('playback.progress')} />
              </span>
              <Times session={session} className="hidden w-24 text-right text-xs tabular-nums text-muted-foreground md:block">
                {t('playback.left', { time: formatOffset(remainingMs(session)) })}
              </Times>
              <span className="hidden w-20 text-right text-xs tabular-nums text-muted-foreground md:block">{d.bandwidth !== null && t('playback.mbps', { value: (d.bandwidth / 1000).toFixed(1) })}</span>
              <ChevronDownIcon className={cn('size-4 shrink-0 text-muted-foreground transition-transform', expanded && 'rotate-180')} />
            </div>
            {expanded && (
              <div className="grid gap-4 bg-muted/30 py-3 pr-4 pl-14 md:grid-cols-2">
                <div className="flex flex-col gap-2">
                  <StreamTracks details={d} />
                  {session.transcode && <TranscodeLine transcode={session.transcode} />}
                </div>
                <dl className="grid grid-cols-[auto_1fr] content-start gap-x-3 gap-y-1 text-xs">
                  <dt className="text-muted-foreground">{t('playback.player')}</dt>
                  <dd>{[d.playerTitle, d.playerProduct, d.playerPlatform].filter(Boolean).join(' · ') || t('common.none')}</dd>
                  {d.ipAddress && (
                    <>
                      <dt className="text-muted-foreground">{t('playback.ip')}</dt>
                      <dd className="tabular-nums">
                        {d.ipAddress} ({d.local ? t('playback.lan') : t('playback.wan')})
                      </dd>
                    </>
                  )}
                  <dt className="text-muted-foreground">{t('playback.position')}</dt>
                  <dd className="tabular-nums">
                    {formatOffset(session.viewOffsetMs)} / {session.item.durationMs ? formatOffset(session.item.durationMs) : '?'}
                  </dd>
                  <dt className="text-muted-foreground">{t('playback.since')}</dt>
                  <dd>{formatDateTime(session.startedAt)}</dd>
                </dl>
              </div>
            )}
          </div>
        )
      })}
    </div>
  )
}

/**
 * Plex's transcoder for this stream, on one line: speed (below 1× and not throttled, the player will soon buffer), how
 * far ahead it is, the hardware and the protocol.
 */
function TranscodeLine({ transcode }: { transcode: PlexTranscode }) {
  const { t } = useTranslation()
  const behind = transcode.speed !== null && transcode.speed < 1 && !transcode.throttled && !transcode.complete
  const hardware =
    transcode.hardwareDecoding && transcode.hardwareEncoding && transcode.hardwareDecoding !== transcode.hardwareEncoding
      ? `${transcode.hardwareDecoding} → ${transcode.hardwareEncoding}`
      : (transcode.hardwareEncoding ?? transcode.hardwareDecoding)
  const rest = [
    transcode.complete ? t('playback.transcodeComplete') : transcode.throttled ? t('playback.throttled') : null,
    transcode.bufferedMs !== null && !transcode.complete ? t('playback.ahead', { time: formatOffset(transcode.bufferedMs) }) : null,
    hardware,
    transcode.protocol,
  ].filter(Boolean)

  return (
    <div className="flex flex-wrap items-baseline gap-x-3 text-xs" data-testid="transcoder">
      <span className="text-muted-foreground">{t('playback.transcoder')}</span>
      <span>
        {transcode.speed !== null && <span className={behind ? 'font-medium text-amber-700 dark:text-amber-300' : 'tabular-nums'}>{t('playback.speed', { value: transcode.speed.toFixed(1) })}</span>}
        {behind && <span className="text-amber-700 dark:text-amber-300"> · {t('playback.cannotKeepUp')}</span>}
        {rest.length > 0 && <span className="text-muted-foreground"> · {rest.join(' · ')}</span>}
      </span>
    </div>
  )
}
