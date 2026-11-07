import { useEffect, useMemo, useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ApiError, migration, type RadarrImportPreview, type RadarrImportRequest, type RadarrImportStatus } from '@/api/client'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Progress } from '@/components/ui/progress'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { mappingComplete, ProfileMappingTable, RootFolderMappingTable, slugPattern, suggestedMapping, Toggle } from '@/features/settings/RadarrMapping'

type Step = 'connect' | 'map' | 'run'

const isBusy = (status: RadarrImportStatus | undefined) => status?.state === 'queued' || status?.state === 'running'

/** Moving in from an existing Radarr (M8): connect, map profiles and root folders, follow the import. */
export function ImportRadarrPage() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [step, setStep] = useState<Step | null>(null)
  const [url, setUrl] = useState('')
  const [apiKey, setApiKey] = useState('')
  const [preview, setPreview] = useState<RadarrImportPreview | null>(null)

  const status = useQuery({
    queryKey: ['radarrImport'],
    queryFn: migration.status,
    refetchInterval: (query) => (isBusy(query.state.data) ? 1000 : false),
  })

  // A page opened while an import runs (or just ran) shows it rather than an empty form.
  useEffect(() => {
    if (step !== null || !status.data) return
    setStep(status.data.state === 'idle' ? 'connect' : 'run')
  }, [step, status.data])

  const importState = status.data?.state
  useEffect(() => {
    if (importState !== 'completed') return
    for (const key of [['movies'], ['wanted'], ['rootFolders'], ['tags']]) {
      void queryClient.invalidateQueries({ queryKey: key })
    }
  }, [importState, queryClient])

  const previewMutation = useMutation({
    mutationFn: () => migration.preview({ url: url.trim(), apiKey: apiKey.trim() }),
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
      <h1 className="text-2xl font-semibold tracking-tight">{t('importRadarr.title')}</h1>
      <p className="max-w-3xl text-sm text-muted-foreground">{t('importRadarr.intro')}</p>

      {(step === 'connect' || step === null) && (
        <form className="flex max-w-xl flex-col gap-4" onSubmit={submitConnect} data-testid="radarr-connect-form">
          <div className="grid grid-cols-1 gap-2">
            <Label htmlFor="radarr-url">{t('importRadarr.url')}</Label>
            <Input id="radarr-url" value={url} onChange={(e) => setUrl(e.target.value)} placeholder="http://radarr:7878" autoFocus />
            <p className="text-xs text-muted-foreground">{t('importRadarr.urlHint')}</p>
          </div>
          <div className="grid grid-cols-1 gap-2">
            <Label htmlFor="radarr-key">{t('importRadarr.apiKey')}</Label>
            <Input id="radarr-key" type="password" value={apiKey} onChange={(e) => setApiKey(e.target.value)} autoComplete="off" />
            <p className="text-xs text-muted-foreground">{t('importRadarr.apiKeyHint')}</p>
          </div>
          {previewMutation.error && (
            <Alert variant="destructive">
              <AlertDescription>{previewMutation.error instanceof ApiError ? previewMutation.error.message : t('importRadarr.previewFailed')}</AlertDescription>
            </Alert>
          )}
          <div>
            <Button type="submit" disabled={previewMutation.isPending || url.trim().length === 0 || apiKey.trim().length === 0} data-testid="radarr-preview">
              {previewMutation.isPending ? t('importRadarr.previewing') : t('importRadarr.preview')}
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
            void queryClient.invalidateQueries({ queryKey: ['radarrImport'] })
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

function MappingStep({ preview, url, apiKey, onBack, onStarted }: { preview: RadarrImportPreview; url: string; apiKey: string; onBack: () => void; onStarted: () => void }) {
  const { t } = useTranslation()
  const [versionName, setVersionName] = useState(preview.suggestedVersionName)
  const [profileMap, setProfileMap] = useState<Record<number, number>>(() => suggestedMapping(preview).profileMap)
  const [rootFolderMap, setRootFolderMap] = useState<Record<string, number>>(() => suggestedMapping(preview).rootFolderMap)
  const [importTags, setImportTags] = useState(true)
  const [importExclusions, setImportExclusions] = useState(true)
  const [importHistory, setImportHistory] = useState(true)
  const [importBlocklist, setImportBlocklist] = useState(true)
  const [importCustomFormats, setImportCustomFormats] = useState(true)
  const [keepMonitored, setKeepMonitored] = useState(true)
  const [includeWithoutFile, setIncludeWithoutFile] = useState(true)
  const [follow, setFollow] = useState(false)

  const versionValid = slugPattern.test(versionName.trim())
  const mapped = useMemo(() => mappingComplete(preview, { profileMap, rootFolderMap }), [preview, profileMap, rootFolderMap])
  const canStart = versionValid && mapped

  const start = useMutation({
    mutationFn: () => {
      const body: RadarrImportRequest = {
        url,
        apiKey,
        versionName: versionName.trim(),
        profileMap,
        rootFolderMap,
        importTags,
        importExclusions,
        importHistory,
        importBlocklist,
        importCustomFormats,
        keepMonitored,
        includeMoviesWithoutFile: includeWithoutFile,
        follow,
      }
      return migration.start(body)
    },
    onSuccess: onStarted,
    onError: (e) => toast.error(e instanceof ApiError ? e.message : t('importRadarr.startFailed')),
  })

  const usedTags = preview.tags.filter((tag) => tag.movies > 0)

  return (
    <div className="flex flex-col gap-6" data-testid="radarr-mapping">
      <p className="text-sm">
        {t('importRadarr.summary', {
          version: preview.radarrVersion,
          movies: preview.movies,
          withFile: preview.withFile,
          monitored: preview.monitored,
          already: preview.alreadyInLibrary,
          exclusions: preview.exclusions,
          history: preview.historyRecords,
          blocklist: preview.blocklist,
          customFormats: preview.customFormats,
        })}
      </p>

      <section className="flex max-w-xl flex-col gap-2">
        <Label htmlFor="radarr-version-name">{t('importRadarr.versionName')}</Label>
        <Input id="radarr-version-name" value={versionName} onChange={(e) => setVersionName(e.target.value)} aria-invalid={!versionValid} />
        <p className="text-xs text-muted-foreground">{t('importRadarr.versionNameHint')}</p>
        {!versionValid && <p className="text-xs text-destructive">{t('importRadarr.versionNameInvalid')}</p>}
      </section>

      <section className="flex flex-col gap-3">
        <h2 className="text-xl font-semibold tracking-tight">{t('importRadarr.profiles')}</h2>
        <ProfileMappingTable preview={preview} value={profileMap} onChange={setProfileMap} />
      </section>

      <section className="flex flex-col gap-3">
        <h2 className="text-xl font-semibold tracking-tight">{t('importRadarr.rootFolders')}</h2>
        <RootFolderMappingTable preview={preview} value={rootFolderMap} onChange={setRootFolderMap} />
        <p className="max-w-3xl text-xs text-muted-foreground">
          {t('importRadarr.rootFoldersHint')}{' '}
          <Link to="/settings/library" className="underline underline-offset-4">
            {t('nav.settingsLibrary')}
          </Link>
        </p>
      </section>

      <section className="flex flex-col gap-3">
        <h2 className="text-xl font-semibold tracking-tight">{t('importRadarr.tags')}</h2>
        {usedTags.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t('importRadarr.noTags')}</p>
        ) : (
          <div className="flex flex-wrap gap-2">
            {usedTags.map((tag) => (
              <Badge key={tag.id} variant="secondary">
                {tag.label} · {tag.movies}
              </Badge>
            ))}
          </div>
        )}
        <Toggle id="radarr-import-tags" label={t('importRadarr.importTags')} checked={importTags} onChange={setImportTags} disabled={usedTags.length === 0} />
      </section>

      <section className="flex flex-col gap-3">
        <h2 className="text-xl font-semibold tracking-tight">{t('importRadarr.options')}</h2>
        <Toggle id="radarr-import-exclusions" label={t('importRadarr.importExclusions')} checked={importExclusions} onChange={setImportExclusions} disabled={preview.exclusions === 0} />
        <Toggle
          id="radarr-import-custom-formats"
          label={t('importRadarr.importCustomFormats')}
          checked={importCustomFormats}
          onChange={setImportCustomFormats}
          disabled={preview.customFormats === 0}
        />
        <Toggle id="radarr-import-history" label={t('importRadarr.importHistory')} checked={importHistory} onChange={setImportHistory} disabled={preview.historyRecords === 0} />
        <Toggle id="radarr-import-blocklist" label={t('importRadarr.importBlocklist')} checked={importBlocklist} onChange={setImportBlocklist} disabled={preview.blocklist === 0} />
        <Toggle id="radarr-keep-monitored" label={t('importRadarr.keepMonitored')} checked={keepMonitored} onChange={setKeepMonitored} />
        <Toggle id="radarr-include-without-file" label={t('importRadarr.includeWithoutFile')} checked={includeWithoutFile} onChange={setIncludeWithoutFile} />
        <Toggle id="radarr-follow" label={t('importRadarr.follow')} hint={t('importRadarr.followHint')} checked={follow} onChange={setFollow} />
      </section>

      <div className="flex flex-wrap items-center gap-3">
        <Button type="button" variant="ghost" onClick={onBack}>
          {t('importRadarr.back')}
        </Button>
        <Button type="button" onClick={() => start.mutate()} disabled={!canStart || start.isPending} data-testid="radarr-start">
          {start.isPending ? t('importRadarr.starting') : t('importRadarr.start')}
        </Button>
        {!canStart && <span className="text-xs text-muted-foreground">{t('importRadarr.mappingIncomplete')}</span>}
      </div>
    </div>
  )
}

function RunStep({ status, onAgain }: { status: RadarrImportStatus; onAgain: () => void }) {
  const { t } = useTranslation()
  const busy = isBusy(status)
  const progress = status.total > 0 ? Math.round((status.processed / status.total) * 100) : 0

  const counters = [
    ['importRadarr.moviesAdded', status.moviesAdded],
    ['importRadarr.versionsAdded', status.versionsAdded],
    ['importRadarr.versionsUpdated', status.versionsUpdated],
    ['importRadarr.filesAdded', status.filesAdded],
    ['importRadarr.filesMissing', status.filesMissing],
    ['importRadarr.skipped', status.skipped],
    ['importRadarr.failed', status.failed],
    ['importRadarr.tagsAdded', status.tagsAdded],
    ['importRadarr.exclusionsAdded', status.exclusionsAdded],
    ['importRadarr.historyAdded', status.historyAdded],
    ['importRadarr.blocklistAdded', status.blocklistAdded],
    ['importRadarr.customFormatsAdded', status.customFormatsAdded],
    ['importRadarr.customFormatsUpdated', status.customFormatsUpdated],
    ['importRadarr.profilesScored', status.profilesScored],
  ] as const

  return (
    <div className="flex flex-col gap-6" data-testid="radarr-run">
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
          <p className="text-sm text-muted-foreground">{busy ? '' : t('importRadarr.noIssues')}</p>
        ) : (
          <div className="overflow-x-auto rounded-lg border">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>{t('importRadarr.issueTitle')}</TableHead>
                  <TableHead>{t('importRadarr.issueTmdb')}</TableHead>
                  <TableHead>{t('importRadarr.issueReason')}</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {status.issues.map((issue, i) => (
                  <TableRow key={`${issue.tmdbId}-${i}`}>
                    <TableCell className="font-medium">
                      {issue.movieId !== null ? (
                        <Link to="/movies/$movieId" params={{ movieId: String(issue.movieId) }} className="underline-offset-4 hover:underline">
                          {issue.title}
                        </Link>
                      ) : (
                        issue.title
                      )}
                    </TableCell>
                    <TableCell className="tabular-nums">{issue.tmdbId ?? t('common.none')}</TableCell>
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
          <Button type="button" variant="outline" onClick={onAgain} data-testid="radarr-again">
            {t('importRadarr.again')}
          </Button>
        </div>
      )}
    </div>
  )
}
