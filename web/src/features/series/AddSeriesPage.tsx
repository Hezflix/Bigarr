import { useEffect, useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate } from '@tanstack/react-router'
import { Loader2Icon, SearchIcon } from 'lucide-react'
import { Trans, useTranslation } from 'react-i18next'
import {
  ApiError,
  monitoringOptions,
  quality,
  rootFolders,
  rootsOfKind,
  series as seriesApi,
  seriesTypes,
  seriesVersionName,
  type MonitoringOption,
  type SeriesLookupResult,
  type SeriesType,
  profilesFor,
} from '@/api/client'
import { Poster } from '@/components/Poster'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardFooter, CardHeader, CardTitle } from '@/components/ui/card'
import { Checkbox } from '@/components/ui/checkbox'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'

const resultKey = (r: SeriesLookupResult) => `${r.tvdbId ?? ''}-${r.tmdbId ?? ''}`

export function AddSeriesPage() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const [term, setTerm] = useState('')
  const [debounced, setDebounced] = useState('')
  const [selected, setSelected] = useState<SeriesLookupResult | null>(null)

  useEffect(() => {
    const handle = setTimeout(() => setDebounced(term.trim()), 400)
    return () => clearTimeout(handle)
  }, [term])

  const results = useQuery({
    queryKey: ['seriesLookup', debounced],
    queryFn: () => seriesApi.lookup(debounced),
    enabled: debounced.length >= 2,
  })

  return (
    <>
      <h1 className="text-2xl font-semibold tracking-tight">{t('addSeries.title')}</h1>

      <div className="relative max-w-xl">
        <SearchIcon className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground" />
        <Input
          data-testid="series-lookup-input"
          className="pl-9"
          placeholder={t('addSeries.placeholder')}
          value={term}
          onChange={(e) => {
            setTerm(e.target.value)
            setSelected(null)
          }}
          autoFocus
        />
        {results.isFetching && <Loader2Icon className="absolute top-1/2 right-3 size-4 -translate-y-1/2 animate-spin text-muted-foreground" />}
      </div>

      {results.isFetching && <p className="text-sm text-muted-foreground">{t('lookup.searching')}</p>}
      {results.error && (
        <Alert variant="destructive" className="max-w-xl">
          <AlertDescription>{t('lookup.failed')}</AlertDescription>
        </Alert>
      )}

      {!selected && results.data && (
        <div className="flex flex-col gap-3">
          {results.data.length === 0 && <p className="text-sm text-muted-foreground">{t('lookup.noResults')}</p>}
          {results.data.map((r) => (
            <Card key={resultKey(r)} data-testid="series-lookup-result" className="py-4">
              <CardContent className="flex items-start gap-4">
                <Poster url={r.posterUrl} title={r.title} size="small" />
                <div className="flex min-w-0 flex-1 flex-col gap-1">
                  <div className="flex flex-wrap items-baseline gap-2">
                    <span className="font-medium">{r.title}</span>
                    {r.year && <span className="text-sm text-muted-foreground">({r.year})</span>}
                    {r.suggestedType !== 'standard' && <Badge variant="secondary">{t(`seriesType.${r.suggestedType}`)}</Badge>}
                  </div>
                  <span className="text-sm text-muted-foreground">
                    {[r.network, r.originalTitle && r.originalTitle !== r.title ? r.originalTitle : null, r.tvdbId ? `TVDB ${r.tvdbId}` : null].filter(Boolean).join(' · ')}
                  </span>
                  {r.overview && <p className="line-clamp-3 text-sm text-muted-foreground">{r.overview}</p>}
                </div>
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
              </CardContent>
            </Card>
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
  const profiles = useQuery({ queryKey: ['qualityProfiles'], queryFn: quality.profiles, select: profilesFor('series') })
  const roots = useQuery({ queryKey: ['rootFolders'], queryFn: rootFolders.list, select: rootsOfKind('series') })
  const [profileId, setProfileId] = useState<number | ''>('')
  const [rootId, setRootId] = useState<number | ''>('')
  const [versionName, setVersionName] = useState('')
  const [seriesType, setSeriesType] = useState<SeriesType>(show.suggestedType)
  const [monitor, setMonitor] = useState<MonitoringOption>('all')
  const [seasonFolder, setSeasonFolder] = useState(true)
  const [monitorNew, setMonitorNew] = useState(true)

  useEffect(() => {
    if (profileId === '' && profiles.data?.length) setProfileId(profiles.data[0].id)
  }, [profiles.data, profileId])
  useEffect(() => {
    if (rootId === '' && roots.data?.length) setRootId(roots.data[0].id)
  }, [roots.data, rootId])

  const profile = profiles.data?.find((p) => p.id === profileId)
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

  const noRoot = roots.data && roots.data.length === 0

  return (
    <Card className="max-w-3xl">
      <form onSubmit={submit}>
        <CardHeader className="grid-cols-[auto_1fr] gap-4">
          <Poster url={show.posterUrl} title={show.title} size="small" className="row-span-2" />
          <CardTitle className="flex flex-wrap items-baseline gap-2 text-lg">
            {show.title}
            {show.year && <span className="text-sm font-normal text-muted-foreground">({show.year})</span>}
          </CardTitle>
          {show.overview && <p className="line-clamp-3 text-sm text-muted-foreground">{show.overview}</p>}
        </CardHeader>

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

            <div className="grid grid-cols-1 gap-2">
              <Label htmlFor="series-profile">{t('addMovie.qualityProfile')}</Label>
              <Select value={profileId === '' ? undefined : String(profileId)} onValueChange={(value) => setProfileId(Number(value))}>
                <SelectTrigger id="series-profile" className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {profiles.data?.map((p) => (
                    <SelectItem key={p.id} value={String(p.id)}>
                      {p.name}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>

            <div className="grid grid-cols-1 gap-2">
              <Label htmlFor="series-root">{t('addMovie.rootFolder')}</Label>
              <Select value={rootId === '' ? undefined : String(rootId)} onValueChange={(value) => setRootId(Number(value))}>
                <SelectTrigger id="series-root" className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {roots.data?.map((r) => (
                    <SelectItem key={r.id} value={String(r.id)}>
                      {r.path}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>

            <div className="grid grid-cols-1 gap-2">
              <Label htmlFor="series-version-name">{t('addMovie.versionName')}</Label>
              <Input id="series-version-name" value={versionName} onChange={(e) => setVersionName(e.target.value)} placeholder={effectiveName} />
              <p className="text-xs text-muted-foreground">{t('addMovie.versionNameHint')}</p>
            </div>
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

          {add.error && (
            <Alert variant="destructive">
              <AlertDescription>{add.error instanceof ApiError ? add.error.message : t('common.unexpectedError')}</AlertDescription>
            </Alert>
          )}
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
