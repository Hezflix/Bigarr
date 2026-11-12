import { useEffect, useState, type FormEvent, type ReactNode } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { ApiError, type RadarrImportState } from '@/api/client'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Progress } from '@/components/ui/progress'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'

/** What the "moving in" pages share: following a background import, and for Radarr and Sonarr the connection form and the report. */

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
      <div className="grid grid-cols-1 gap-2">
        <Label htmlFor={`${app}-url`}>{texts.url}</Label>
        <Input id={`${app}-url`} value={url} onChange={(e) => onUrl(e.target.value)} placeholder={placeholder} autoFocus />
        <p className="text-xs text-muted-foreground">{texts.urlHint}</p>
      </div>
      <div className="grid grid-cols-1 gap-2">
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

/** The progress bar while the import runs, the counters once it is over. `counters` pairs a label with its number. */
export function ImportReport({ status, counters }: { status: ImportProgressStatus; counters: readonly (readonly [string, number])[] }) {
  const { t } = useTranslation()
  const progress = status.total > 0 ? Math.round((status.processed / status.total) * 100) : 0

  if (isImportBusy(status)) {
    return (
      <section className="flex max-w-xl flex-col gap-2">
        <h2 className="text-xl font-semibold tracking-tight">{t('importRadarr.progress')}</h2>
        <Progress value={progress} />
        <span className="text-xs text-muted-foreground">
          {status.state === 'queued' ? t('importRadarr.queued') : t('importRadarr.processedOf', { processed: status.processed, total: status.total })}
          {status.current ? ` · ${status.current}` : ''}
        </span>
      </section>
    )
  }

  return (
    <section className="flex flex-col gap-3">
      <h2 className="text-xl font-semibold tracking-tight">{status.state === 'failed' ? t('importRadarr.reportFailed') : t('importRadarr.report')}</h2>
      {status.error && (
        <Alert variant="destructive">
          <AlertDescription>{status.error}</AlertDescription>
        </Alert>
      )}
      <dl className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-5">
        {counters.map(([label, value]) => (
          <div key={label} className="rounded-lg border p-3">
            <dt className="text-xs text-muted-foreground">{label}</dt>
            <dd className="text-lg font-semibold tabular-nums">{value}</dd>
          </div>
        ))}
      </dl>
    </section>
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
