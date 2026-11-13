import { useState, type ReactNode } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { cn } from 'cn'
import { BookmarkIcon, SearchIcon, ZapIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ApiError, history, playback, series as seriesApi, type Episode, type EpisodeFile, type Play, type Series, type SeriesVersion } from '@/api/client'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Skeleton } from '@/components/ui/skeleton'
import { HistoryTable } from '@/features/activity/HistoryTable'
import { PlayDetailDialog } from '@/features/playback/PlayDetailDialog'
import { PlaysTable } from '@/features/playback/PlaysTable'
import { formatDate, formatDateTime } from '@/i18n'
import { EpisodeFileDetails } from './SeriesFiles'
import { orderLabel } from './SeriesOrderDialog'

const code = (season: number, episode: number) => `S${String(season).padStart(2, '0')}E${String(episode).padStart(2, '0')}`

const tabs = ['summary', 'file', 'history', 'plays'] as const
type Tab = (typeof tabs)[number]

/**
 * One episode, like Sonarr's episode details, in a centered dialog like the rest of the app. Tabs: what it is (overview,
 * air date, every numbering it has), the file of the version on screen, its history across versions, its plays on the
 * media servers. The automatic and the interactive search for it sit above the tabs.
 */
export function EpisodeDetailDialog({
  series: s,
  version,
  episode,
  file,
  onClose,
  onInteractiveSearch,
  onChanged,
}: {
  series: Series
  version: SeriesVersion
  episode: Episode
  file: EpisodeFile | undefined
  onClose: () => void
  /** Opens the interactive search for this episode; absent (with the automatic search) when the film engine owns it. */
  onInteractiveSearch?: () => void
  onChanged: () => void
}) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [tab, setTab] = useState<Tab>('summary')
  const events = useQuery({ queryKey: ['history', 'episode', episode.id], queryFn: () => history.list({ episodeId: episode.id, limit: 100 }) })
  const plays = useQuery({ queryKey: ['playback', 'plays', 'episode', episode.id], queryFn: () => playback.plays({ episodeId: episode.id, pageSize: 50 }) })
  const [play, setPlay] = useState<Play | null>(null)
  const automatic = useMutation({
    mutationFn: () => seriesApi.searchEpisode(s.id, episode.id),
    onSuccess: (results) => {
      for (const r of results) {
        if (r.grabbed > 0) toast.success(t('episodeDetail.autoGrabbed', { version: r.versionName }))
        else if (r.picked > 0) toast.success(t('episodeDetail.autoPicked', { version: r.versionName }))
        else toast.info(t('episodeDetail.autoNothing', { version: r.versionName, detail: r.detail ?? '' }))
      }
      void queryClient.invalidateQueries({ queryKey: ['history', 'episode', episode.id] })
      void queryClient.invalidateQueries({ queryKey: ['queue'] })
      onChanged()
    },
    onError: (e) => toast.error(e instanceof ApiError ? e.message : t('search.failed')),
  })

  const alt = s.ordering !== 'tvdb'
  const tvdb = code(episode.seasonNumber, episode.episodeNumber)
  const shown = alt && !episode.notInOrder ? code(episode.displaySeasonNumber, episode.displayEpisodeNumber) : tvdb
  const scene =
    episode.sceneSeasonNumber != null && episode.sceneEpisodeNumber != null
      ? [code(episode.sceneSeasonNumber, episode.sceneEpisodeNumber), episode.sceneAbsoluteNumber != null ? t('episodeDetail.absoluteShort', { number: episode.sceneAbsoluteNumber }) : null].filter(Boolean).join(' · ')
      : null

  const facts: [string, ReactNode][] = [
    [t('episodeDetail.airDate'), episode.airDateUtc ? formatDateTime(episode.airDateUtc) : episode.airDate ? formatDate(episode.airDate) : t('seriesDetail.tba')],
    [t('episodeDetail.runtime'), episode.runtime ? t('common.minutes', { count: episode.runtime }) : null],
    [t('episodeDetail.tvdbNumber'), episode.absoluteEpisodeNumber != null ? `${tvdb} · ${t('episodeDetail.absoluteShort', { number: episode.absoluteEpisodeNumber })}` : tvdb],
    [
      t('episodeDetail.orderNumber', { order: orderLabel(t, s) }),
      alt ? (episode.notInOrder ? t('episodeDetail.notInOrder') : [shown, episode.orderAbsoluteNumber != null ? t('episodeDetail.absoluteShort', { number: episode.orderAbsoluteNumber }) : null].filter(Boolean).join(' · ')) : null,
    ],
    [t('episodeDetail.sceneNumber'), scene],
    [
      t('episodeDetail.ids'),
      episode.tvdbId ? (
        <a key="tvdb" href={`https://thetvdb.com/dereferrer/episode/${episode.tvdbId}`} target="_blank" rel="noreferrer" className="underline underline-offset-4 hover:text-foreground">
          {t('seriesDetail.ids.tvdb', { id: episode.tvdbId })}
        </a>
      ) : null,
    ],
  ]

  const tabLabel: Record<Tab, string> = {
    summary: t('episodeDetail.tabs.summary'),
    file: t('episodeDetail.tabs.file'),
    history: events.data ? t('episodeDetail.tabs.historyCount', { count: events.data.length }) : t('episodeDetail.tabs.history'),
    plays: plays.data ? t('episodeDetail.tabs.playsCount', { count: plays.data.totalRecords }) : t('episodeDetail.tabs.plays'),
  }

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="flex max-h-[90vh] flex-col sm:max-w-4xl" data-testid="episode-detail">
        <DialogHeader>
          <DialogTitle className="text-lg">
            <span className="text-muted-foreground tabular-nums">{shown}</span> · {episode.title}
          </DialogTitle>
          <DialogDescription>{s.title}</DialogDescription>
        </DialogHeader>

        <div className="flex flex-wrap items-center gap-2">
          {episode.versions.map((state) => {
            const v = s.versions.find((x) => x.id === state.versionId)
            return (
              <Badge key={state.versionId} variant={state.versionId === version.id ? 'secondary' : 'outline'} className="gap-1" data-testid="episode-detail-version">
                <BookmarkIcon className={state.monitored ? 'fill-current' : undefined} aria-hidden="true" />
                {v?.name}
                <span className="font-normal text-muted-foreground">· {state.hasFile ? t('episodeDetail.downloaded') : episode.hasAired ? t('seriesDetail.missing') : t('seriesDetail.unaired')}</span>
              </Badge>
            )
          })}
          {onInteractiveSearch && (
            <span className="ml-auto flex gap-2">
              <Button type="button" size="sm" variant="outline" onClick={() => automatic.mutate()} disabled={automatic.isPending} title={t('episodeDetail.autoSearchHint')} data-testid="episode-detail-auto-search">
                <ZapIcon className={automatic.isPending ? 'animate-pulse' : undefined} />
                {automatic.isPending ? t('search.searching') : t('episodeDetail.autoSearch')}
              </Button>
              <Button type="button" size="sm" variant="outline" onClick={onInteractiveSearch} data-testid="episode-detail-search">
                <SearchIcon />
                {t('episodeDetail.search')}
              </Button>
            </span>
          )}
        </div>

        <div className="flex gap-1 border-b" role="tablist" aria-label={t('episodeDetail.tabsLabel')}>
          {tabs.map((key) => (
            <button
              key={key}
              type="button"
              role="tab"
              id={`episode-tab-${key}`}
              aria-selected={tab === key}
              aria-controls={`episode-panel-${key}`}
              onClick={() => setTab(key)}
              className={cn(
                '-mb-px border-b-2 px-3 py-1.5 text-sm font-medium transition-colors outline-none focus-visible:ring-2 focus-visible:ring-ring/50',
                tab === key ? 'border-primary text-foreground' : 'border-transparent text-muted-foreground hover:text-foreground',
              )}
              data-testid={`episode-tab-${key}`}
            >
              {tabLabel[key]}
            </button>
          ))}
        </div>

        <div className="min-h-64 overflow-y-auto" role="tabpanel" id={`episode-panel-${tab}`} aria-labelledby={`episode-tab-${tab}`} data-testid={`episode-panel-${tab}`}>
          {tab === 'summary' && (
            <div className="flex flex-col gap-4">
              {episode.overview && <p className="text-sm leading-relaxed text-muted-foreground">{episode.overview}</p>}
              <dl className="grid grid-cols-[10rem_1fr] gap-x-3 gap-y-1.5 text-sm" data-testid="episode-detail-facts">
                {facts
                  .filter(([, value]) => value)
                  .map(([label, value]) => (
                    <div key={label} className="contents">
                      <dt className="text-muted-foreground">{label}</dt>
                      <dd className="tabular-nums">{value}</dd>
                    </div>
                  ))}
              </dl>
            </div>
          )}

          {tab === 'file' && (
            <div className="flex flex-col gap-2">
              <p className="text-xs text-muted-foreground">{t('episodeDetail.fileOf', { version: version.name })}</p>
              {file ? <EpisodeFileDetails seriesId={s.id} fileId={file.id} /> : <p className="text-sm text-muted-foreground">{episode.hasAired ? t('episodeDetail.noFile') : t('seriesDetail.unaired')}</p>}
            </div>
          )}

          {tab === 'history' &&
            (events.isPending ? (
              <Skeleton className="h-16 w-full" />
            ) : events.data && events.data.length > 0 ? (
              <HistoryTable events={events.data} showMovie={false} />
            ) : (
              <p className="text-sm text-muted-foreground">{t('episodeDetail.noHistory')}</p>
            ))}

          {tab === 'plays' &&
            (plays.isPending ? (
              <Skeleton className="h-16 w-full" />
            ) : plays.data && plays.data.records.length > 0 ? (
              <PlaysTable plays={plays.data.records} showItem={false} onSelect={setPlay} />
            ) : (
              <p className="text-sm text-muted-foreground">{t('episodeDetail.noPlays')}</p>
            ))}
        </div>
        <PlayDetailDialog play={play} onClose={() => setPlay(null)} />
      </DialogContent>
    </Dialog>
  )
}
