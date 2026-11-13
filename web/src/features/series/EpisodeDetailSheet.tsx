import { useState, type ReactNode } from 'react'
import { useQuery } from '@tanstack/react-query'
import { BookmarkIcon, SearchIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { history, playback, type Episode, type EpisodeFile, type Play, type Series, type SeriesVersion } from '@/api/client'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from '@/components/ui/sheet'
import { Skeleton } from '@/components/ui/skeleton'
import { HistoryTable } from '@/features/activity/HistoryTable'
import { PlayDetailDialog } from '@/features/playback/PlayDetailDialog'
import { PlaysTable } from '@/features/playback/PlaysTable'
import { formatDate, formatDateTime } from '@/i18n'
import { EpisodeFileDetails } from './SeriesFiles'
import { orderLabel } from './SeriesOrderDialog'

const code = (season: number, episode: number) => `S${String(season).padStart(2, '0')}E${String(episode).padStart(2, '0')}`

/**
 * One episode, like Sonarr's episode details: what it is (overview, air date, every numbering it has), the file of the
 * version on screen, its history across versions, its plays on the media servers, and a search for it.
 */
export function EpisodeDetailSheet({
  series: s,
  version,
  episode,
  file,
  onClose,
  onSearch,
}: {
  series: Series
  version: SeriesVersion
  episode: Episode
  file: EpisodeFile | undefined
  onClose: () => void
  /** Opens the interactive search for this episode; absent when the film engine owns it. */
  onSearch?: () => void
}) {
  const { t } = useTranslation()
  const events = useQuery({ queryKey: ['history', 'episode', episode.id], queryFn: () => history.list({ episodeId: episode.id, limit: 100 }) })
  const plays = useQuery({ queryKey: ['playback', 'plays', 'episode', episode.id], queryFn: () => playback.plays({ episodeId: episode.id, pageSize: 50 }) })
  const [play, setPlay] = useState<Play | null>(null)
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

  return (
    <Sheet open onOpenChange={(open) => !open && onClose()}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-3xl" data-testid="episode-detail">
        <SheetHeader className="pr-12">
          <SheetTitle className="text-lg">
            <span className="text-muted-foreground tabular-nums">{shown}</span> · {episode.title}
          </SheetTitle>
          <SheetDescription>{s.title}</SheetDescription>
        </SheetHeader>

        <div className="flex flex-col gap-6 px-4 pb-6">
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
            {onSearch && (
              <Button type="button" size="sm" variant="outline" className="ml-auto" onClick={onSearch} data-testid="episode-detail-search">
                <SearchIcon />
                {t('episodeDetail.search')}
              </Button>
            )}
          </div>

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

          <Section title={t('episodeDetail.file', { version: version.name })}>
            {file ? (
              <EpisodeFileDetails seriesId={s.id} fileId={file.id} />
            ) : (
              <p className="text-sm text-muted-foreground">{episode.hasAired ? t('episodeDetail.noFile') : t('seriesDetail.unaired')}</p>
            )}
          </Section>

          <Section title={t('episodeDetail.history')} testId="episode-detail-history">
            {events.isPending ? (
              <Skeleton className="h-16 w-full" />
            ) : events.data && events.data.length > 0 ? (
              <HistoryTable events={events.data} showMovie={false} />
            ) : (
              <p className="text-sm text-muted-foreground">{t('episodeDetail.noHistory')}</p>
            )}
          </Section>

          <Section title={t('episodeDetail.plays', { count: plays.data?.totalRecords ?? 0 })} testId="episode-detail-plays">
            {plays.isPending ? (
              <Skeleton className="h-16 w-full" />
            ) : plays.data && plays.data.records.length > 0 ? (
              <PlaysTable plays={plays.data.records} showItem={false} onSelect={setPlay} />
            ) : (
              <p className="text-sm text-muted-foreground">{t('episodeDetail.noPlays')}</p>
            )}
          </Section>
        </div>
        <PlayDetailDialog play={play} onClose={() => setPlay(null)} />
      </SheetContent>
    </Sheet>
  )
}

function Section({ title, testId, children }: { title: string; testId?: string; children: ReactNode }) {
  return (
    <section className="flex flex-col gap-2" data-testid={testId}>
      <h3 className="text-sm font-semibold">{title}</h3>
      {children}
    </section>
  )
}
