import { useQuery } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { PauseIcon, PlayIcon, LoaderIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { playback, type PlaybackSession, type PlaybackServerStatus } from '@/api/client'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Progress } from '@/components/ui/progress'
import { Skeleton } from '@/components/ui/skeleton'
import { formatDateTime } from '@/i18n'
import { DecisionBadge, formatOffset, itemLabel } from './format'

/** The streams playing now on the followed Plex servers, refreshed every 10 s like Plex's own notifications. */
export function NowPlayingPage() {
  const { t } = useTranslation()
  const query = useQuery({ queryKey: ['playback', 'now'], queryFn: playback.nowPlaying, refetchInterval: 10_000 })
  const data = query.data

  return (
    <>
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="text-2xl font-semibold tracking-tight">{t('playback.nowTitle')}</h1>
        {data && data.sessions.length > 0 && <Badge variant="secondary">{t('playback.streams', { count: data.sessions.length })}</Badge>}
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
      {data && data.sessions.length > 0 && (
        <div className="grid gap-4 md:grid-cols-2 2xl:grid-cols-3" data-testid="now-playing">
          {data.sessions.map((session) => (
            <SessionCard key={session.id} session={session} />
          ))}
        </div>
      )}
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

function SessionCard({ session }: { session: PlaybackSession }) {
  const { t } = useTranslation()
  const d = session.details
  const duration = session.item.durationMs ?? 0
  const progress = duration > 0 ? Math.min(100, (session.viewOffsetMs / duration) * 100) : 0
  const StateIcon = session.state === 'paused' ? PauseIcon : session.state === 'buffering' ? LoaderIcon : PlayIcon
  const source = [d.sourceVideoResolution, d.sourceVideoCodec, d.sourceDynamicRange].filter(Boolean).join(' · ')
  const stream = [d.streamVideoResolution, d.streamVideoCodec].filter(Boolean).join(' · ')

  return (
    <Card className="gap-3" data-session={session.id}>
      <CardHeader className="gap-1">
        <CardTitle className="flex items-start gap-2 text-base leading-snug">
          <StateIcon className="mt-0.5 size-4 shrink-0 text-muted-foreground" aria-label={t(`playback.states.${session.state}`)} />
          {session.item.movieId ? (
            <Link to="/movies/$movieId" params={{ movieId: String(session.item.movieId) }} className="underline-offset-4 hover:underline">
              {itemLabel(session.item)}
            </Link>
          ) : (
            <span>{itemLabel(session.item)}</span>
          )}
        </CardTitle>
        <p className="text-sm text-muted-foreground">
          {session.user.name} · {d.playerTitle ?? d.playerProduct ?? t('common.none')}
          {d.playerPlatform ? ` (${d.playerPlatform})` : ''}
        </p>
      </CardHeader>
      <CardContent className="flex flex-col gap-3">
        <div className="flex flex-col gap-1">
          <Progress value={progress} aria-label={t('playback.progress')} />
          <div className="flex justify-between text-xs tabular-nums text-muted-foreground">
            <span>{formatOffset(session.viewOffsetMs)}</span>
            <span>{duration > 0 ? formatOffset(duration) : ''}</span>
          </div>
        </div>
        <div className="flex flex-wrap items-center gap-1.5">
          <DecisionBadge decision={d.decision} />
          {session.versionName && <Badge variant="secondary">{session.versionName}</Badge>}
          {d.hardwareTranscode && <Badge variant="outline">{t('playback.hardware')}</Badge>}
          <Badge variant="outline">{d.local ? t('playback.lan') : t('playback.wan')}</Badge>
        </div>
        <dl className="grid grid-cols-[auto_1fr] gap-x-3 gap-y-1 text-xs">
          {source && (
            <>
              <dt className="text-muted-foreground">{t('playback.source')}</dt>
              <dd>{source}</dd>
            </>
          )}
          {d.decision === 'transcode' && stream && (
            <>
              <dt className="text-muted-foreground">{t('playback.stream')}</dt>
              <dd>{stream}</dd>
            </>
          )}
          {d.bandwidth !== null && (
            <>
              <dt className="text-muted-foreground">{t('playback.bandwidth')}</dt>
              <dd className="tabular-nums">{t('playback.mbps', { value: (d.bandwidth / 1000).toFixed(1) })}</dd>
            </>
          )}
          {d.ipAddress && (
            <>
              <dt className="text-muted-foreground">{t('playback.ip')}</dt>
              <dd className="tabular-nums">{d.ipAddress}</dd>
            </>
          )}
          <dt className="text-muted-foreground">{t('playback.since')}</dt>
          <dd>{formatDateTime(session.startedAt)}</dd>
        </dl>
      </CardContent>
    </Card>
  )
}
