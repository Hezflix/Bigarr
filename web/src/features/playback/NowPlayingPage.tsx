import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { ChevronDownIcon, LayoutGridIcon, ListIcon, LoaderIcon, PauseIcon, PlayIcon } from 'lucide-react'
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
import { DecisionBadge, formatOffset, itemLabel } from './format'
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
  return <div className={cn('absolute inset-y-0 left-0 w-1.5', stripes[level])} title={t(`playback.severity.${severityKeys[level]}`)} data-severity={severityKeys[level]} />
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

const upper = (value: string | null | undefined) => value?.toUpperCase() ?? null
const channelsOf = (count: number | null) => (count === null ? null : count === 6 ? '5.1' : count === 8 ? '7.1' : count === 2 ? '2.0' : `${count} ch`)
const join = (...parts: (string | number | null | undefined)[]) => parts.filter((p) => p !== null && p !== undefined && p !== '').join(' ')

/** "Direct play", "Copy", "Transcode", "Burned in", amber when the track is converted. */
function TrackDecision({ value }: { value: string | null }) {
  const { t } = useTranslation()
  if (!value) return null
  const converted = ['transcode', 'burn'].includes(value.toLowerCase())
  return <span className={converted ? 'font-medium text-amber-700 dark:text-amber-300' : undefined}>{t(`playback.trackDecisions.${value.toLowerCase()}`, { defaultValue: value })}</span>
}

/** One labeled line of a card; long values are cut, the whole of it shows on hover. */
function Field({ label, title, children }: { label: string; title?: string; children: React.ReactNode }) {
  return (
    <>
      <dt className="text-[10px] font-medium tracking-wide text-muted-foreground uppercase">{label}</dt>
      <dd className="truncate" title={title}>
        {children}
      </dd>
    </>
  )
}

/**
 * A stream as a fixed-size tile, Tautulli's layout: the poster the whole height on the left, the same labeled lines
 * on every card (a track that is not converted says so rather than disappearing), the position and end time in the
 * corner, then the title and the user under the progress. Every card is the same size, so they line up in a grid.
 */
function SessionCard({ session }: { session: PlaybackSession }) {
  const { t } = useTranslation()
  const d = session.details
  const tr = session.transcode
  const transcoding = d.decision === 'transcode'
  const behind = severity(session) === 2
  const mbpsOf = (kbps: number | null) => (kbps === null ? null : t('playback.mbps', { value: (kbps / 1000).toFixed(1) }))

  const quality = transcoding ? join(mbpsOf(d.streamBitrate), d.streamVideoResolution) : t('playback.original', { bitrate: mbpsOf(d.sourceBitrate) ?? '?' })
  const streamExtra = tr ? [t('playback.speed', { value: tr.speed?.toFixed(1) ?? '?' }), tr.complete ? t('playback.transcodeComplete') : tr.throttled ? t('playback.throttled') : null, tr.bufferedMs !== null && !tr.complete ? t('playback.ahead', { time: formatOffset(tr.bufferedMs) }) : null].filter(Boolean).join(', ') : null
  const container = d.streamContainer && d.sourceContainer && d.streamContainer !== d.sourceContainer ? `${upper(d.sourceContainer)} → ${upper(d.streamContainer)}` : upper(d.sourceContainer ?? d.streamContainer)
  const videoConverted = d.videoDecision?.toLowerCase() === 'transcode'
  const video = join(d.sourceVideoCodec, d.sourceVideoResolution, d.sourceDynamicRange) + (videoConverted ? ` → ${join(d.streamVideoCodec, d.streamVideoResolution)}${d.hardwareTranscode ? ` (${t('playback.hw')})` : ''}` : '')
  const audioConverted = d.audioDecision?.toLowerCase() === 'transcode'
  const audio = join(d.sourceAudioCodec, channelsOf(d.sourceAudioChannels)) + (audioConverted ? ` → ${join(d.streamAudioCodec, channelsOf(d.streamAudioChannels))}` : '')
  const hardware = tr?.hardwareEncoding ?? tr?.hardwareDecoding

  return (
    <Card className="relative isolate gap-0 overflow-hidden p-0" data-session={session.id}>
      {/* The film's backdrop behind the whole card, blurred and tinted with the card colour: a mood, never in the way. */}
      {session.fanartUrl && <img src={tmdbSize(session.fanartUrl, 300)} alt="" aria-hidden="true" className="absolute inset-0 -z-10 size-full scale-125 object-cover blur-2xl" />}
      <div className="absolute inset-0 -z-10 bg-card/85" />
      <Stripe session={session} />
      <div className="relative h-52">
        <div className="relative flex h-full gap-3 py-2.5 pr-3 pl-4">
          <PosterImage session={session} width={342} className="h-full w-auto" />
          <div className="flex min-w-0 flex-1 flex-col justify-between">
            <dl className="grid grid-cols-[4.75rem_minmax(0,1fr)] items-baseline gap-x-2 gap-y-0.5 text-xs">
              <Field label={t('playback.player')} title={join(d.playerProduct, d.playerTitle, d.playerPlatform)}>
                {d.playerTitle ?? d.playerProduct ?? t('common.none')}
                {d.playerPlatform && <span className="text-muted-foreground"> · {d.playerPlatform}</span>}
              </Field>
              <Field label={t('playback.quality')}>
                {quality}
                {session.versionName && <span className="text-muted-foreground"> · {session.versionName}</span>}
              </Field>
              <Field label={t('playback.stream')} title={hardware ? `${streamExtra ?? ''} · ${hardware}` : (streamExtra ?? undefined)}>
                <DecisionText decision={d.decision} />
                {streamExtra && <span className={behind ? 'text-amber-700 dark:text-amber-300' : 'text-muted-foreground'}> ({streamExtra}{behind ? `, ${t('playback.cannotKeepUp')}` : ''})</span>}
              </Field>
              <Field label={t('playback.container')}>{container ?? t('common.none')}</Field>
              <Field label={t('playback.video')} title={video}>
                <TrackDecision value={d.videoDecision} /> <span className="text-muted-foreground">{video}</span>
              </Field>
              <Field label={t('playback.audio')} title={audio}>
                <TrackDecision value={d.audioDecision} /> <span className="text-muted-foreground">{audio}</span>
              </Field>
              <Field label={t('playback.subtitles')}>{d.subtitleDecision ? <TrackDecision value={d.subtitleDecision} /> : t('playback.noneTrack')}</Field>
              <Field label={t('playback.location')}>
                {d.local ? t('playback.lan') : t('playback.wan')}
                {d.ipAddress && `: ${d.ipAddress}`}
              </Field>
              <Field label={t('playback.bandwidth')}>{mbpsOf(d.bandwidth) ?? t('common.none')}</Field>
            </dl>
          </div>
          <Times session={session} className="absolute right-3 bottom-2 text-right text-[11px] leading-tight tabular-nums text-muted-foreground">
            {session.state !== 'paused' && session.item.durationMs ? (
              <span className="block">{t('playback.ends', { time: formatTime(Date.now() + remainingMs(session)) })}</span>
            ) : null}
            <span className="block">
              {formatOffset(session.viewOffsetMs)} / {session.item.durationMs ? formatOffset(session.item.durationMs) : '?'}
            </span>
          </Times>
        </div>
      </div>
      <Progress value={progressOf(session)} className="relative h-1 rounded-none" aria-label={t('playback.progress')} />
      <div className="relative flex items-center gap-3 py-2 pr-3 pl-4">
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-1.5 text-sm font-semibold">
            <StateIcon session={session} className="size-3.5" />
            <span className="truncate">
              <Title session={session} />
            </span>
          </div>
          <div className="truncate pl-5 text-xs text-muted-foreground">
            {session.item.kind === 'episode' ? `S${String(session.item.seasonNumber ?? 0).padStart(2, '0')} · E${String(session.item.episodeNumber ?? 0).padStart(2, '0')}` : (session.item.year ?? '')}
          </div>
        </div>
        <span className="truncate text-sm text-muted-foreground">{session.user.name}</span>
        <Avatar name={session.user.name} url={session.user.thumbUrl} />
      </div>
    </Card>
  )
}

function DecisionText({ decision }: { decision: PlaybackSession['details']['decision'] }) {
  const { t } = useTranslation()
  if (!decision) return <>{t('common.none')}</>
  return <span className={decision === 'transcode' ? 'font-medium text-amber-700 dark:text-amber-300' : undefined}>{t(`playback.decisions.${decision}`)}</span>
}

/** The Plex account picture, or the name's initials. */
function Avatar({ name, url }: { name: string; url: string | null }) {
  const [broken, setBroken] = useState(false)
  return url && !broken ? (
    <img src={url} alt="" className="size-8 shrink-0 rounded-full object-cover" onError={() => setBroken(true)} />
  ) : (
    <span className="flex size-8 shrink-0 items-center justify-center rounded-full bg-muted text-xs font-semibold text-muted-foreground" aria-hidden="true">
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
