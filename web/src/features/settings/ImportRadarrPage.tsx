import { useMemo, useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ApiError, migration, type RadarrImportPreview, type RadarrImportRequest, type RadarrImportStatus } from '@/api/client'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { ConnectForm, ImportIssues, ImportReport, isImportBusy, useImportRun } from '@/features/settings/importWizard'
import { mappingComplete, ProfileMappingTable, RootFolderMappingTable, slugPattern, suggestedMapping, Toggle } from '@/features/settings/RadarrMapping'

type Step = 'connect' | 'map'

const staleAfterImport = ['movies', 'wanted', 'rootFolders', 'tags'] as const

/** Moving in from an existing Radarr: connect, map profiles and root folders, follow the import. */
export function ImportRadarrPage() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [url, setUrl] = useState('')
  const [apiKey, setApiKey] = useState('')
  const [preview, setPreview] = useState<RadarrImportPreview | null>(null)

  const { step, setStep, status } = useImportRun({ queryKey: 'radarrImport', queryFn: migration.status, firstStep: 'connect' as Step, invalidate: staleAfterImport })

  const previewMutation = useMutation({
    mutationFn: () => migration.preview({ url: url.trim(), apiKey: apiKey.trim() }),
    onSuccess: (result) => {
      setPreview(result)
      setStep('map')
    },
  })

  return (
    <>
      <h1 className="text-2xl font-semibold tracking-tight">{t('importRadarr.title')}</h1>
      <p className="max-w-3xl text-sm text-muted-foreground">{t('importRadarr.intro')}</p>

      {(step === 'connect' || step === null) && (
        <ConnectForm
          app="radarr"
          url={url}
          onUrl={setUrl}
          apiKey={apiKey}
          onApiKey={setApiKey}
          placeholder="http://radarr:7878"
          texts={{
            url: t('importRadarr.url'),
            urlHint: t('importRadarr.urlHint'),
            apiKey: t('importRadarr.apiKey'),
            apiKeyHint: t('importRadarr.apiKeyHint'),
            previewFailed: t('importRadarr.previewFailed'),
            previewing: t('importRadarr.previewing'),
            preview: t('importRadarr.preview'),
          }}
          error={previewMutation.error}
          pending={previewMutation.isPending}
          onSubmit={() => previewMutation.mutate()}
        />
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
  const [importProfileQualities, setImportProfileQualities] = useState(true)
  const [importNaming, setImportNaming] = useState(true)
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
        importProfileQualities,
        importNaming,
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
        <Toggle id="radarr-import-profile-qualities" label={t('importRadarr.importProfileQualities')} checked={importProfileQualities} onChange={setImportProfileQualities} />
        <Toggle id="radarr-import-naming" label={t('importRadarr.importNaming')} checked={importNaming} onChange={setImportNaming} />
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
  const busy = isImportBusy(status)

  const counters = [
    [t('importRadarr.moviesAdded'), status.moviesAdded],
    [t('importRadarr.versionsAdded'), status.versionsAdded],
    [t('importRadarr.versionsUpdated'), status.versionsUpdated],
    [t('importRadarr.filesAdded'), status.filesAdded],
    [t('importRadarr.filesMissing'), status.filesMissing],
    [t('importRadarr.skipped'), status.skipped],
    [t('importRadarr.failed'), status.failed],
    [t('importRadarr.tagsAdded'), status.tagsAdded],
    [t('importRadarr.exclusionsAdded'), status.exclusionsAdded],
    [t('importRadarr.historyAdded'), status.historyAdded],
    [t('importRadarr.blocklistAdded'), status.blocklistAdded],
    [t('importRadarr.customFormatsAdded'), status.customFormatsAdded],
    [t('importRadarr.customFormatsUpdated'), status.customFormatsUpdated],
    [t('importRadarr.profilesScored'), status.profilesScored],
    [t('importRadarr.profileQualitiesCopied'), status.profileQualitiesCopied],
    [t('importRadarr.namingImported'), status.namingImported ? 1 : 0],
  ] as const

  return (
    <div className="flex flex-col gap-6" data-testid="radarr-run">
      <ImportReport status={status} counters={counters} />

      <ImportIssues
        busy={busy}
        noIssues={t('importRadarr.noIssues')}
        titleHead={t('importRadarr.issueTitle')}
        idHead={t('importRadarr.issueTmdb')}
        issues={status.issues.map((issue, i) => ({
          key: `${issue.tmdbId}-${i}`,
          title:
            issue.movieId !== null ? (
              <Link to="/movies/$movieId" params={{ movieId: String(issue.movieId) }} className="underline-offset-4 hover:underline">
                {issue.title}
              </Link>
            ) : (
              issue.title
            ),
          id: issue.tmdbId,
          reason: issue.reason,
        }))}
      />

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
