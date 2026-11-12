import { useMemo, useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ApiError, sonarrMigration, type SonarrImportPreview, type SonarrImportRequest, type SonarrImportStatus } from '@/api/client'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { ConnectForm, ImportIssues, ImportReport, isImportBusy, useImportRun } from '@/features/settings/importWizard'
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

type Step = 'connect' | 'map'

const staleAfterImport = ['series', 'rootFolders', 'tags', 'shadow'] as const

export const useSonarrLabels = (): MappingLabels => {
  const { t } = useTranslation()
  return { profile: t('importSonarr.sonarrProfile'), count: t('importSonarr.series'), path: t('importSonarr.sonarrPath') }
}

/** Moving in from an existing Sonarr: connect, map profiles and series root folders, follow the import. */
export function ImportSonarrPage() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [url, setUrl] = useState('')
  const [apiKey, setApiKey] = useState('')
  const [preview, setPreview] = useState<SonarrImportPreview | null>(null)

  const { step, setStep, status } = useImportRun({ queryKey: 'sonarrImport', queryFn: sonarrMigration.status, firstStep: 'connect' as Step, invalidate: staleAfterImport })

  const previewMutation = useMutation({
    mutationFn: () => sonarrMigration.preview({ url: url.trim(), apiKey: apiKey.trim() }),
    onSuccess: (result) => {
      setPreview(result)
      setStep('map')
    },
  })

  return (
    <>
      <h1 className="text-2xl font-semibold tracking-tight">{t('importSonarr.title')}</h1>
      <p className="max-w-3xl text-sm text-muted-foreground">{t('importSonarr.intro')}</p>

      {(step === 'connect' || step === null) && (
        <ConnectForm
          app="sonarr"
          url={url}
          onUrl={setUrl}
          apiKey={apiKey}
          onApiKey={setApiKey}
          placeholder="http://sonarr:8989"
          texts={{
            url: t('importSonarr.url'),
            urlHint: t('importSonarr.urlHint'),
            apiKey: t('importRadarr.apiKey'),
            apiKeyHint: t('importSonarr.apiKeyHint'),
            previewFailed: t('importSonarr.previewFailed'),
            previewing: t('importSonarr.previewing'),
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
                {(preview.customFormats ?? []).map((f) => (
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
  const busy = isImportBusy(status)

  const counters = [
    [t('importSonarr.seriesAdded'), status.seriesAdded],
    [t('importRadarr.versionsAdded'), status.versionsAdded],
    [t('importRadarr.versionsUpdated'), status.versionsUpdated],
    [t('importSonarr.episodesAdded'), status.episodesAdded],
    [t('importRadarr.filesAdded'), status.filesAdded],
    [t('importRadarr.filesMissing'), status.filesMissing],
    [t('importRadarr.skipped'), status.skipped],
    [t('importRadarr.failed'), status.failed],
    [t('importRadarr.tagsAdded'), status.tagsAdded],
    [t('importRadarr.exclusionsAdded'), status.exclusionsAdded],
    [t('importRadarr.historyAdded'), status.historyAdded],
    [t('importRadarr.blocklistAdded'), status.blocklistAdded],
    [t('importSonarr.customFormatsAdded'), status.customFormatsAdded ?? 0],
    [t('importSonarr.customFormatsReused'), status.customFormatsReused ?? 0],
    [t('importSonarr.customFormatsRenamed'), status.customFormatsRenamed ?? 0],
    [t('importRadarr.profilesScored'), status.profilesScored],
    [t('importRadarr.profileQualitiesCopied'), status.profileQualitiesCopied],
    [t('importSonarr.namingImported'), status.namingImported ? 1 : 0],
  ] as const

  return (
    <div className="flex flex-col gap-6" data-testid="sonarr-run">
      <ImportReport status={status} counters={counters} />

      <ImportIssues
        busy={busy}
        noIssues={t('importSonarr.noIssues')}
        titleHead={t('importSonarr.issueTitle')}
        idHead={t('importSonarr.issueTvdb')}
        issues={status.issues.map((issue, i) => ({
          key: `${issue.tvdbId}-${i}`,
          title:
            issue.seriesId !== null ? (
              <Link to="/series/$seriesId" params={{ seriesId: String(issue.seriesId) }} className="underline-offset-4 hover:underline">
                {issue.title}
              </Link>
            ) : (
              issue.title
            ),
          id: issue.tvdbId,
          reason: issue.reason,
        }))}
      />

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
