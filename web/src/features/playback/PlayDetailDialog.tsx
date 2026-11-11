import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { LinkIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { playback, type Play } from '@/api/client'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Skeleton } from '@/components/ui/skeleton'
import { formatDateTime } from '@/i18n'
import { DecisionBadge, formatOffset, formatPlayed, itemLabel, locationText } from './format'
import { LinkFilmDialog } from './LinkFilmDialog'
import { StreamTracks } from './StreamTracks'

/** A play and the sessions it was made of: player, network, what happened to each track, the file. */
export function PlayDetailDialog({ play, onClose }: { play: Play | null; onClose: () => void }) {
  const { t } = useTranslation()
  const streams = useQuery({ queryKey: ['playback', 'streams', play?.id], queryFn: () => playback.streams(play!.id), enabled: play !== null })
  const [linking, setLinking] = useState(false)
  const linked = play !== null && (play.movieId !== null || play.item.movieId !== null)

  return (
    <Dialog open={play !== null} onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="max-h-[90vh] overflow-y-auto sm:max-w-2xl" data-testid="play-detail">
        {play && (
          <>
            <DialogHeader>
              <DialogTitle className="pr-6">{itemLabel(play.item)}</DialogTitle>
              <DialogDescription>
                {play.user.name} · {formatDateTime(play.startedAt)}
              </DialogDescription>
              <div className="flex flex-wrap items-center gap-1.5 pt-1">
                {play.versionName && <Badge variant="secondary">{play.versionName}</Badge>}
                <Badge variant="outline">{formatPlayed(play.playedSeconds)}</Badge>
                {play.pausedSeconds > 0 && <Badge variant="outline">{t('playback.pausedFor', { time: formatPlayed(play.pausedSeconds) })}</Badge>}
                {play.watched && <Badge variant="outline">{t('playback.watched')}</Badge>}
                {play.item.kind === 'movie' && (
                  <Button type="button" variant="outline" size="sm" className="ml-auto h-7 gap-1.5" onClick={() => setLinking(true)} data-testid="detail-link">
                    <LinkIcon className="size-3.5" aria-hidden="true" />
                    {linked ? t('playback.link.change') : t('playback.link.action')}
                  </Button>
                )}
              </div>
              {play.item.serverTitle && play.item.serverTitle !== play.item.title && <p className="text-xs text-muted-foreground">{t('playback.link.serverTitle', { title: play.item.serverTitle })}</p>}
            </DialogHeader>
            <div className="flex flex-col gap-4">
              {streams.isPending && <Skeleton className="h-40 w-full" />}
              {streams.data?.map((stream, index) => {
                const d = stream.details
                return (
                  <section key={stream.id} className="flex flex-col gap-3 rounded-lg border p-3" data-stream={stream.id}>
                    <div className="flex flex-wrap items-center gap-2">
                      {streams.data.length > 1 && <span className="text-sm font-medium">{t('playback.sessionN', { n: index + 1 })}</span>}
                      <DecisionBadge decision={d.decision} />
                      <Badge variant="outline">{d.local ? t('playback.lan') : t('playback.wan')}</Badge>
                      {d.relayed && <Badge variant="outline">{t('playback.relayed')}</Badge>}
                      {d.secure === false && <Badge variant="outline">{t('playback.insecure')}</Badge>}
                    </div>
                    <dl className="grid grid-cols-[auto_1fr] gap-x-3 gap-y-1 text-xs">
                      <dt className="text-muted-foreground">{t('playback.when')}</dt>
                      <dd>
                        {formatDateTime(stream.startedAt)} → {formatDateTime(stream.stoppedAt)}
                      </dd>
                      <dt className="text-muted-foreground">{t('playback.position')}</dt>
                      <dd className="tabular-nums">
                        {formatOffset(stream.startOffsetMs)} → {formatOffset(stream.endOffsetMs)}
                        {stream.pausedSeconds > 0 && <span className="text-muted-foreground"> · {t('playback.pausedFor', { time: formatPlayed(stream.pausedSeconds) })}</span>}
                      </dd>
                      <dt className="text-muted-foreground">{t('playback.player')}</dt>
                      <dd>{[d.playerTitle, d.playerProduct, [d.playerPlatform, d.playerPlatformVersion].filter(Boolean).join(' ')].filter(Boolean).join(' · ') || t('common.none')}</dd>
                      {d.ipAddress && (
                        <>
                          <dt className="text-muted-foreground">{t('playback.ip')}</dt>
                          <dd className="tabular-nums">{d.ipAddress}</dd>
                          <dt className="text-muted-foreground">{t('playback.location')}</dt>
                          <dd data-testid="location">{locationText(stream.location, t('playback.localNetwork')) ?? t('playback.unknownLocation')}</dd>
                        </>
                      )}
                      {d.bandwidth !== null && (
                        <>
                          <dt className="text-muted-foreground">{t('playback.bandwidth')}</dt>
                          <dd className="tabular-nums">{t('playback.mbps', { value: (d.bandwidth / 1000).toFixed(1) })}</dd>
                        </>
                      )}
                      {stream.versionName && (
                        <>
                          <dt className="text-muted-foreground">{t('playback.version')}</dt>
                          <dd>{stream.versionName}</dd>
                        </>
                      )}
                      {(stream.filePath ?? d.serverFilePath) && (
                        <>
                          <dt className="text-muted-foreground">{t('playback.file')}</dt>
                          <dd className="break-all">{stream.filePath ?? d.serverFilePath}</dd>
                        </>
                      )}
                    </dl>
                    <StreamTracks details={d} />
                  </section>
                )
              })}
            </div>
          </>
        )}
      </DialogContent>
      {/* The play's title and film change: the details close once the link is saved. */}
      <LinkFilmDialog item={linking && play ? { ...play.item, movieId: play.movieId ?? play.item.movieId } : null} onClose={() => setLinking(false)} onLinked={onClose} />
    </Dialog>
  )
}
