import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { cn } from 'cn'
import { BookmarkIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { history, playback, type Episode, type EpisodeFile, type Play, type Series, type SeriesVersion } from '@/api/client'
import { Badge } from '@/components/ui/badge'
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Skeleton } from '@/components/ui/skeleton'
import { HistoryTable } from '@/features/activity/HistoryTable'
import { PlayDetailDialog } from '@/features/playback/PlayDetailDialog'
import { PlaysTable } from '@/features/playback/PlaysTable'
import { formatDate } from '@/i18n'
import { AutomaticSearchButton, InteractiveSearchButton } from '@/features/search/SearchButtons'
import { EpisodeFileDetails } from './SeriesFiles'
import { useEpisodeAutomaticSearch } from './useEpisodeAutomaticSearch'

const code = (season: number, episode: number) => `S${String(season).padStart(2, '0')}E${String(episode).padStart(2, '0')}`

const tabs = ['file', 'history', 'plays'] as const
type Tab = (typeof tabs)[number]

/**
 * One episode, like Sonarr's episode details, in a centered dialog like the rest of the app. A one-line header (number,
 * title, air date, state per version), then tabs: the file of the version on screen, its history across versions, its
 * plays on the media servers. The automatic and the interactive search sit at the end of the tab bar.
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
  const [tab, setTab] = useState<Tab>('file')
  const events = useQuery({ queryKey: ['history', 'episode', episode.id], queryFn: () => history.list({ episodeId: episode.id, limit: 100 }) })
  const plays = useQuery({ queryKey: ['playback', 'plays', 'episode', episode.id], queryFn: () => playback.plays({ episodeId: episode.id, pageSize: 50 }) })
  const [play, setPlay] = useState<Play | null>(null)
  const automatic = useEpisodeAutomaticSearch(s.id, onChanged)

  const shown = s.ordering !== 'tvdb' && !episode.notInOrder ? code(episode.displaySeasonNumber, episode.displayEpisodeNumber) : code(episode.seasonNumber, episode.episodeNumber)
  const aired = episode.airDate ? formatDate(episode.airDate) : episode.airDateUtc ? formatDate(episode.airDateUtc) : t('seriesDetail.tba')

  const tabLabel: Record<Tab, string> = {
    file: t('episodeDetail.tabs.file'),
    history: events.data ? t('episodeDetail.tabs.historyCount', { count: events.data.length }) : t('episodeDetail.tabs.history'),
    plays: plays.data ? t('episodeDetail.tabs.playsCount', { count: plays.data.totalRecords }) : t('episodeDetail.tabs.plays'),
  }

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="flex max-h-[90vh] flex-col sm:max-w-4xl" data-testid="episode-detail">
        <DialogHeader className="gap-1 pr-8 text-left">
          <DialogTitle className="truncate text-base" data-testid="episode-detail-title">
            <span className="text-muted-foreground tabular-nums">{shown}</span> · {episode.title}
          </DialogTitle>
          <DialogDescription className="flex flex-wrap items-center gap-1.5 text-xs">
            <span className="tabular-nums">{aired}</span>
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
          </DialogDescription>
        </DialogHeader>

        <div className="flex flex-wrap items-end gap-2 border-b">
          <div className="flex gap-1" role="tablist" aria-label={t('episodeDetail.tabsLabel')}>
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
          {onInteractiveSearch && (
            <span className="ml-auto flex flex-wrap gap-2 pb-1.5">
              <AutomaticSearchButton size="sm" onClick={() => automatic.mutate(episode.id)} pending={automatic.isPending} data-testid="episode-detail-auto-search" />
              <InteractiveSearchButton size="sm" onClick={onInteractiveSearch} data-testid="episode-detail-search" />
            </span>
          )}
        </div>

        <div className="min-h-64 overflow-y-auto" role="tabpanel" id={`episode-panel-${tab}`} aria-labelledby={`episode-tab-${tab}`} data-testid={`episode-panel-${tab}`}>
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
