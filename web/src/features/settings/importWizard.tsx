import { useEffect, useRef, useState, type FormEvent, type ReactNode } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { cn } from 'cn'
import { UploadIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ApiError, type RadarrImportState } from '@/api/client'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Progress } from '@/components/ui/progress'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { formatBytes } from '@/i18n'

/**
 * What the "moving in" pages share: following a background import and its report, for Radarr and Sonarr the connection
 * form, for Seerr and Tautulli the upload of their database.
 */

type ImportProgressStatus = { state: RadarrImportState; total: number; processed: number; current: string | null; error: string | null }

export const isImportBusy = (status: { state: RadarrImportState } | undefined) => status?.state === 'queued' || status?.state === 'running'

/**
 * The import's status, polled while it runs, and the step shown. A page opened while an import runs (or just ran)
 * shows it rather than an empty form; `invalidate` lists the query keys a finished import made stale.
 */
export function useImportRun<S extends { state: RadarrImportState }, Step extends string>({
  queryKey,
  queryFn,
  firstStep,
  invalidate,
}: {
  queryKey: string
  queryFn: () => Promise<S>
  firstStep: Step
  /** A constant: it is a dependency of the effect that invalidates. */
  invalidate: readonly string[]
}) {
  const queryClient = useQueryClient()
  const [step, setStep] = useState<Step | 'run' | null>(null)
  const status = useQuery({
    queryKey: [queryKey],
    queryFn,
    refetchInterval: (query) => (isImportBusy(query.state.data) ? 1000 : false),
  })

  useEffect(() => {
    if (step !== null || !status.data) return
    setStep(status.data.state === 'idle' ? firstStep : 'run')
  }, [step, status.data, firstStep])

  const importState = status.data?.state
  useEffect(() => {
    if (importState !== 'completed') return
    for (const key of invalidate) {
      void queryClient.invalidateQueries({ queryKey: [key] })
    }
  }, [importState, queryClient, invalidate])

  return { step, setStep, status }
}

/** Address and API key of the application to read, then the preview. `app` prefixes the ids and test ids. */
export function ConnectForm({
  app,
  url,
  onUrl,
  apiKey,
  onApiKey,
  placeholder,
  texts,
  error,
  pending,
  onSubmit,
}: {
  app: 'radarr' | 'sonarr'
  url: string
  onUrl: (url: string) => void
  apiKey: string
  onApiKey: (apiKey: string) => void
  placeholder: string
  texts: { url: string; urlHint: string; apiKey: string; apiKeyHint: string; previewFailed: string; previewing: string; preview: string }
  error: unknown
  pending: boolean
  onSubmit: () => void
}) {
  const submit = (e: FormEvent) => {
    e.preventDefault()
    onSubmit()
  }
  return (
    <form className="flex max-w-xl flex-col gap-4" onSubmit={submit} data-testid={`${app}-connect-form`}>
      <div className="grid grid-cols-1 content-start gap-2">
        <Label htmlFor={`${app}-url`}>{texts.url}</Label>
        <Input id={`${app}-url`} value={url} onChange={(e) => onUrl(e.target.value)} placeholder={placeholder} autoFocus />
        <p className="text-xs text-muted-foreground">{texts.urlHint}</p>
      </div>
      <div className="grid grid-cols-1 content-start gap-2">
        <Label htmlFor={`${app}-key`}>{texts.apiKey}</Label>
        <Input id={`${app}-key`} type="password" value={apiKey} onChange={(e) => onApiKey(e.target.value)} autoComplete="off" />
        <p className="text-xs text-muted-foreground">{texts.apiKeyHint}</p>
      </div>
      {!!error && (
        <Alert variant="destructive">
          <AlertDescription>{error instanceof ApiError ? error.message : texts.previewFailed}</AlertDescription>
        </Alert>
      )}
      <div>
        <Button type="submit" disabled={pending || url.trim().length === 0 || apiKey.trim().length === 0} data-testid={`${app}-preview`}>
          {pending ? texts.previewing : texts.preview}
        </Button>
      </div>
    </form>
  )
}

/** Counts read well with their thousands apart: 12,480 plays. */
export const importNumber = new Intl.NumberFormat('en-GB')

/** One number of a preview or a report, under its label; `hint` adds a line of detail. */
export function ImportFigure({ label, value, hint }: { label: string; value: number | string; hint?: string }) {
  return (
    <div className="rounded-lg border p-3">
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className="text-lg font-semibold tabular-nums">{typeof value === 'number' ? importNumber.format(value) : value}</dd>
      {hint && <dd className="text-xs text-muted-foreground">{hint}</dd>}
    </div>
  )
}

/**
 * The progress bar while the import runs, the counters once it is over. `counters` pairs a label with its number.
 * `texts` are the words of an import that has its own (Radarr's otherwise), `columns` how many counters a wide screen
 * puts on a line, `children` what the report adds under the counters.
 */
export function ImportReport({
  status,
  counters,
  texts,
  columns = 'lg:grid-cols-5',
  children,
}: {
  status: ImportProgressStatus
  counters: readonly (readonly [string, number])[]
  texts?: { progress: string; queued: string; processed: string; report: string; reportFailed: string }
  columns?: string
  children?: ReactNode
}) {
  const { t } = useTranslation()
  const progress = status.total > 0 ? Math.round((status.processed / status.total) * 100) : 0
  const words = texts ?? {
    progress: t('importRadarr.progress'),
    queued: t('importRadarr.queued'),
    processed: t('importRadarr.processedOf', { processed: importNumber.format(status.processed), total: importNumber.format(status.total) }),
    report: t('importRadarr.report'),
    reportFailed: t('importRadarr.reportFailed'),
  }

  if (isImportBusy(status)) {
    return (
      <section className="flex max-w-xl flex-col gap-2">
        <h2 className="text-xl font-semibold tracking-tight">{words.progress}</h2>
        <Progress value={progress} />
        <span className="text-xs text-muted-foreground">
          {status.state === 'queued' ? words.queued : words.processed}
          {status.current ? ` · ${status.current}` : ''}
        </span>
      </section>
    )
  }

  return (
    <section className="flex flex-col gap-3">
      <h2 className="text-xl font-semibold tracking-tight">{status.state === 'failed' ? words.reportFailed : words.report}</h2>
      {status.error && (
        <Alert variant="destructive">
          <AlertDescription>{status.error}</AlertDescription>
        </Alert>
      )}
      <dl className={cn('grid grid-cols-2 gap-3 sm:grid-cols-3', columns)}>
        {counters.map(([label, value]) => (
          <ImportFigure key={label} label={label} value={value} />
        ))}
      </dl>
      {children}
    </section>
  )
}

/**
 * Sending a copy of a database to the server, for an import that reads one: how much has gone so far, and its size
 * once it is in. The path the server kept it under goes to `onPath`.
 */
export function useDatabaseUpload({
  upload,
  onPath,
  failed,
}: {
  upload: (file: File, onProgress: (share: number) => void) => Promise<{ path: string; bytes: number }>
  onPath: (path: string) => void
  failed: string
}) {
  const [share, setShare] = useState<number | null>(null)
  const [bytes, setBytes] = useState<number | null>(null)
  const mutation = useMutation({
    mutationFn: (file: File) => upload(file, setShare),
    onSuccess: (result) => {
      onPath(result.path)
      setBytes(result.bytes)
    },
    onError: (e) => toast.error(e instanceof ApiError ? e.message : failed),
    onSettled: () => setShare(null),
  })
  return { pending: mutation.isPending, send: (file: File) => mutation.mutate(file), share, bytes }
}

/** The upload button of such an import, with the progress of the upload and the size once uploaded. */
export function DatabaseUploadField({
  upload,
  accept,
  texts,
}: {
  upload: ReturnType<typeof useDatabaseUpload>
  accept: string
  texts: { upload: string; uploading: string; uploaded: (size: string) => string; hint: string }
}) {
  const fileInput = useRef<HTMLInputElement>(null)
  return (
    <div className="flex flex-col gap-2">
      <div className="flex flex-wrap items-center gap-3">
        <input
          ref={fileInput}
          type="file"
          accept={accept}
          className="hidden"
          onChange={(e) => {
            const file = e.target.files?.[0]
            e.target.value = ''
            if (file) upload.send(file)
          }}
        />
        <Button type="button" variant="outline" onClick={() => fileInput.current?.click()} disabled={upload.pending}>
          <UploadIcon />
          {upload.pending ? texts.uploading : texts.upload}
        </Button>
        {upload.bytes !== null && !upload.pending && <span className="text-xs text-muted-foreground">{texts.uploaded(formatBytes(upload.bytes))}</span>}
      </div>
      {upload.share !== null && <Progress value={Math.round(upload.share * 100)} className="max-w-xs" />}
      <p className="text-xs text-muted-foreground">{texts.hint}</p>
    </div>
  )
}

/** What the import could not bring, one line each: the title (a link when it is in the library), its external id, the reason. */
export function ImportIssues({
  busy,
  noIssues,
  titleHead,
  idHead,
  issues,
}: {
  busy: boolean
  noIssues: string
  titleHead: string
  idHead: string
  issues: { key: string; title: ReactNode; id: number | null; reason: string }[]
}) {
  const { t } = useTranslation()
  return (
    <section className="flex flex-col gap-3">
      <h2 className="text-xl font-semibold tracking-tight">{t('importRadarr.issues')}</h2>
      {issues.length === 0 ? (
        <p className="text-sm text-muted-foreground">{busy ? '' : noIssues}</p>
      ) : (
        <div className="overflow-x-auto rounded-lg border">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>{titleHead}</TableHead>
                <TableHead>{idHead}</TableHead>
                <TableHead>{t('importRadarr.issueReason')}</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {issues.map((issue) => (
                <TableRow key={issue.key}>
                  <TableCell className="font-medium">{issue.title}</TableCell>
                  <TableCell className="tabular-nums">{issue.id ?? t('common.none')}</TableCell>
                  <TableCell className="whitespace-normal">{issue.reason}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}
    </section>
  )
}
