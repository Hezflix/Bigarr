import { useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate } from '@tanstack/react-router'
import { Loader2Icon } from 'lucide-react'
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
import { Card, CardContent, CardFooter } from '@/components/ui/card'
import { Checkbox } from '@/components/ui/checkbox'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { AddError, AddFormHeader, IdSelectField, LookupResultCard, LookupSearch, useAddTargets, VersionNameField } from '@/features/library/addParts'
import { useDebounced } from '@/hooks/useDebounced'

const resultKey = (r: SeriesLookupResult) => `${r.tvdbId ?? ''}-${r.tmdbId ?? ''}`

export function AddSeriesPage() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const [term, setTerm] = useState('')
  const [selected, setSelected] = useState<SeriesLookupResult | null>(null)

  const debounced = useDebounced(term.trim(), 400)

  const results = useQuery({
    queryKey: ['seriesLookup', debounced],
    queryFn: () => seriesApi.lookup(debounced),
    enabled: debounced.length >= 2,
  })

  return (
    <>
      <h1 className="text-2xl font-semibold tracking-tight">{t('addSeries.title')}</h1>

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

      {!selected && results.data && (
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

  const submit = (e: FormEvent) => {
    e.preventDefault()
    add.mutate()
  }

  return (
    <Card className="max-w-3xl">
      <form onSubmit={submit}>
        <AddFormHeader posterUrl={show.posterUrl} title={show.title} year={show.year} overview={show.overview} />

        <CardContent className="mt-6 flex flex-col gap-4">
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

          <div className="grid gap-4 sm:grid-cols-2">
            <div className="grid grid-cols-1 gap-2">
              <Label htmlFor="series-monitor">{t('addSeries.monitor')}</Label>
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
              <p className="text-xs text-muted-foreground">{t('addSeries.monitorHint')}</p>
            </div>

            <div className="grid grid-cols-1 gap-2">
              <Label htmlFor="series-type">{t('addSeries.seriesType')}</Label>
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
              <p className="text-xs text-muted-foreground">{t('addSeries.seriesTypeHint')}</p>
            </div>

            <IdSelectField id="series-profile" label={t('addMovie.qualityProfile')} value={profileId} onChange={setProfileId} options={profiles?.map((p) => ({ id: p.id, label: p.name }))} />

            <IdSelectField id="series-root" label={t('addMovie.rootFolder')} value={rootId} onChange={setRootId} options={roots?.map((r) => ({ id: r.id, label: r.path }))} />

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

          <AddError error={add.error} />
        </CardContent>

        <CardFooter className="mt-6 justify-end gap-2">
          <Button type="button" variant="ghost" onClick={onCancel}>
            {t('common.cancel')}
          </Button>
          <Button type="submit" data-testid="series-add-submit" disabled={add.isPending || noRoot || profileId === '' || rootId === ''}>
            {add.isPending && <Loader2Icon className="animate-spin" />}
            {t('addSeries.submit')}
          </Button>
        </CardFooter>
      </form>
    </Card>
  )
}
