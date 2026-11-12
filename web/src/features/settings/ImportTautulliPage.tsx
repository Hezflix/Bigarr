import { useEffect, useRef, useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { UploadIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ApiError, mediaServers, tautulliImport, type TautulliImportPreview, type TautulliImportStatus } from '@/api/client'
import { PathPicker } from '@/components/PathPicker'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Label } from '@/components/ui/label'
import { Progress } from '@/components/ui/progress'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { isImportBusy, useImportRun } from '@/features/settings/importWizard'
import { Toggle } from '@/features/settings/RadarrMapping'
import { formatBytes, formatDate } from '@/i18n'

type Step = 'source' | 'preview'

const staleAfterImport = ['playback'] as const

const number = new Intl.NumberFormat('en-GB')

/** Moving in from Tautulli: point at (or upload) a copy of its database, check the preview, follow the import. */
export function ImportTautulliPage() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [path, setPath] = useState('')
  const [serverId, setServerId] = useState<number | null>(null)
  const [preview, setPreview] = useState<TautulliImportPreview | null>(null)

  const { step, setStep, status } = useImportRun({ queryKey: 'tautulliImport', queryFn: tautulliImport.status, firstStep: 'source' as Step, invalidate: staleAfterImport })

  return (
    <>
      <h1 className="text-2xl font-semibold tracking-tight">{t('importTautulli.title')}</h1>
      <p className="max-w-3xl text-sm text-muted-foreground">{t('importTautulli.intro')}</p>

      {(step === 'source' || step === null) && (
        <SourceStep
          path={path}
          onPath={setPath}
          serverId={serverId}
          onServer={setServerId}
          onPreview={(result) => {
            setPreview(result)
            setStep('preview')
          }}
        />
      )}

      {step === 'preview' && preview && serverId !== null && (
        <PreviewStep
          preview={preview}
          path={path.trim()}
          serverId={serverId}
          onBack={() => setStep('source')}
          onStarted={() => {
            void queryClient.invalidateQueries({ queryKey: ['tautulliImport'] })
            setStep('run')
          }}
        />
      )}

      {step === 'run' && status.data && (
        <RunStep
          status={status.data}
          onAgain={() => {
            setPreview(null)
            setPath('')
            setStep('source')
          }}
        />
      )}
    </>
  )
}

function SourceStep({
  path,
  onPath,
  serverId,
  onServer,
  onPreview,
}: {
  path: string
  onPath: (path: string) => void
  serverId: number | null
  onServer: (id: number) => void
  onPreview: (preview: TautulliImportPreview) => void
}) {
  const { t } = useTranslation()
  const fileInput = useRef<HTMLInputElement>(null)
  const [uploadShare, setUploadShare] = useState<number | null>(null)
  const [uploadedBytes, setUploadedBytes] = useState<number | null>(null)

  const servers = useQuery({ queryKey: ['mediaServers'], queryFn: mediaServers.list })
  const plexServers = (servers.data ?? []).filter((s) => s.kind === 'plex')

  // One Plex server, nothing to choose.
  useEffect(() => {
    if (serverId === null && plexServers.length === 1) onServer(plexServers[0].id)
  }, [serverId, plexServers, onServer])

  const upload = useMutation({
    mutationFn: (file: File) => tautulliImport.upload(file, setUploadShare),
    onSuccess: (result) => {
      onPath(result.path)
      setUploadedBytes(result.bytes)
    },
    onError: (e) => toast.error(e instanceof ApiError ? e.message : t('importTautulli.uploadFailed')),
    onSettled: () => setUploadShare(null),
  })

  const previewMutation = useMutation({
    mutationFn: () => tautulliImport.preview({ path: path.trim(), mediaServerId: serverId ?? 0 }),
    onSuccess: onPreview,
  })

  const submit = (e: FormEvent) => {
    e.preventDefault()
    previewMutation.mutate()
  }

  if (servers.isSuccess && plexServers.length === 0) {
    return (
      <Alert className="max-w-xl">
        <AlertDescription>
          {t('importTautulli.noPlex')}{' '}
          <Link to="/settings/mediaservers" className="underline underline-offset-4">
            {t('nav.settingsMediaServers')}
          </Link>
        </AlertDescription>
      </Alert>
    )
  }

  return (
    <form className="flex max-w-xl flex-col gap-5" onSubmit={submit} data-testid="tautulli-source-form">
      <div className="grid grid-cols-1 gap-2">
        <Label htmlFor="tautulli-server">{t('importTautulli.server')}</Label>
        <Select value={serverId !== null ? String(serverId) : ''} onValueChange={(value) => onServer(Number(value))}>
          <SelectTrigger id="tautulli-server" className="w-full">
            <SelectValue placeholder={t('importTautulli.chooseServer')} />
          </SelectTrigger>
          <SelectContent>
            {plexServers.map((s) => (
              <SelectItem key={s.id} value={String(s.id)}>
                {s.name}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
        <p className="text-xs text-muted-foreground">{t('importTautulli.serverHint')}</p>
      </div>

      <div className="grid grid-cols-1 gap-2">
        <Label htmlFor="tautulli-path">{t('importTautulli.path')}</Label>
        <PathPicker id="tautulli-path" value={path} onChange={onPath} placeholder="/config/tautulli.db" testId="tautulli-path" />
        <p className="text-xs text-muted-foreground">{t('importTautulli.pathHint')}</p>
      </div>

      <div className="flex flex-col gap-2">
        <div className="flex flex-wrap items-center gap-3">
          <input
            ref={fileInput}
            type="file"
            accept=".db,.sqlite,application/octet-stream"
            className="hidden"
            onChange={(e) => {
              const file = e.target.files?.[0]
              e.target.value = ''
              if (file) upload.mutate(file)
            }}
          />
          <Button type="button" variant="outline" onClick={() => fileInput.current?.click()} disabled={upload.isPending}>
            <UploadIcon />
            {upload.isPending ? t('importTautulli.uploading') : t('importTautulli.upload')}
          </Button>
          {uploadedBytes !== null && !upload.isPending && <span className="text-xs text-muted-foreground">{t('importTautulli.uploaded', { size: formatBytes(uploadedBytes) })}</span>}
        </div>
        {uploadShare !== null && <Progress value={Math.round(uploadShare * 100)} className="max-w-xs" />}
        <p className="text-xs text-muted-foreground">{t('importTautulli.uploadHint')}</p>
      </div>

      {previewMutation.error && (
        <Alert variant="destructive">
          <AlertDescription>{previewMutation.error instanceof ApiError ? previewMutation.error.message : t('importTautulli.previewFailed')}</AlertDescription>
        </Alert>
      )}
      <div>
        <Button type="submit" disabled={previewMutation.isPending || upload.isPending || path.trim().length === 0 || serverId === null} data-testid="tautulli-preview">
          {previewMutation.isPending ? t('importTautulli.previewing') : t('importTautulli.preview')}
        </Button>
      </div>
    </form>
  )
}

function Figure({ label, value, hint }: { label: string; value: number | string; hint?: string }) {
  return (
    <div className="rounded-lg border p-3">
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className="text-lg font-semibold tabular-nums">{typeof value === 'number' ? number.format(value) : value}</dd>
      {hint && <dd className="text-xs text-muted-foreground">{hint}</dd>}
    </div>
  )
}

function PreviewStep({ preview, path, serverId, onBack, onStarted }: { preview: TautulliImportPreview; path: string; serverId: number; onBack: () => void; onStarted: () => void }) {
  const { t } = useTranslation()
  const [askPlex, setAskPlex] = useState(true)
  const nothingToDo = preview.plays - preview.alreadyImported - preview.overlappingLive - preview.otherPlays <= 0

  const start = useMutation({
    mutationFn: () => tautulliImport.start({ path, mediaServerId: serverId, askPlex }),
    onSuccess: onStarted,
    onError: (e) => toast.error(e instanceof ApiError ? e.message : t('importTautulli.startFailed')),
  })

  const range = preview.firstPlayAt && preview.lastPlayAt ? `${formatDate(preview.firstPlayAt)} – ${formatDate(preview.lastPlayAt)}` : t('common.none')

  return (
    <div className="flex flex-col gap-6" data-testid="tautulli-preview-step">
      <section className="flex flex-col gap-3">
        <h2 className="text-xl font-semibold tracking-tight">{t('importTautulli.history')}</h2>
        <dl className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4">
          <Figure label={t('importTautulli.range')} value={range} hint={preview.tautulliVersion ? t('importTautulli.version', { version: preview.tautulliVersion }) : undefined} />
          <Figure label={t('importTautulli.users')} value={preview.users} hint={t('importTautulli.usersHint', { deleted: preview.deletedUsers, known: preview.usersKnown })} />
          <Figure label={t('importTautulli.plays')} value={preview.plays} hint={t('importTautulli.playsHint', { rows: number.format(preview.historyRows) })} />
          <Figure label={t('importTautulli.episodePlays')} value={preview.episodePlays} hint={t('importTautulli.episodesHint', { episodes: number.format(preview.episodes) })} />
        </dl>
      </section>

      <section className="flex flex-col gap-3">
        <h2 className="text-xl font-semibold tracking-tight">{t('importTautulli.films')}</h2>
        <dl className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-5">
          <Figure label={t('importTautulli.moviePlays')} value={preview.moviePlays} hint={t('importTautulli.moviesHint', { movies: number.format(preview.movies) })} />
          <Figure label={t('importTautulli.withFilm')} value={preview.moviePlaysWithFilm} />
          <Figure label={t('importTautulli.withVersion')} value={preview.moviePlaysWithVersion} />
          <Figure label={t('importTautulli.askPlexCount')} value={preview.moviePlaysAskPlex} />
          <Figure label={t('importTautulli.unmatched')} value={preview.moviePlaysUnmatched} />
        </dl>
        <p className="max-w-3xl text-xs text-muted-foreground">{t('importTautulli.filmsHint')}</p>
      </section>

      <section className="flex flex-col gap-3">
        <h2 className="text-xl font-semibold tracking-tight">{t('importTautulli.leftOut')}</h2>
        <dl className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4">
          <Figure label={t('importTautulli.alreadyImported')} value={preview.alreadyImported} />
          <Figure label={t('importTautulli.overlappingLive')} value={preview.overlappingLive} />
          <Figure label={t('importTautulli.otherPlays')} value={preview.otherPlays} />
        </dl>
      </section>

      <section className="flex flex-col gap-3">
        <h2 className="text-xl font-semibold tracking-tight">{t('importTautulli.options')}</h2>
        <Toggle id="tautulli-ask-plex" label={t('importTautulli.askPlex')} hint={t('importTautulli.askPlexHint')} checked={askPlex} onChange={setAskPlex} />
      </section>

      <div className="flex flex-wrap items-center gap-3">
        <Button type="button" variant="ghost" onClick={onBack}>
          {t('importTautulli.back')}
        </Button>
        <Button type="button" onClick={() => start.mutate()} disabled={start.isPending || nothingToDo} data-testid="tautulli-start">
          {start.isPending ? t('importTautulli.starting') : t('importTautulli.start')}
        </Button>
        {nothingToDo && <span className="text-xs text-muted-foreground">{t('importTautulli.nothingToDo')}</span>}
      </div>
    </div>
  )
}

function RunStep({ status, onAgain }: { status: TautulliImportStatus; onAgain: () => void }) {
  const { t } = useTranslation()
  const busy = isImportBusy(status)
  const progress = status.total > 0 ? Math.round((status.processed / status.total) * 100) : 0

  const counters = [
    ['importTautulli.playsAdded', status.playsAdded],
    ['importTautulli.streamsAdded', status.streamsAdded],
    ['importTautulli.playsExtended', status.playsExtended],
    ['importTautulli.usersAdded', status.usersAdded],
    ['importTautulli.itemsAdded', status.itemsAdded],
    ['importTautulli.moviePlays', status.moviePlays],
    ['importTautulli.withFilm', status.moviePlaysWithFilm],
    ['importTautulli.withVersion', status.moviePlaysWithVersion],
    ['importTautulli.plexAsked', status.plexAsked],
    ['importTautulli.plexResolved', status.plexResolved],
    ['importTautulli.alreadyImported', status.alreadyImported],
    ['importTautulli.overlappingLive', status.overlappingLive],
    ['importTautulli.otherPlays', status.otherSkipped],
  ] as const

  return (
    <div className="flex flex-col gap-6" data-testid="tautulli-run">
      {busy && (
        <section className="flex max-w-xl flex-col gap-2">
          <h2 className="text-xl font-semibold tracking-tight">{t('importTautulli.progress')}</h2>
          <Progress value={progress} />
          <span className="text-xs text-muted-foreground">
            {status.state === 'queued' ? t('importTautulli.queued') : t('importTautulli.processedOf', { processed: number.format(status.processed), total: number.format(status.total) })}
            {status.current ? ` · ${status.current}` : ''}
          </span>
        </section>
      )}

      {!busy && (
        <section className="flex flex-col gap-3">
          <h2 className="text-xl font-semibold tracking-tight">{status.state === 'failed' ? t('importTautulli.reportFailed') : t('importTautulli.report')}</h2>
          {status.error && (
            <Alert variant="destructive">
              <AlertDescription>{status.error}</AlertDescription>
            </Alert>
          )}
          <dl className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-5">
            {counters.map(([key, value]) => (
              <Figure key={key} label={t(key)} value={value} />
            ))}
          </dl>
          {status.uploadDeleted && <p className="text-xs text-muted-foreground">{t('importTautulli.uploadDeleted')}</p>}
          {status.state === 'completed' && (
            <p className="text-sm">
              <Link to="/playback/plays" className="underline underline-offset-4">
                {t('importTautulli.seePlays')}
              </Link>
            </p>
          )}
        </section>
      )}

      <section className="flex flex-col gap-3">
        <div className="flex flex-wrap items-baseline gap-3">
          <h2 className="text-xl font-semibold tracking-tight">{t('importTautulli.issues')}</h2>
          {!busy && status.issues.length > 0 && (
            <Link to="/playback/plays" search={{ kind: 'unmatched' }} className="text-sm underline underline-offset-4" data-testid="tautulli-unmatched-plays">
              {t('importTautulli.seeUnmatched')}
            </Link>
          )}
        </div>
        {status.issues.length > 0 && <p className="max-w-3xl text-xs text-muted-foreground">{t('importTautulli.issuesHint')}</p>}
        {status.issues.length === 0 ? (
          <p className="text-sm text-muted-foreground">{busy ? '' : t('importTautulli.noIssues')}</p>
        ) : (
          <div className="overflow-x-auto rounded-lg border">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>{t('importTautulli.issueTitle')}</TableHead>
                  <TableHead>{t('importTautulli.issueYear')}</TableHead>
                  <TableHead className="text-right">{t('importTautulli.issuePlays')}</TableHead>
                  <TableHead>{t('importTautulli.issueReason')}</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {status.issues.map((issue, i) => (
                  <TableRow key={`${issue.title}-${i}`}>
                    <TableCell className="font-medium">{issue.title}</TableCell>
                    <TableCell className="tabular-nums">{issue.year ?? t('common.none')}</TableCell>
                    <TableCell className="text-right tabular-nums">{issue.plays}</TableCell>
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
          <Button type="button" variant="outline" onClick={onAgain} data-testid="tautulli-again">
            {t('importTautulli.again')}
          </Button>
        </div>
      )}
    </div>
  )
}
