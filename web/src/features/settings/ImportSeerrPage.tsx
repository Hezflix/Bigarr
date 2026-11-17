import { useRef, useState, type FormEvent } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { UploadIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ApiError, seerrImport, type SeerrImportPreview, type SeerrImportStatus, type SeerrUserLine, type SeerrUserMatch } from '@/api/client'
import { PathPicker } from '@/components/PathPicker'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Label } from '@/components/ui/label'
import { Progress } from '@/components/ui/progress'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { isImportBusy, useImportRun } from '@/features/settings/importWizard'
import { Toggle } from '@/features/settings/RadarrMapping'
import { formatBytes } from '@/i18n'

type Step = 'source' | 'preview'

const staleAfterImport = ['requests', 'issues', 'users', 'discover'] as const

const number = new Intl.NumberFormat('en-GB')

/** How many titles the preview lists before "and N more". */
const titlesShown = 50

/** Moving in from Seerr: point at (or upload) a copy of its database, check the preview, follow the import. */
export function ImportSeerrPage() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [path, setPath] = useState('')
  const [preview, setPreview] = useState<SeerrImportPreview | null>(null)

  const { step, setStep, status } = useImportRun({ queryKey: 'seerrImport', queryFn: seerrImport.status, firstStep: 'source' as Step, invalidate: staleAfterImport })

  return (
    <>
      <h1 className="text-2xl font-semibold tracking-tight">{t('importSeerr.title')}</h1>
      <p className="max-w-3xl text-sm text-muted-foreground">{t('importSeerr.intro')}</p>

      {(step === 'source' || step === null) && (
        <SourceStep
          path={path}
          onPath={setPath}
          onPreview={(result) => {
            setPreview(result)
            setStep('preview')
          }}
        />
      )}

      {step === 'preview' && preview && (
        <PreviewStep
          preview={preview}
          path={path.trim()}
          onBack={() => setStep('source')}
          onStarted={() => {
            void queryClient.invalidateQueries({ queryKey: ['seerrImport'] })
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

function SourceStep({ path, onPath, onPreview }: { path: string; onPath: (path: string) => void; onPreview: (preview: SeerrImportPreview) => void }) {
  const { t } = useTranslation()
  const fileInput = useRef<HTMLInputElement>(null)
  const [uploadShare, setUploadShare] = useState<number | null>(null)
  const [uploadedBytes, setUploadedBytes] = useState<number | null>(null)

  const upload = useMutation({
    mutationFn: (file: File) => seerrImport.upload(file, setUploadShare),
    onSuccess: (result) => {
      onPath(result.path)
      setUploadedBytes(result.bytes)
    },
    onError: (e) => toast.error(e instanceof ApiError ? e.message : t('importSeerr.uploadFailed')),
    onSettled: () => setUploadShare(null),
  })

  const previewMutation = useMutation({ mutationFn: () => seerrImport.preview(path.trim()), onSuccess: onPreview })

  const submit = (e: FormEvent) => {
    e.preventDefault()
    previewMutation.mutate()
  }

  return (
    <form className="flex max-w-xl flex-col gap-5" onSubmit={submit} data-testid="seerr-source-form">
      <div className="grid grid-cols-1 content-start gap-2">
        <Label htmlFor="seerr-path">{t('importSeerr.path')}</Label>
        <PathPicker id="seerr-path" value={path} onChange={onPath} placeholder="/config/db/db.sqlite3" testId="seerr-path" />
        <p className="text-xs text-muted-foreground">{t('importSeerr.pathHint')}</p>
      </div>

      <div className="flex flex-col gap-2">
        <div className="flex flex-wrap items-center gap-3">
          <input
            ref={fileInput}
            type="file"
            accept=".sqlite3,.sqlite,.db,application/octet-stream"
            className="hidden"
            onChange={(e) => {
              const file = e.target.files?.[0]
              e.target.value = ''
              if (file) upload.mutate(file)
            }}
          />
          <Button type="button" variant="outline" onClick={() => fileInput.current?.click()} disabled={upload.isPending}>
            <UploadIcon />
            {upload.isPending ? t('importSeerr.uploading') : t('importSeerr.upload')}
          </Button>
          {uploadedBytes !== null && !upload.isPending && <span className="text-xs text-muted-foreground">{t('importSeerr.uploaded', { size: formatBytes(uploadedBytes) })}</span>}
        </div>
        {uploadShare !== null && <Progress value={Math.round(uploadShare * 100)} className="max-w-xs" />}
        <p className="text-xs text-muted-foreground">{t('importSeerr.uploadHint')}</p>
      </div>

      {previewMutation.error && (
        <Alert variant="destructive">
          <AlertDescription>{previewMutation.error instanceof ApiError ? previewMutation.error.message : t('importSeerr.previewFailed')}</AlertDescription>
        </Alert>
      )}
      <div>
        <Button type="submit" disabled={previewMutation.isPending || upload.isPending || path.trim().length === 0} data-testid="seerr-preview">
          {previewMutation.isPending ? t('importSeerr.previewing') : t('importSeerr.preview')}
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

function Section({ title, hint, children }: { title: string; hint?: string; children: React.ReactNode }) {
  return (
    <section className="flex flex-col gap-3">
      <h2 className="text-xl font-semibold tracking-tight">{title}</h2>
      {hint && <p className="max-w-3xl text-xs text-muted-foreground">{hint}</p>}
      {children}
    </section>
  )
}

const matchVariant: Record<SeerrUserMatch, 'default' | 'secondary' | 'outline' | 'destructive'> = {
  alreadyImported: 'outline',
  plexId: 'secondary',
  username: 'secondary',
  owner: 'secondary',
  newPlex: 'default',
  newWithoutSignIn: 'destructive',
}

function Permissions({ user }: { user: SeerrUserLine }) {
  const { t } = useTranslation()
  if (user.role === 'admin') return <span className="text-muted-foreground">{t('users.adminHasAll')}</span>
  const keys = ['requestFilms', 'requestSeries', 'request4K', 'autoApproveFilms', 'autoApproveSeries', 'reportIssues'] as const
  const on = keys.filter((key) => user.permissions[key])
  return <span>{on.length === 0 ? t('common.none') : on.map((key) => t(`users.permissions.${key}`)).join(', ')}</span>
}

function PreviewStep({ preview, path, onBack, onStarted }: { preview: SeerrImportPreview; path: string; onBack: () => void; onStarted: () => void }) {
  const { t } = useTranslation()
  const [askTmdb, setAskTmdb] = useState(true)
  const [importSliders, setImportSliders] = useState(true)
  const requests = preview.requests
  const issues = preview.issues
  const newUsers = preview.users.filter((u) => u.match === 'newPlex' || u.match === 'newWithoutSignIn').length
  const matchedUsers = preview.users.length - newUsers
  const nothingToDo =
    newUsers === 0 &&
    requests.total - requests.alreadyImported - requests.skipped <= 0 &&
    issues.total - issues.alreadyImported - issues.skipped <= 0 &&
    !importSliders

  const start = useMutation({
    mutationFn: () => seerrImport.start({ path, askTmdb, importSliders }),
    onSuccess: onStarted,
    onError: (e) => toast.error(e instanceof ApiError ? e.message : t('importSeerr.startFailed')),
  })

  return (
    <div className="flex flex-col gap-8" data-testid="seerr-preview-step">
      <Section title={t('importSeerr.summary')} hint={t('importSeerr.flavor', { flavor: preview.flavor })}>
        <dl className="grid grid-cols-2 gap-3 sm:grid-cols-4">
          <Figure label={t('importSeerr.users')} value={preview.users.length} hint={t('importSeerr.usersHint', { matched: matchedUsers, created: newUsers })} />
          <Figure label={t('importSeerr.requests')} value={requests.total} hint={t('importSeerr.requestsHint', { movies: number.format(requests.movies), series: number.format(requests.series), fourK: number.format(requests.fourK) })} />
          <Figure label={t('importSeerr.issues')} value={issues.total} hint={t('importSeerr.issuesHint', { open: number.format(issues.open), comments: number.format(issues.comments) })} />
          <Figure label={t('importSeerr.sliders')} value={preview.sliders.length} hint={t('importSeerr.slidersHint', { mapped: preview.sliders.filter((s) => s.kind).length })} />
        </dl>
      </Section>

      <Section title={t('importSeerr.usersTitle')} hint={t('importSeerr.usersExplain')}>
        <div className="overflow-x-auto rounded-lg border">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>{t('importSeerr.seerrUser')}</TableHead>
                <TableHead>{t('importSeerr.match')}</TableHead>
                <TableHead>{t('importSeerr.bigarrUser')}</TableHead>
                <TableHead>{t('users.role')}</TableHead>
                <TableHead>{t('users.permissionsTitle')}</TableHead>
                <TableHead className="text-right">{t('importSeerr.requests')}</TableHead>
                <TableHead className="text-right">{t('importSeerr.issues')}</TableHead>
                <TableHead>{t('importSeerr.notBrought')}</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {preview.users.map((user) => {
                const created = user.match === 'newPlex' || user.match === 'newWithoutSignIn'
                return (
                  <TableRow key={user.seerrId} data-testid={`seerr-user-${user.seerrId}`}>
                    <TableCell className="font-medium">{user.name}</TableCell>
                    <TableCell>
                      <Badge variant={matchVariant[user.match]}>{t(`importSeerr.matches.${user.match}`)}</Badge>
                    </TableCell>
                    <TableCell>
                      {user.bigarrUsername}
                      {user.discordId && created && <span className="text-xs text-muted-foreground"> · Discord</span>}
                    </TableCell>
                    <TableCell>{created ? t(`users.roles.${user.role}`) : <span className="text-muted-foreground">{t('importSeerr.keeps')}</span>}</TableCell>
                    <TableCell className="whitespace-normal text-xs">{created ? <Permissions user={user} /> : <span className="text-muted-foreground">{t('importSeerr.keeps')}</span>}</TableCell>
                    <TableCell className="text-right tabular-nums">{number.format(user.requests)}</TableCell>
                    <TableCell className="text-right tabular-nums">{number.format(user.issues)}</TableCell>
                    <TableCell className="whitespace-normal text-xs text-muted-foreground">{user.skipped.length > 0 ? user.skipped.join(', ') : t('common.none')}</TableCell>
                  </TableRow>
                )
              })}
            </TableBody>
          </Table>
        </div>
      </Section>

      <Section title={t('importSeerr.requestsTitle')} hint={t('importSeerr.requestsExplain')}>
        <dl className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-5">
          <Figure label={t('importSeerr.pending')} value={requests.pending} />
          <Figure label={t('importSeerr.approved')} value={requests.approved} />
          <Figure label={t('importSeerr.completed')} value={requests.completed} />
          <Figure label={t('importSeerr.declined')} value={requests.declined} />
          <Figure label={t('importSeerr.failed')} value={requests.failed} />
          <Figure label={t('importSeerr.inLibrary')} value={requests.inLibrary} />
          <Figure label={t('importSeerr.notInLibrary')} value={requests.notInLibrary} />
          <Figure label={t('importSeerr.alreadyImported')} value={requests.alreadyImported} />
          <Figure label={t('importSeerr.skipped')} value={requests.skipped} />
          <Figure label={t('importSeerr.overlapping')} value={requests.overlappingBigarr} hint={t('importSeerr.overlappingHint')} />
        </dl>
      </Section>

      <Section title={t('importSeerr.issuesTitle')} hint={t('importSeerr.issuesExplain')}>
        <dl className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-5">
          <Figure label={t('importSeerr.open')} value={issues.open} />
          <Figure label={t('importSeerr.resolved')} value={issues.resolved} />
          <Figure label={t('importSeerr.inLibrary')} value={issues.inLibrary} />
          <Figure label={t('importSeerr.notInLibrary')} value={issues.notInLibrary} />
          <Figure label={t('importSeerr.alreadyImported')} value={issues.alreadyImported} />
        </dl>
      </Section>

      <Section title={t('importSeerr.titlesTitle')} hint={t('importSeerr.titlesExplain')}>
        {preview.titlesNotInLibrary.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t('importSeerr.allInLibrary')}</p>
        ) : (
          <>
            <div className="overflow-x-auto rounded-lg border">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>{t('importSeerr.titleType')}</TableHead>
                    <TableHead>TMDB</TableHead>
                    <TableHead className="text-right">{t('importSeerr.requests')}</TableHead>
                    <TableHead className="text-right">{t('importSeerr.issues')}</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {preview.titlesNotInLibrary.slice(0, titlesShown).map((title) => (
                    <TableRow key={`${title.mediaType}-${title.tmdbId}`}>
                      <TableCell>{t(`importSeerr.mediaTypes.${title.mediaType}`)}</TableCell>
                      <TableCell className="tabular-nums">
                        <a
                          href={`https://www.themoviedb.org/${title.mediaType === 'movie' ? 'movie' : 'tv'}/${title.tmdbId}`}
                          target="_blank"
                          rel="noreferrer"
                          className="underline underline-offset-4"
                        >
                          {title.tmdbId}
                        </a>
                      </TableCell>
                      <TableCell className="text-right tabular-nums">{title.requests}</TableCell>
                      <TableCell className="text-right tabular-nums">{title.issues}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </div>
            {preview.titlesNotInLibrary.length > titlesShown && (
              <p className="text-xs text-muted-foreground">{t('importSeerr.moreTitles', { count: preview.titlesNotInLibrary.length - titlesShown })}</p>
            )}
          </>
        )}
      </Section>

      {preview.sliders.length > 0 && (
        <Section title={t('importSeerr.slidersTitle')} hint={t('importSeerr.slidersExplain')}>
          <div className="overflow-x-auto rounded-lg border">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>{t('importSeerr.seerrRow')}</TableHead>
                  <TableHead>{t('importSeerr.rowState')}</TableHead>
                  <TableHead>{t('importSeerr.bigarrRow')}</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {preview.sliders.map((slider, i) => (
                  // biome-ignore lint/suspicious/noArrayIndexKey: read-only list in Seerr's order; names may repeat
                  <TableRow key={`${slider.name}-${i}`}>
                    <TableCell className="font-medium">
                      {slider.name}
                      {slider.title && <span className="text-muted-foreground"> · {slider.title}</span>}
                    </TableCell>
                    <TableCell>{slider.enabled ? t('importSeerr.on') : t('importSeerr.off')}</TableCell>
                    <TableCell className="whitespace-normal">
                      {slider.kind ? (
                        `${t(`discoverSettings.kind.${slider.kind as 'trending'}`)}${slider.value ? ` · ${slider.value}` : ''}`
                      ) : (
                        <span className="text-muted-foreground">{slider.skipped}</span>
                      )}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        </Section>
      )}

      {preview.skipped.length > 0 && (
        <Section title={t('importSeerr.skippedTitle')}>
          <SkippedTable rows={preview.skipped} />
        </Section>
      )}

      <Section title={t('importSeerr.notImportedTitle')}>
        <ul className="flex max-w-3xl list-disc flex-col gap-1 pl-5 text-sm">
          {preview.notImported.map((line) => (
            <li key={line.what}>
              <span className="font-medium">{line.what}</span> ({number.format(line.count)}): <span className="text-muted-foreground">{line.why}</span>
            </li>
          ))}
        </ul>
      </Section>

      <Section title={t('importSeerr.options')}>
        <Toggle
          id="seerr-ask-tmdb"
          label={t('importSeerr.askTmdb')}
          hint={t('importSeerr.askTmdbHint', { count: preview.titlesToAskTmdb })}
          checked={askTmdb && preview.titlesToAskTmdb > 0}
          disabled={preview.titlesToAskTmdb === 0}
          onChange={setAskTmdb}
        />
        <Toggle id="seerr-sliders" label={t('importSeerr.importSliders')} hint={t('importSeerr.importSlidersHint')} checked={importSliders} onChange={setImportSliders} />
      </Section>

      <div className="flex flex-wrap items-center gap-3">
        <Button type="button" variant="ghost" onClick={onBack}>
          {t('importSeerr.back')}
        </Button>
        <Button type="button" onClick={() => start.mutate()} disabled={start.isPending || nothingToDo} data-testid="seerr-start">
          {start.isPending ? t('importSeerr.starting') : t('importSeerr.start')}
        </Button>
        {nothingToDo && <span className="text-xs text-muted-foreground">{t('importSeerr.nothingToDo')}</span>}
      </div>
    </div>
  )
}

function SkippedTable({ rows }: { rows: { kind: string; seerrId: number; what: string; reason: string }[] }) {
  const { t } = useTranslation()
  return (
    <div className="overflow-x-auto rounded-lg border">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>{t('importSeerr.rowKind')}</TableHead>
            <TableHead>{t('importSeerr.what')}</TableHead>
            <TableHead>{t('importSeerr.why')}</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {rows.map((row) => (
            <TableRow key={`${row.kind}-${row.seerrId}`}>
              <TableCell>{t(`importSeerr.kinds.${row.kind as 'request'}`)}</TableCell>
              <TableCell className="font-medium">{row.what}</TableCell>
              <TableCell className="whitespace-normal">{row.reason}</TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  )
}

function RunStep({ status, onAgain }: { status: SeerrImportStatus; onAgain: () => void }) {
  const { t } = useTranslation()
  const busy = isImportBusy(status)
  const progress = status.total > 0 ? Math.round((status.processed / status.total) * 100) : 0

  const counters = [
    ['importSeerr.usersAdded', status.usersAdded],
    ['importSeerr.usersMatched', status.usersMatched],
    ['importSeerr.usersUpdated', status.usersUpdated],
    ['importSeerr.requestsAdded', status.requestsAdded],
    ['importSeerr.issuesAdded', status.issuesAdded],
    ['importSeerr.commentsAdded', status.commentsAdded],
    ['importSeerr.slidersAdded', status.slidersAdded],
    ['importSeerr.slidersUpdated', status.slidersUpdated],
    ['importSeerr.titlesFromTmdb', status.titlesFromTmdb],
    ['importSeerr.alreadyImported', status.alreadyImported],
    ['importSeerr.skipped', status.skipped],
  ] as const

  return (
    <div className="flex flex-col gap-6" data-testid="seerr-run">
      {busy && (
        <section className="flex max-w-xl flex-col gap-2">
          <h2 className="text-xl font-semibold tracking-tight">{t('importSeerr.progress')}</h2>
          <Progress value={progress} />
          <span className="text-xs text-muted-foreground">
            {status.state === 'queued' ? t('importSeerr.queued') : t('importSeerr.processedOf', { processed: number.format(status.processed), total: number.format(status.total) })}
            {status.current ? ` · ${status.current}` : ''}
          </span>
        </section>
      )}

      {!busy && (
        <section className="flex flex-col gap-3">
          <h2 className="text-xl font-semibold tracking-tight">{status.state === 'failed' ? t('importSeerr.reportFailed') : t('importSeerr.report')}</h2>
          {status.error && (
            <Alert variant="destructive">
              <AlertDescription>{status.error}</AlertDescription>
            </Alert>
          )}
          <dl className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4">
            {counters.map(([key, value]) => (
              <Figure key={key} label={t(key)} value={value} />
            ))}
          </dl>
          {status.uploadDeleted && <p className="text-xs text-muted-foreground">{t('importSeerr.uploadDeleted')}</p>}
          {status.state === 'completed' && (
            <p className="flex flex-wrap gap-4 text-sm">
              <Link to="/requests" className="underline underline-offset-4">
                {t('importSeerr.seeRequests')}
              </Link>
              <Link to="/issues" className="underline underline-offset-4">
                {t('importSeerr.seeIssues')}
              </Link>
              <Link to="/settings/users" className="underline underline-offset-4">
                {t('importSeerr.seeUsers')}
              </Link>
            </p>
          )}
        </section>
      )}

      <section className="flex flex-col gap-3">
        <h2 className="text-xl font-semibold tracking-tight">{t('importSeerr.reportIssues')}</h2>
        {status.issues.length > 0 && <p className="max-w-3xl text-xs text-muted-foreground">{t('importSeerr.reportIssuesHint')}</p>}
        {status.issues.length === 0 ? <p className="text-sm text-muted-foreground">{busy ? '' : t('importSeerr.noReportIssues')}</p> : <SkippedTable rows={status.issues} />}
      </section>

      {!busy && (
        <div>
          <Button type="button" variant="outline" onClick={onAgain} data-testid="seerr-again">
            {t('importSeerr.again')}
          </Button>
        </div>
      )}
    </div>
  )
}
