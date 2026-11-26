import { useEffect, useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate } from '@tanstack/react-router'
import { Trans, useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import {
  discover,
  type MinimumAvailability,
  type MonitoringOption,
  monitoringOptions,
  type RequestMediaType,
  requests,
  type SeriesType,
  seriesTypes,
  slugify,
  minimumAvailabilities,
} from '@/api/client'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Checkbox } from '@/components/ui/checkbox'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { FormField, VersionNameField } from '@/features/library/addParts'
import { TitleForm } from '@/features/library/TitleForm'
import { seasonChoices, takenReason } from './RequestDialog'

const PICKED = 'picked'
const AUTO = 'auto'

/** A title Discover cannot show (a series only TVDB has): what the search found of it. */
export type AddPreset = { title: string; year: number | null; posterUrl: string | null; overview: string | null }

/**
 * The admin's add, on a Discover title: everything the library's former Add pages offered (version name, profile, root
 * folder, monitoring, search on add, series type, season folders, new seasons) plus the version slot and the season
 * picker. It goes through the request path: an approved request in the admin's name, forwarded like any other.
 * With `onAdded` (the manual import's "Add & pick"), the new title's library id goes back to the caller instead of
 * opening its page, and no search starts on add: the file to import is already there.
 */
export function AdminAddDialog({
  mediaType,
  tmdbId,
  tvdbId,
  preset,
  onClose,
  onAdded,
}: {
  mediaType: RequestMediaType
  tmdbId: number | null
  tvdbId?: number | null
  preset?: AddPreset
  onClose: () => void
  onAdded?: (libraryId: number) => void
}) {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const series = mediaType === 'series'
  const options = useQuery({ queryKey: ['requests', 'options', mediaType], queryFn: () => requests.options(mediaType) })
  const movie = useQuery({ queryKey: ['discover', 'movie', String(tmdbId)], queryFn: () => discover.movie(tmdbId as number), enabled: !series && tmdbId !== null })
  const show = useQuery({ queryKey: ['discover', 'tv', String(tmdbId)], queryFn: () => discover.show(tmdbId as number), enabled: series && tmdbId !== null })
  const title = series ? show.data : movie.data

  const [fourK, setFourK] = useState(false)
  const [versionName, setVersionName] = useState('')
  const [profileId, setProfileId] = useState<number | ''>('')
  const [rootId, setRootId] = useState<number | ''>('')
  const [monitored, setMonitored] = useState(true)
  const [searchOnAdd, setSearchOnAdd] = useState(!onAdded)
  const [availability, setAvailability] = useState<MinimumAvailability>('released')
  const [monitor, setMonitor] = useState<MonitoringOption | typeof PICKED>('all')
  const [seasons, setSeasons] = useState<number[] | null>(null)
  const [seriesType, setSeriesType] = useState<SeriesType | typeof AUTO>(AUTO)
  const [seasonFolder, setSeasonFolder] = useState(true)
  const [monitorNew, setMonitorNew] = useState(true)

  const o = options.data
  const slot = fourK ? 'fourK' : 'standard'
  const profile = profileId === '' ? ((fourK ? o?.fourKProfileId : o?.standardProfileId) ?? '') : profileId
  const root = rootId === '' ? ((fourK ? o?.fourKRootFolderId : o?.standardRootFolderId) ?? '') : rootId
  const named = versionName.trim()
  // A version the admin names is its own slot; the slot buttons only say which default name it takes.
  const standardTaken = title && !series && !named ? takenReason(t, title.availability, title.requests, 'standard') : null
  const fourKTaken = title && !series && !named ? takenReason(t, title.availability, title.requests, 'fourK') : null
  // A film whose standard slot is taken opens on 4K when that one is free; with both taken, a version name is needed.
  useEffect(() => {
    if (standardTaken && !fourKTaken && o?.fourKVersion) setFourK(true)
  }, [standardTaken, fourKTaken, o?.fourKVersion])
  const needsName = !named && !!standardTaken && (!!fourKTaken || !o?.fourKVersion)
  const choices = useMemo(() => (show.data ? seasonChoices(t, show.data.seasons, slot).map((c) => (named ? { ...c, taken: null } : c)) : []), [show.data, slot, named, t])
  const picked = seasons ?? choices.filter((c) => !c.taken && c.seasonNumber > 0).map((c) => c.seasonNumber)
  const placeholder = fourK ? (o?.fourKVersion ?? '') : t('requests.add.standardName')

  const add = useMutation({
    mutationFn: () =>
      requests.add({
        mediaType,
        tmdbId,
        tvdbId: tvdbId ?? null,
        fourK,
        seasons: series && monitor === PICKED ? picked : null,
        add: {
          qualityProfileId: profile === '' ? null : profile,
          rootFolderId: root === '' ? null : root,
          versionName: named ? slugify(named) : null,
          monitored,
          searchOnAdd: monitored && searchOnAdd,
          minimumAvailability: availability,
          seriesType: seriesType === AUTO ? 'standard' : seriesType,
          suggestedSeriesType: seriesType === AUTO,
          monitor: monitor === PICKED ? 'all' : monitor,
          monitorPickedSeasons: monitor === PICKED,
          seasonFolder,
          monitorNewItems: monitorNew ? 'all' : 'none',
        },
      }),
    onSuccess: async (done) => {
      await Promise.all([queryClient.invalidateQueries({ queryKey: ['discover'] }), queryClient.invalidateQueries({ queryKey: ['requests'] }), queryClient.invalidateQueries({ queryKey: ['series'] })])
      if (done.state === 'failed' || done.libraryId == null) {
        toast.error(t('requests.add.failed', { title: done.title }))
        onClose()
        if (onAdded) return
        void navigate({ to: '/requests', search: { filter: 'failed' } })
        return
      }

      toast.success(t('requests.add.done', { title: done.title }))
      if (onAdded) {
        onAdded(done.libraryId)
        return
      }
      onClose()
      void (series ? navigate({ to: '/series/$seriesId', params: { seriesId: String(done.libraryId) } }) : navigate({ to: '/movies/$movieId', params: { movieId: String(done.libraryId) } }))
    },
  })

  const head = title ? { posterUrl: title.posterUrl, title: title.title, year: title.year, overview: title.overview } : preset
  if (!head) return null
  const noRoot = o !== undefined && o.rootFolders.length === 0

  return (
    <TitleForm
      kind={series ? 'series' : 'movie'}
      posterUrl={head.posterUrl}
      title={head.title}
      year={head.year}
      overview={head.overview}
      slot={o?.fourKVersion ? { fourK, onChange: setFourK, standardAllowed: true, fourKAllowed: true, standardTaken, fourKTaken } : undefined}
      seasons={series && monitor === PICKED && choices.length > 0 ? { choices, value: picked, onChange: setSeasons } : undefined}
      targets={{
        profiles: o?.profiles,
        roots: o?.rootFolders,
        profileId: profile,
        onProfile: setProfileId,
        rootId: root,
        onRoot: setRootId,
        notice: noRoot && (
          <Alert variant="destructive">
            <AlertDescription>
              <Trans i18nKey={series ? 'addSeries.noRootFolder' : 'addMovie.noRootFolder'} components={{ link: <Link to="/settings/library" className="underline underline-offset-4" /> }} />
            </AlertDescription>
          </Alert>
        ),
      }}
      onCancel={onClose}
      onSubmit={() => add.mutate()}
      submitLabel={t('addMovie.submit')}
      submitTestId="admin-add-submit"
      submitDisabled={!o || noRoot || (!named && (fourK ? !!fourKTaken : !!standardTaken))}
      pending={add.isPending}
      error={add.error}
    >
      <div className="grid gap-4 sm:grid-cols-2">
        <VersionNameField id="add-version-name" value={versionName} onChange={setVersionName} placeholder={placeholder} />
        {series ? (
          <FormField id="add-monitor" label={t('addSeries.monitor')} hint={t('addSeries.monitorHint')}>
            <Select value={monitor} onValueChange={(v) => setMonitor(v as MonitoringOption | typeof PICKED)}>
              <SelectTrigger id="add-monitor" className="w-full" data-testid="add-monitor">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {tmdbId !== null && <SelectItem value={PICKED}>{t('requests.add.pickedSeasons')}</SelectItem>}
                {monitoringOptions.map((option) => (
                  <SelectItem key={option} value={option}>
                    {t(`monitoringOption.${option}`)}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </FormField>
        ) : (
          <FormField id="add-availability" label={t('addMovie.minimumAvailability')}>
            <Select value={availability} onValueChange={(v) => setAvailability(v as MinimumAvailability)}>
              <SelectTrigger id="add-availability" className="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {minimumAvailabilities.map((a) => (
                  <SelectItem key={a} value={a}>
                    {t(`availability.${a}`)}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </FormField>
        )}
        {series && (
          <FormField id="add-series-type" label={t('addSeries.seriesType')} hint={t('addSeries.seriesTypeHint')}>
            <Select value={seriesType} onValueChange={(v) => setSeriesType(v as SeriesType | typeof AUTO)}>
              <SelectTrigger id="add-series-type" className="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={AUTO}>{t('requests.add.suggestedType')}</SelectItem>
                {seriesTypes.map((type) => (
                  <SelectItem key={type} value={type}>
                    {t(`seriesType.${type}`)}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </FormField>
        )}
      </div>

      <div className="flex flex-col gap-3">
        <Check id="add-monitored" label={t('addMovie.monitor')} checked={monitored} onChange={setMonitored} />
        <Check id="add-search" label={t('addMovie.searchOnAdd')} hint={t('requests.add.searchHint')} checked={monitored && searchOnAdd} disabled={!monitored} onChange={setSearchOnAdd} />
        {series && (
          <>
            <Check id="add-season-folder" label={t('addSeries.seasonFolder')} checked={seasonFolder} onChange={setSeasonFolder} />
            <Check id="add-monitor-new" label={t('addSeries.monitorNewItems')} checked={monitorNew} onChange={setMonitorNew} />
          </>
        )}
      </div>
      {needsName && <p className="text-xs text-muted-foreground">{t('requests.add.needsName')}</p>}
      <p className="text-xs text-muted-foreground">{t('requests.add.viaRequest')}</p>
    </TitleForm>
  )
}

function Check({ id, label, hint, checked, disabled, onChange }: { id: string; label: string; hint?: string; checked: boolean; disabled?: boolean; onChange: (on: boolean) => void }) {
  return (
    <div className="flex items-start gap-2">
      <Checkbox id={id} checked={checked} disabled={disabled} onCheckedChange={(v) => onChange(v === true)} className="mt-0.5" data-testid={id} />
      <div className="grid gap-0.5">
        <Label htmlFor={id} className="font-normal">
          {label}
        </Label>
        {hint && <span className="text-xs text-muted-foreground">{hint}</span>}
      </div>
    </div>
  )
}
