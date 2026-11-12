import { useId, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { AudioLinesIcon, CaptionsIcon, ChevronDownIcon, FilmIcon, GaugeIcon, LayoutGridIcon, ListIcon, LoaderIcon, PackageIcon, PauseIcon, PlayIcon, SquareIcon } from 'lucide-react'
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
import { DecisionBadge, formatCodec, formatOffset, itemLabel, locationText } from './format'
import { StopStreamDialog } from './StopStreamDialog'
import { StreamTracks } from './StreamTracks'
import { useLivePositions } from './livePosition'
import { hideBroken, sizedImage } from './imageSize'

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

const severityKeys = ['fine', 'transcoding', 'slow'] as const

/** Where a stream is now: the server's position moved on locally between refreshes (see `useLivePositions`). */
type Live = ReturnType<typeof useLivePositions>

const remainingMs = (s: PlaybackSession, offsetMs: number) => Math.max(0, (s.item.durationMs ?? 0) - offsetMs)
const progressOf = (s: PlaybackSession, offsetMs: number) => (s.item.durationMs ? Math.min(100, (offsetMs / s.item.durationMs) * 100) : 0)

/** The bar glides from one second to the next while playing; a jump, a pause or reduced motion moves it at once. */
function LiveProgress({ session, live, className }: { session: PlaybackSession; live: Live; className?: string }) {
  const { t } = useTranslation()
  return (
    <Progress
      value={progressOf(session, live.positionOf(session))}
      className={className}
      indicatorClassName={live.glides(session) ? 'duration-1000 ease-linear motion-reduce:transition-none' : 'transition-none'}
      aria-label={t('playback.progress')}
    />
  )
}
/** TMDB serves smaller files under the same path: a blurred backdrop needs no more than w300. */

/**
 * The streams playing now on the followed Plex servers, refreshed every 10 s. Cards (poster, tracks, transcoder) or
 * one row per stream for busy evenings; the choice is remembered in this browser.
 */
export function NowPlayingPage() {
  const { t } = useTranslation()
  const query = useQuery({ queryKey: ['playback', 'now'], queryFn: playback.nowPlaying, refetchInterval: 10_000 })
  const data = query.data
  const live = useLivePositions(data?.sessions, query.dataUpdatedAt)
  const [view, setView] = useState<View>(storedView)
  const [stopping, setStopping] = useState<PlaybackSession | null>(null)
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
        <fieldset className="ml-auto shrink-0" aria-label={t('playback.view')}>
          <div className="flex rounded-md border p-0.5">
            <Button size="sm" variant={view === 'cards' ? 'secondary' : 'ghost'} className="h-7 gap-1.5" aria-pressed={view === 'cards'} onClick={() => choose('cards')} data-testid="view-cards">
              <LayoutGridIcon className="size-3.5" />
              {t('playback.viewCards')}
            </Button>
            <Button size="sm" variant={view === 'rows' ? 'secondary' : 'ghost'} className="h-7 gap-1.5" aria-pressed={view === 'rows'} onClick={() => choose('rows')} data-testid="view-rows">
              <ListIcon className="size-3.5" />
              {t('playback.viewRows')}
            </Button>
          </div>
        </fieldset>
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
            <SessionCard key={session.id} session={session} live={live} onStop={setStopping} />
          ))}
        </div>
      )}
      {data && data.sessions.length > 0 && view === 'rows' && <SessionRows sessions={data.sessions} live={live} onStop={setStopping} />}
      <StopStreamDialog session={stopping} onClose={() => setStopping(null)} />
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
    <img src={sizedImage(session.posterUrl, width)} onError={hideBroken} alt="" aria-hidden="true" className={cn('aspect-[2/3] shrink-0 rounded-md object-cover shadow-sm', className)} />
  ) : (
    <div className={cn('flex aspect-[2/3] shrink-0 items-center justify-center rounded-md bg-muted text-xs font-semibold text-muted-foreground', className)} aria-hidden="true">
      {session.item.title.slice(0, 2).toUpperCase()}
    </div>
  )
}

/** "Started 09/11/2026, 21:05 · ends 23:51" on hover of the position or the time left. */
function Times({ session, live, children, className }: { session: PlaybackSession; live: Live; children: React.ReactNode; className?: string }) {
  const { t } = useTranslation()
  const ends = session.state !== 'paused' && session.item.durationMs ? formatTime(Date.now() + remainingMs(session, live.positionOf(session))) : null
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
function SessionCard({ session, live, onStop }: { session: PlaybackSession; live: Live; onStop: (session: PlaybackSession) => void }) {
  const { t } = useTranslation()
  const d = session.details
  const player = [d.playerTitle ?? d.playerProduct, d.playerPlatform].filter(Boolean).join(' · ')
  const position = live.positionOf(session)
  const network = [d.bandwidth !== null ? t('playback.mbps', { value: (d.bandwidth / 1000).toFixed(1) }) : null, d.ipAddress, locationText(session.location, t('playback.localNetwork'), true)].filter(Boolean).join(' · ')

  return (
    <Card className="group/tile relative h-full gap-0 overflow-hidden p-0" data-session={session.id} data-severity={severityKeys[severity(session)]}>
      <div className="relative isolate h-28">
        {session.fanartUrl ? (
          <img src={sizedImage(session.fanartUrl, 780)} onError={hideBroken} alt="" aria-hidden="true" className="absolute inset-0 -z-10 size-full object-cover" />
        ) : (
          <div className="absolute inset-0 -z-10 bg-muted" />
        )}
        <div className="absolute inset-0 -z-10 bg-gradient-to-t from-card via-card/80 to-card/20" />
        {/* Above the banner content, which spans the whole banner and would take the pointer otherwise. */}
        {session.canStop && <StopButton session={session} onStop={onStop} className="absolute top-2 right-2 z-10" />}
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
          <LiveProgress session={session} live={live} />
          <div className="flex flex-wrap justify-between gap-x-3 text-xs tabular-nums text-muted-foreground">
            <Times session={session} live={live}>
              {formatOffset(position)} / {session.item.durationMs ? formatOffset(session.item.durationMs) : '?'}
              {session.state !== 'paused' && session.item.durationMs ? ` · ${t('playback.ends', { time: formatTime(Date.now() + remainingMs(session, position)) })}` : ''}
            </Times>
            {/* LAN or WAN shows in the address and its place. */}
            <span className="min-w-0 truncate" title={locationText(session.location, t('playback.localNetwork')) ?? undefined} data-testid="network">
              {network}
            </span>
          </div>
        </div>
      </div>
    </Card>
  )
}

/**
 * A light stop icon on a tile, red under the pointer or the keyboard focus. Shown when the tile is hovered or the
 * button has the focus, and always on screens that cannot hover. It only opens the confirmation; stopping happens
 * from there.
 */
function StopButton({ session, onStop, className }: { session: PlaybackSession; onStop: (session: PlaybackSession) => void; className?: string }) {
  const { t } = useTranslation()
  return (
    <Tooltip>
      <TooltipTrigger asChild>
        <Button
          type="button"
          size="icon-sm"
          variant="ghost"
          className={cn(
            'bg-card/60 text-muted-foreground opacity-0 backdrop-blur-sm transition-opacity hover:bg-card/80 hover:text-destructive focus-visible:text-destructive focus-visible:opacity-100 group-hover/tile:opacity-100 [@media(hover:none)]:opacity-100',
            className,
          )}
          aria-label={t('playback.stop')}
          onClick={() => onStop(session)}
          data-testid="stop"
        >
          <SquareIcon />
        </Button>
      </TooltipTrigger>
      <TooltipContent>{t('playback.stop')}</TooltipContent>
    </Tooltip>
  )
}

/** The Plex account picture, or the name's initial. */
function Avatar({ name, url, className }: { name: string; url: string | null; className?: string }) {
  const [broken, setBroken] = useState(false)
  return url && !broken ? (
    <img src={url} alt="" aria-hidden="true" className={cn('size-8 shrink-0 rounded-full object-cover', className)} onError={() => setBroken(true)} />
  ) : (
    <span className={cn('flex size-8 shrink-0 items-center justify-center rounded-full bg-muted text-xs font-semibold text-muted-foreground', className)} aria-hidden="true">
      {name.slice(0, 1).toUpperCase()}
    </span>
  )
}

/** One line per stream, for evenings with many; a click opens the same details as a card under the row. */
function SessionRows({ sessions, live, onStop }: { sessions: PlaybackSession[]; live: Live; onStop: (session: PlaybackSession) => void }) {
  const { t } = useTranslation()
  const [open, setOpen] = useState<number | null>(null)
  const rowsId = useId()

  return (
    <div className="divide-y overflow-hidden rounded-lg border" data-testid="now-playing-rows">
      {sessions.map((session) => {
        const d = session.details
        const expanded = open === session.id
        const position = live.positionOf(session)
        return (
          <div key={session.id} className="relative" data-session={session.id}>
            {/*
              The row toggles through a real button under it; the row's content is that button's <label>, so a click
              anywhere on it toggles too, while the title link inside still navigates (a label does not activate its
              control for a click on a link) and the time left keeps its tooltip.
            */}
            <div className="relative hover:bg-muted/50">
              <button
                id={`${rowsId}-${session.id}`}
                type="button"
                aria-expanded={expanded}
                aria-label={`${session.user.name} · ${itemLabel(session.item)}`}
                onClick={() => setOpen(expanded ? null : session.id)}
                className="absolute inset-0 cursor-pointer outline-none focus-visible:ring-2 focus-visible:ring-ring/50 focus-visible:ring-inset"
              />
              <label htmlFor={`${rowsId}-${session.id}`} className="relative flex w-full cursor-pointer items-center gap-3 px-3 py-2 text-left text-sm">
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
                  <LiveProgress session={session} live={live} className="h-1.5" />
                </span>
                <Times session={session} live={live} className="hidden w-24 text-right text-xs tabular-nums text-muted-foreground md:block">
                  {t('playback.left', { time: formatOffset(remainingMs(session, position)) })}
                </Times>
                <span className="hidden w-20 text-right text-xs tabular-nums text-muted-foreground md:block">{d.bandwidth !== null && t('playback.mbps', { value: (d.bandwidth / 1000).toFixed(1) })}</span>
                <ChevronDownIcon className={cn('size-4 shrink-0 text-muted-foreground transition-transform', expanded && 'rotate-180')} />
              </label>
            </div>
            {expanded && (
              <div className="grid gap-4 bg-muted/30 py-3 pr-4 pl-12 md:grid-cols-2">
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
                      <dt className="text-muted-foreground">{t('playback.location')}</dt>
                      <dd>{locationText(session.location, t('playback.localNetwork')) ?? t('playback.unknownLocation')}</dd>
                    </>
                  )}
                  <dt className="text-muted-foreground">{t('playback.position')}</dt>
                  <dd className="tabular-nums">
                    {formatOffset(position)} / {session.item.durationMs ? formatOffset(session.item.durationMs) : '?'}
                  </dd>
                  <dt className="text-muted-foreground">{t('playback.since')}</dt>
                  <dd>{formatDateTime(session.startedAt)}</dd>
                  {session.canStop && (
                    <dd className="col-span-2 pt-2">
                      <Button type="button" size="sm" variant="outline" className="hover:bg-destructive hover:text-white dark:hover:bg-destructive/80" onClick={() => onStop(session)} data-testid="stop">
                        <SquareIcon className="fill-current" />
                        {t('playback.stop')}
                      </Button>
                    </dd>
                  )}
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
