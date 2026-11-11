import { useEffect, useMemo, useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ApiError, sonarrMigration, type SonarrImportPreview, type SonarrImportRequest, type SonarrImportStatus } from '@/api/client'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Progress } from '@/components/ui/progress'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import {
  mappingComplete,
  ProfileMappingTable,
  RootFolderMappingTable,
  slugPattern,
  sonarrMappingPreview,
  suggestedMapping,
  Toggle,
  type MappingLabels,
} from '@/features/settings/RadarrMapping'

type Step = 'connect' | 'map' | 'run'

const isBusy = (status: SonarrImportStatus | undefined) => status?.state === 'queued' || status?.state === 'running'

export const useSonarrLabels = (): MappingLabels => {
  const { t } = useTranslation()
  return { profile: t('importSonarr.sonarrProfile'), count: t('importSonarr.series'), path: t('importSonarr.sonarrPath') }
}

/** Moving in from an existing Sonarr (S4): connect, map profiles and series root folders, follow the import. */
export function ImportSonarrPage() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [step, setStep] = useState<Step | null>(null)
  const [url, setUrl] = useState('')
  const [apiKey, setApiKey] = useState('')
  const [preview, setPreview] = useState<SonarrImportPreview | null>(null)

  const status = useQuery({
    queryKey: ['sonarrImport'],
    queryFn: sonarrMigration.status,
    refetchInterval: (query) => (isBusy(query.state.data) ? 1000 : false),
  })

  useEffect(() => {
    if (step !== null || !status.data) return
    setStep(status.data.state === 'idle' ? 'connect' : 'run')
  }, [step, status.data])

  const importState = status.data?.state
  useEffect(() => {
    if (importState !== 'completed') return
    for (const key of [['series'], ['rootFolders'], ['tags'], ['shadow']]) {
      void queryClient.invalidateQueries({ queryKey: key })
    }
  }, [importState, queryClient])

  const previewMutation = useMutation({
    mutationFn: () => sonarrMigration.preview({ url: url.trim(), apiKey: apiKey.trim() }),
    onSuccess: (result) => {
      setPreview(result)
      setStep('map')
    },
  })

  const submitConnect = (e: FormEvent) => {
    e.preventDefault()
    previewMutation.mutate()
  }

  return (
    <>
      <h1 className="text-2xl font-semibold tracking-tight">{t('importSonarr.title')}</h1>
      <p className="max-w-3xl text-sm text-muted-foreground">{t('importSonarr.intro')}</p>

      {(step === 'connect' || step === null) && (
        <form className="flex max-w-xl flex-col gap-4" onSubmit={submitConnect} data-testid="sonarr-connect-form">
          <div className="grid grid-cols-1 gap-2">
            <Label htmlFor="sonarr-url">{t('importSonarr.url')}</Label>
            <Input id="sonarr-url" value={url} onChange={(e) => setUrl(e.target.value)} placeholder="http://sonarr:8989" autoFocus />
            <p className="text-xs text-muted-foreground">{t('importSonarr.urlHint')}</p>
          </div>
          <div className="grid grid-cols-1 gap-2">
            <Label htmlFor="sonarr-key">{t('importRadarr.apiKey')}</Label>
            <Input id="sonarr-key" type="password" value={apiKey} onChange={(e) => setApiKey(e.target.value)} autoComplete="off" />
            <p className="text-xs text-muted-foreground">{t('importSonarr.apiKeyHint')}</p>
          </div>
          {previewMutation.error && (
            <Alert variant="destructive">
              <AlertDescription>{previewMutation.error instanceof ApiError ? previewMutation.error.message : t('importSonarr.previewFailed')}</AlertDescription>
            </Alert>
          )}
          <div>
            <Button type="submit" disabled={previewMutation.isPending || url.trim().length === 0 || apiKey.trim().length === 0} data-testid="sonarr-preview">
              {previewMutation.isPending ? t('importSonarr.previewing') : t('importRadarr.preview')}
            </Button>
          </div>
        </form>
      )}

      {step === 'map' && preview && (
        <MappingStep
          preview={preview}
          url={url.trim()}
          apiKey={apiKey.trim()}
          onBack={() => setStep('connect')}
          onStarted={() => {
            void queryClient.invalidateQueries({ queryKey: ['sonarrImport'] })
            setStep('run')
          }}
        />
      )}

      {step === 'run' && status.data && (
        <RunStep
          status={status.data}
          onAgain={() => {
            setPreview(null)
            setStep('connect')
          }}
        />
      )}
    </>
  )
}

function MappingStep({ preview, url, apiKey, onBack, onStarted }: { preview: SonarrImportPreview; url: string; apiKey: string; onBack: () => void; onStarted: () => void }) {
  const { t } = useTranslation()
  const labels = useSonarrLabels()
  const shaped = useMemo(() => sonarrMappingPreview(preview), [preview])
  const [versionName, setVersionName] = useState(preview.suggestedVersionName)
  const [profileMap, setProfileMap] = useState<Record<number, number>>(() => suggestedMapping(shaped).profileMap)
  const [rootFolderMap, setRootFolderMap] = useState<Record<string, number>>(() => suggestedMapping(shaped).rootFolderMap)
  const [importTags, setImportTags] = useState(true)
  const [importExclusions, setImportExclusions] = useState(true)
  const [importHistory, setImportHistory] = useState(true)
  const [importBlocklist, setImportBlocklist] = useState(true)
  const [importProfileQualities, setImportProfileQualities] = useState(true)
  const [importCustomFormats, setImportCustomFormats] = useState(true)
  const [importNaming, setImportNaming] = useState(true)
  const [keepMonitored, setKeepMonitored] = useState(true)
  const [follow, setFollow] = useState(false)

  const versionValid = slugPattern.test(versionName.trim())
  const mapped = useMemo(() => mappingComplete(shaped, { profileMap, rootFolderMap }), [shaped, profileMap, rootFolderMap])
  const canStart = versionValid && mapped

  const start = useMutation({
    mutationFn: () => {
      const body: SonarrImportRequest = {
        url,
        apiKey,
        versionName: versionName.trim(),
        profileMap,
        rootFolderMap,
        importTags,
        importExclusions,
        importHistory,
        importBlocklist,
        importProfileQualities,
        importCustomFormats,
        importNaming,
        keepMonitored,
        follow,
      }
      return sonarrMigration.start(body)
    },
    onSuccess: onStarted,
    onError: (e) => toast.error(e instanceof ApiError ? e.message : t('importRadarr.startFailed')),
  })

  const usedTags = preview.tags.filter((tag) => tag.movies > 0)
  const types = Object.entries(preview.seriesTypes)
    .map(([type, count]) => `${count} ${type}`)
    .join(', ')

  return (
    <div className="flex flex-col gap-6" data-testid="sonarr-mapping">
      <p className="text-sm">
        {t('importSonarr.summary', {
          version: preview.sonarrVersion,
          series: preview.series,
          types,
          episodes: preview.episodes,
          files: preview.episodeFiles,
          monitored: preview.monitored,
          already: preview.alreadyInLibrary,
          exclusions: preview.exclusions,
          history: preview.historyRecords,
          blocklist: preview.blocklist,
        })}
      </p>
      {preview.languageProfiles.length > 0 && <p className="max-w-3xl text-xs text-muted-foreground">{t('importSonarr.languageProfiles', { names: preview.languageProfiles.join(', ') })}</p>}

      <section className="flex max-w-xl flex-col gap-2">
        <Label htmlFor="sonarr-version-name">{t('importRadarr.versionName')}</Label>
        <Input id="sonarr-version-name" value={versionName} onChange={(e) => setVersionName(e.target.value)} aria-invalid={!versionValid} />
        <p className="text-xs text-muted-foreground">{t('importSonarr.versionNameHint')}</p>
        {!versionValid && <p className="text-xs text-destructive">{t('importRadarr.versionNameInvalid')}</p>}
      </section>

      <section className="flex flex-col gap-3">
        <h2 className="text-xl font-semibold tracking-tight">{t('importRadarr.profiles')}</h2>
        <ProfileMappingTable preview={shaped} value={profileMap} onChange={setProfileMap} labels={labels} kind="series" allowCreate />
        <p className="max-w-3xl text-xs text-muted-foreground">{t('importSonarr.profilesHint')}</p>
      </section>

      <section className="flex flex-col gap-3">
        <h2 className="text-xl font-semibold tracking-tight">{t('importSonarr.customFormats')}</h2>
        <p className="max-w-3xl text-xs text-muted-foreground">{t('importSonarr.customFormatsHint')}</p>
        {(preview.customFormats ?? []).length === 0 ? (
          <p className="text-sm text-muted-foreground">{t('importSonarr.noFormats')}</p>
        ) : (
          <div className="overflow-x-auto rounded-lg border">
            <Table data-testid="sonarr-formats">
              <TableHeader>
                <TableRow>
                  <TableHead>{t('importSonarr.formatSonarr')}</TableHead>
                  <TableHead>{t('importSonarr.formatOutcome')}</TableHead>
                  <TableHead>{t('importSonarr.formatBigarr')}</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {preview.customFormats!.map((f) => (
                  <TableRow key={f.sonarrId}>
                    <TableCell className="font-medium">{f.sonarrName}</TableCell>
                    <TableCell>
                      <Badge variant={f.outcome === 'reused' ? 'secondary' : 'outline'}>{t(`importSonarr.formatOutcomes.${f.outcome}`)}</Badge>
                    </TableCell>
                    <TableCell className="whitespace-normal">
                      {f.bigarrName}
                      {f.notes.map((note) => (
                        <span key={note} className="block text-xs text-amber-700 dark:text-amber-300">
                          {note}
                        </span>
                      ))}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        )}
        <Toggle id="sonarr-import-custom-formats" label={t('importSonarr.importCustomFormats')} checked={importCustomFormats} onChange={setImportCustomFormats} />
        <Toggle id="sonarr-import-naming" label={t('importSonarr.importNaming')} checked={importNaming} onChange={setImportNaming} />
      </section>

      <section className="flex flex-col gap-3">
        <h2 className="text-xl font-semibold tracking-tight">{t('importRadarr.rootFolders')}</h2>
        <RootFolderMappingTable preview={shaped} value={rootFolderMap} onChange={setRootFolderMap} labels={labels} kind="series" />
        <p className="max-w-3xl text-xs text-muted-foreground">
          {t('importSonarr.rootFoldersHint')}{' '}
          <Link to="/settings/library" className="underline underline-offset-4">
            {t('nav.settingsLibrary')}
          </Link>
        </p>
      </section>

      <section className="flex flex-col gap-3">
        <h2 className="text-xl font-semibold tracking-tight">{t('importRadarr.tags')}</h2>
        {usedTags.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t('importSonarr.noTags')}</p>
        ) : (
          <div className="flex flex-wrap gap-2">
            {usedTags.map((tag) => (
              <Badge key={tag.id} variant="secondary">
                {tag.label} · {tag.movies}
              </Badge>
            ))}
          </div>
        )}
        <Toggle id="sonarr-import-tags" label={t('importRadarr.importTags')} checked={importTags} onChange={setImportTags} disabled={usedTags.length === 0} />
      </section>

      <section className="flex flex-col gap-3">
        <h2 className="text-xl font-semibold tracking-tight">{t('importRadarr.options')}</h2>
        <Toggle id="sonarr-import-exclusions" label={t('importRadarr.importExclusions')} checked={importExclusions} onChange={setImportExclusions} disabled={preview.exclusions === 0} />
        <Toggle id="sonarr-import-profile-qualities" label={t('importSonarr.importProfileQualities')} checked={importProfileQualities} onChange={setImportProfileQualities} />
        <Toggle id="sonarr-import-history" label={t('importSonarr.importHistory')} checked={importHistory} onChange={setImportHistory} disabled={preview.historyRecords === 0} />
        <Toggle id="sonarr-import-blocklist" label={t('importSonarr.importBlocklist')} checked={importBlocklist} onChange={setImportBlocklist} disabled={preview.blocklist === 0} />
        <Toggle id="sonarr-keep-monitored" label={t('importSonarr.keepMonitored')} checked={keepMonitored} onChange={setKeepMonitored} />
        <Toggle id="sonarr-follow" label={t('importSonarr.follow')} hint={t('importSonarr.followHint')} checked={follow} onChange={setFollow} />
      </section>

      <div className="flex flex-wrap items-center gap-3">
        <Button type="button" variant="ghost" onClick={onBack}>
          {t('importRadarr.back')}
        </Button>
        <Button type="button" onClick={() => start.mutate()} disabled={!canStart || start.isPending} data-testid="sonarr-start">
          {start.isPending ? t('importRadarr.starting') : t('importRadarr.start')}
        </Button>
        {!canStart && <span className="text-xs text-muted-foreground">{t('importSonarr.mappingIncomplete')}</span>}
      </div>
    </div>
  )
}

function RunStep({ status, onAgain }: { status: SonarrImportStatus; onAgain: () => void }) {
  const { t } = useTranslation()
  const busy = isBusy(status)
  const progress = status.total > 0 ? Math.round((status.processed / status.total) * 100) : 0

  const counters = [
    ['importSonarr.seriesAdded', status.seriesAdded],
    ['importRadarr.versionsAdded', status.versionsAdded],
    ['importRadarr.versionsUpdated', status.versionsUpdated],
    ['importSonarr.episodesAdded', status.episodesAdded],
    ['importRadarr.filesAdded', status.filesAdded],
    ['importRadarr.filesMissing', status.filesMissing],
    ['importRadarr.skipped', status.skipped],
    ['importRadarr.failed', status.failed],
    ['importRadarr.tagsAdded', status.tagsAdded],
    ['importRadarr.exclusionsAdded', status.exclusionsAdded],
    ['importRadarr.historyAdded', status.historyAdded],
    ['importRadarr.blocklistAdded', status.blocklistAdded],
    ['importSonarr.customFormatsAdded', status.customFormatsAdded ?? 0],
    ['importSonarr.customFormatsReused', status.customFormatsReused ?? 0],
    ['importSonarr.customFormatsRenamed', status.customFormatsRenamed ?? 0],
    ['importRadarr.profilesScored', status.profilesScored],
    ['importRadarr.profileQualitiesCopied', status.profileQualitiesCopied],
    ['importSonarr.namingImported', status.namingImported ? 1 : 0],
  ] as const

  return (
    <div className="flex flex-col gap-6" data-testid="sonarr-run">
      {busy && (
        <section className="flex max-w-xl flex-col gap-2">
          <h2 className="text-xl font-semibold tracking-tight">{t('importRadarr.progress')}</h2>
          <Progress value={progress} />
          <span className="text-xs text-muted-foreground">
            {status.state === 'queued' ? t('importRadarr.queued') : t('importRadarr.processedOf', { processed: status.processed, total: status.total })}
            {status.current ? ` · ${status.current}` : ''}
          </span>
        </section>
      )}

      {!busy && (
        <section className="flex flex-col gap-3">
          <h2 className="text-xl font-semibold tracking-tight">{status.state === 'failed' ? t('importRadarr.reportFailed') : t('importRadarr.report')}</h2>
          {status.error && (
            <Alert variant="destructive">
              <AlertDescription>{status.error}</AlertDescription>
            </Alert>
          )}
          <dl className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-5">
            {counters.map(([key, value]) => (
              <div key={key} className="rounded-lg border p-3">
                <dt className="text-xs text-muted-foreground">{t(key)}</dt>
                <dd className="text-lg font-semibold tabular-nums">{value}</dd>
              </div>
            ))}
          </dl>
        </section>
      )}

      <section className="flex flex-col gap-3">
        <h2 className="text-xl font-semibold tracking-tight">{t('importRadarr.issues')}</h2>
        {status.issues.length === 0 ? (
          <p className="text-sm text-muted-foreground">{busy ? '' : t('importSonarr.noIssues')}</p>
        ) : (
          <div className="overflow-x-auto rounded-lg border">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>{t('importSonarr.issueTitle')}</TableHead>
                  <TableHead>{t('importSonarr.issueTvdb')}</TableHead>
                  <TableHead>{t('importRadarr.issueReason')}</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {status.issues.map((issue, i) => (
                  <TableRow key={`${issue.tvdbId}-${i}`}>
                    <TableCell className="font-medium">
                      {issue.seriesId !== null ? (
                        <Link to="/series/$seriesId" params={{ seriesId: String(issue.seriesId) }} className="underline-offset-4 hover:underline">
                          {issue.title}
                        </Link>
                      ) : (
                        issue.title
                      )}
                    </TableCell>
                    <TableCell className="tabular-nums">{issue.tvdbId ?? t('common.none')}</TableCell>
                    <TableCell className="whitespace-normal">{issue.reason}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        )}
      </section>

      {!busy && (
        <div>
          <Button type="button" variant="outline" onClick={onAgain} data-testid="sonarr-again">
            {t('importSonarr.again')}
          </Button>
        </div>
      )}
    </div>
  )
}
