import { useEffect, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useSearch } from '@tanstack/react-router'
import { Trans, useTranslation } from 'react-i18next'
import {
  monitoringOptions,
  series as seriesApi,
  seriesTypes,
  seriesVersionName,
  type MonitoringOption,
  type SeriesLookupResult,
  type SeriesType,
} from '@/api/client'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { FormField, LookupResultCard, LookupSearch, useAddTargets, VersionNameField } from '@/features/library/addParts'
import { AddKindSwitch } from '@/features/library/AddKindSwitch'
import { TitleForm } from '@/features/library/TitleForm'
import { useDebounced } from '@/hooks/useDebounced'

const resultKey = (r: SeriesLookupResult) => `${r.tvdbId ?? ''}-${r.tmdbId ?? ''}`

export function AddSeriesPage() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const { term: askedTerm } = useSearch({ strict: false })
  const [term, setTerm] = useState(askedTerm ?? '')
  const [selected, setSelected] = useState<SeriesLookupResult | null>(null)
  // The palette's "Add '…'" entry can land here while the page is already open.
  useEffect(() => {
    if (askedTerm) {
      setTerm(askedTerm)
      setSelected(null)
    }
  }, [askedTerm])

  const debounced = useDebounced(term.trim(), 400)

  const results = useQuery({
    queryKey: ['seriesLookup', debounced],
    queryFn: () => seriesApi.lookup(debounced),
    enabled: debounced.length >= 2,
  })

  return (
    <>
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-semibold tracking-tight">{t('addSeries.title')}</h1>
        <AddKindSwitch kind="series" />
      </div>

      <LookupSearch
        testId="series-lookup-input"
        placeholder={t('addSeries.placeholder')}
        value={term}
        onChange={(value) => {
          setTerm(value)
          setSelected(null)
        }}
        fetching={results.isFetching}
        failed={!!results.error}
      />

      {results.data && (
        <div className="flex flex-col gap-3">
          {results.data.length === 0 && <p className="text-sm text-muted-foreground">{t('lookup.noResults')}</p>}
          {results.data.map((r) => (
            <LookupResultCard
              key={resultKey(r)}
              testId="series-lookup-result"
              posterUrl={r.posterUrl}
              title={r.title}
              year={r.year}
              badge={r.suggestedType !== 'standard' && <Badge variant="secondary">{t(`seriesType.${r.suggestedType}`)}</Badge>}
              subtitle={
                <span className="text-sm text-muted-foreground">
                  {[r.network, r.originalTitle && r.originalTitle !== r.title ? r.originalTitle : null, r.tvdbId ? `TVDB ${r.tvdbId}` : null].filter(Boolean).join(' · ')}
                </span>
              }
              overview={r.overview}
            >
              {r.existingSeriesId ? (
                <Button asChild variant="outline">
                  <Link to="/series/$seriesId" params={{ seriesId: String(r.existingSeriesId) }}>
                    {t('lookup.alreadyInLibrary')}
                  </Link>
                </Button>
              ) : (
                <Button type="button" onClick={() => setSelected(r)}>
                  {t('lookup.choose')}
                </Button>
              )}
            </LookupResultCard>
          ))}
        </div>
      )}

      {selected && (
        <AddForm
          show={selected}
          onCancel={() => setSelected(null)}
          onAdded={(id) => navigate({ to: '/series/$seriesId', params: { seriesId: String(id) } })}
        />
      )}
    </>
  )
}

function AddForm({ show, onCancel, onAdded }: { show: SeriesLookupResult; onCancel: () => void; onAdded: (id: number) => void }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const { profiles, roots, profileId, setProfileId, rootId, setRootId, profile, noRoot } = useAddTargets('series')
  const [versionName, setVersionName] = useState('')
  const [seriesType, setSeriesType] = useState<SeriesType>(show.suggestedType)
  const [monitor, setMonitor] = useState<MonitoringOption>('all')
  const [seasonFolder, setSeasonFolder] = useState(true)
  const [monitorNew, setMonitorNew] = useState(true)

  const effectiveName = versionName.trim() || (profile ? seriesVersionName(profile.name) : '')

  const add = useMutation({
    mutationFn: () =>
      seriesApi.add({
        tvdbId: show.tvdbId,
        tmdbId: show.tmdbId,
        qualityProfileId: Number(profileId),
        rootFolderId: Number(rootId),
        versionName: versionName.trim() || null,
        seriesType,
        monitor,
        seasonFolder,
        monitorNewItems: monitorNew ? 'all' : 'none',
      }),
    onSuccess: (created) => {
      void queryClient.invalidateQueries({ queryKey: ['series'] })
      onAdded(created.id)
    },
  })

  return (
    <TitleForm
      kind="series"
      posterUrl={show.posterUrl}
      title={show.title}
      year={show.year}
      overview={show.overview}
      targets={{
        profiles,
        roots: roots?.map((r) => ({ id: r.id, name: r.path })),
        profileId,
        onProfile: setProfileId,
        rootId,
        onRoot: setRootId,
        notice: (
          <>
            {noRoot && (
              <Alert variant="destructive">
                <AlertDescription>
                  <Trans i18nKey="addSeries.noRootFolder" components={{ link: <Link to="/settings/library" className="underline underline-offset-4" /> }} />
                </AlertDescription>
              </Alert>
            )}
            {show.tvdbId === null && (
              <Alert>
                <AlertDescription>{t('addSeries.noTvdb')}</AlertDescription>
              </Alert>
            )}
          </>
        ),
      }}
      onCancel={onCancel}
      onSubmit={() => add.mutate()}
      submitLabel={t('addSeries.submit')}
      submitTestId="series-add-submit"
      submitDisabled={noRoot || profileId === '' || rootId === ''}
      pending={add.isPending}
      error={add.error}
    >
      <div className="grid gap-4 sm:grid-cols-2">
        <FormField id="series-monitor" label={t('addSeries.monitor')} hint={t('addSeries.monitorHint')}>
          <Select value={monitor} onValueChange={(value) => setMonitor(value as MonitoringOption)}>
            <SelectTrigger id="series-monitor" className="w-full" data-testid="series-monitor">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {monitoringOptions.map((option) => (
                <SelectItem key={option} value={option}>
                  {t(`monitoringOption.${option}`)}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </FormField>

        <FormField id="series-type" label={t('addSeries.seriesType')} hint={t('addSeries.seriesTypeHint')}>
          <Select value={seriesType} onValueChange={(value) => setSeriesType(value as SeriesType)}>
            <SelectTrigger id="series-type" className="w-full">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {seriesTypes.map((type) => (
                <SelectItem key={type} value={type}>
                  {t(`seriesType.${type}`)}
                  {type === show.suggestedType && <span className="text-muted-foreground"> ({t('addSeries.suggested')})</span>}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </FormField>

        <VersionNameField id="series-version-name" value={versionName} onChange={setVersionName} placeholder={effectiveName} />
      </div>

      <div className="flex flex-col gap-3">
        <div className="flex items-center gap-2">
          <Checkbox id="series-season-folder" checked={seasonFolder} onCheckedChange={(checked) => setSeasonFolder(checked === true)} />
          <Label htmlFor="series-season-folder" className="font-normal">
            {t('addSeries.seasonFolder')}
          </Label>
        </div>
        <div className="flex items-center gap-2">
          <Checkbox id="series-monitor-new" checked={monitorNew} onCheckedChange={(checked) => setMonitorNew(checked === true)} />
          <Label htmlFor="series-monitor-new" className="font-normal">
            {t('addSeries.monitorNewItems')}
          </Label>
        </div>
      </div>

    </TitleForm>
  )
}
