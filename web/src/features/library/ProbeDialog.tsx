import { useMemo, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { cn } from 'cn'
import { CopyIcon, FileSearchIcon, RefreshCwIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ApiError, type MediaProbe } from '@/api/client'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Skeleton } from '@/components/ui/skeleton'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { formatBytes, formatDateTime } from '@/i18n'
import { formatBitrate, formatDuration, probeFormat, probeStreams, type ProbeStream, type ProbeStreamKind } from './probeStreams'

const tabs = ['streams', 'raw'] as const
type Tab = (typeof tabs)[number]

const kinds: ProbeStreamKind[] = ['video', 'audio', 'subtitle', 'other']

/** The icon button that opens the probe of one file: next to the media info summary of a film's version, in an episode file's details. */
export function ProbeButton({ onClick, className, 'data-testid': testId }: { onClick: () => void; className?: string; 'data-testid'?: string }) {
  const { t } = useTranslation()
  return (
    <Tooltip>
      <TooltipTrigger asChild>
        <Button type="button" size="icon" variant="ghost" className={cn('size-6', className)} onClick={onClick} aria-label={t('probe.open')} data-testid={testId}>
          <FileSearchIcon />
        </Button>
      </TooltipTrigger>
      <TooltipContent>{t('probe.open')}</TooltipContent>
    </Tooltip>
  )
}

/**
 * ffprobe run now on one file: its streams one by one (video, audio, subtitles, the rest), the container, and the whole
 * JSON to copy. Nothing is stored; each opening probes again.
 */
export function ProbeDialog({ title, queryKey, load, onClose }: { title: string; queryKey: readonly unknown[]; load: () => Promise<MediaProbe>; onClose: () => void }) {
  const { t } = useTranslation()
  const [tab, setTab] = useState<Tab>('streams')
  const probe = useQuery({ queryKey: ['probe', ...queryKey], queryFn: load, retry: false, gcTime: 0, staleTime: Number.POSITIVE_INFINITY })
  const streams = useMemo(() => probeStreams(probe.data?.probe), [probe.data])
  const format = useMemo(() => probeFormat(probe.data?.probe), [probe.data])
  const json = useMemo(() => (probe.data ? JSON.stringify(probe.data.probe, null, 2) : ''), [probe.data])

  const copy = () => {
    void navigator.clipboard
      ?.writeText(json)
      .then(() => toast.success(t('probe.copied')))
      .catch(() => toast.error(t('probe.copyFailed')))
  }

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="flex max-h-[90vh] flex-col sm:max-w-3xl" data-testid="probe-dialog">
        <DialogHeader className="gap-1 pr-8 text-left">
          <DialogTitle className="truncate text-base">{t('probe.title', { name: title })}</DialogTitle>
          <DialogDescription className="break-all font-mono text-xs">
            {probe.data ? probe.data.path : t('probe.running')}
          </DialogDescription>
        </DialogHeader>

        <div className="flex flex-wrap items-end gap-2 border-b">
          <div className="flex gap-1" role="tablist" aria-label={t('probe.tabsLabel')}>
            {tabs.map((key) => (
              <button
                key={key}
                type="button"
                role="tab"
                id={`probe-tab-${key}`}
                aria-selected={tab === key}
                aria-controls={`probe-panel-${key}`}
                onClick={() => setTab(key)}
                className={cn(
                  '-mb-px border-b-2 px-3 py-1.5 text-sm font-medium transition-colors outline-none focus-visible:ring-2 focus-visible:ring-ring/50',
                  tab === key ? 'border-primary text-foreground' : 'border-transparent text-muted-foreground hover:text-foreground',
                )}
                data-testid={`probe-tab-${key}`}
              >
                {t(`probe.tabs.${key}`)}
              </button>
            ))}
          </div>
          <span className="ml-auto flex flex-wrap items-center gap-2 pb-1.5">
            {probe.data && <span className="text-xs text-muted-foreground tabular-nums">{t('probe.probedAt', { date: formatDateTime(probe.data.probedAt) })}</span>}
            <Button type="button" size="sm" variant="outline" onClick={() => void probe.refetch()} disabled={probe.isFetching} data-testid="probe-rerun">
              <RefreshCwIcon className={probe.isFetching ? 'animate-spin' : undefined} />
              {t('probe.rerun')}
            </Button>
            {tab === 'raw' && (
              <Button type="button" size="sm" variant="outline" onClick={copy} disabled={!probe.data} data-testid="probe-copy">
                <CopyIcon />
                {t('probe.copy')}
              </Button>
            )}
          </span>
        </div>

        <div key={tab} className="min-h-64 overflow-y-auto" role="tabpanel" id={`probe-panel-${tab}`} aria-labelledby={`probe-tab-${tab}`} data-testid={`probe-panel-${tab}`}>
          {probe.isPending && <Skeleton className="h-48 w-full" />}
          {probe.error && (
            <Alert variant="destructive" data-testid="probe-error">
              <AlertTitle>{t('probe.failed')}</AlertTitle>
              <AlertDescription className="break-all">{probe.error instanceof ApiError ? probe.error.message : t('common.unexpectedError')}</AlertDescription>
            </Alert>
          )}
          {probe.data && tab === 'streams' && (
            <div className="flex flex-col gap-4">
              {format && <FormatCard format={format} />}
              {streams.length === 0 && <p className="text-sm text-muted-foreground">{t('probe.noStreams')}</p>}
              {kinds.map((kind) => {
                const group = streams.filter((s) => s.kind === kind)
                if (group.length === 0) return null
                return (
                  <section key={kind} className="flex flex-col gap-2" data-testid={`probe-group-${kind}`}>
                    <h3 className="text-sm font-semibold">{t(`probe.kinds.${kind}`, { count: group.length })}</h3>
                    {group.map((stream) => (
                      <StreamCard key={stream.index} stream={stream} />
                    ))}
                  </section>
                )
              })}
            </div>
          )}
          {probe.data && tab === 'raw' && (
            <pre className="overflow-x-auto rounded-md border bg-muted/40 p-3 font-mono text-xs leading-relaxed" data-testid="probe-json">
              {json}
            </pre>
          )}
        </div>
      </DialogContent>
    </Dialog>
  )
}

type Row = [label: string, value: string | null]

function Fields({ rows }: { rows: Row[] }) {
  return (
    <dl className="grid grid-cols-[9rem_1fr] gap-x-3 gap-y-1 text-sm">
      {rows
        .filter((row): row is [string, string] => row[1] !== null && row[1] !== '')
        .map(([label, value]) => (
          <div key={label} className="contents">
            <dt className="text-muted-foreground">{label}</dt>
            <dd className="break-all">{value}</dd>
          </div>
        ))}
    </dl>
  )
}

function FormatCard({ format }: { format: NonNullable<ReturnType<typeof probeFormat>> }) {
  const { t } = useTranslation()
  return (
    <section className="flex flex-col gap-2 rounded-md border p-3" data-testid="probe-format">
      <h3 className="text-sm font-semibold">{t('probe.container')}</h3>
      <Fields
        rows={[
          [t('probe.fields.format'), format.longName ? `${format.longName} (${format.name})` : format.name],
          [t('probe.fields.title'), format.title],
          [t('probe.fields.duration'), format.durationSeconds !== null ? formatDuration(format.durationSeconds) : null],
          [t('probe.fields.size'), format.size !== null ? formatBytes(format.size) : null],
          [t('probe.fields.bitrate'), format.bitrate !== null ? formatBitrate(format.bitrate) : null],
        ]}
      />
    </section>
  )
}

function StreamCard({ stream: s }: { stream: ProbeStream }) {
  const { t } = useTranslation()
  const hdr = [s.transfer, s.doviProfile !== null ? t('probe.dolbyVision', { profile: s.doviProfile }) : null].filter(Boolean).join(' · ')
  const rows: Row[] = [
    [t('probe.fields.codec'), s.codecLong ? `${s.codec} (${s.codecLong})` : s.codec],
    [t('probe.fields.profile'), s.profile],
    [t('probe.fields.language'), s.language],
    [t('probe.fields.title'), s.title],
    [t('probe.fields.bitrate'), s.bitrate !== null ? formatBitrate(s.bitrate) : null],
  ]
  if (s.kind === 'video') {
    rows.push(
      [t('probe.fields.resolution'), s.width && s.height ? `${s.width}×${s.height}` : null],
      [t('probe.fields.bitDepth'), s.bitDepth !== null ? t('probe.bits', { count: s.bitDepth }) : null],
      [t('probe.fields.pixelFormat'), s.pixelFormat],
      [t('probe.fields.frameRate'), s.frameRate !== null ? t('probe.fps', { value: s.frameRate }) : null],
      [t('probe.fields.hdr'), hdr || null],
      [t('probe.fields.primaries'), s.colorPrimaries],
      [t('probe.fields.sideData'), s.sideData.length > 0 ? s.sideData.join(', ') : null],
    )
  }
  if (s.kind === 'audio') {
    rows.push(
      [t('probe.fields.channels'), s.channels !== null ? [s.channels, s.channelLayout].filter(Boolean).join(' · ') : null],
      [t('probe.fields.sampleRate'), s.sampleRate !== null ? t('probe.hz', { value: s.sampleRate }) : null],
    )
  }
  if (s.kind === 'other') rows.unshift([t('probe.fields.type'), s.type])

  return (
    <div className="flex flex-col gap-2 rounded-md border p-3" data-testid="probe-stream">
      <div className="flex flex-wrap items-center gap-1.5">
        <span className="font-mono text-xs text-muted-foreground">#{s.index}</span>
        {s.isDefault && <Badge variant="secondary">{t('probe.default')}</Badge>}
        {s.isForced && <Badge variant="secondary">{t('probe.forced')}</Badge>}
        {s.isHearingImpaired && <Badge variant="secondary">{t('probe.hearingImpaired')}</Badge>}
      </div>
      <Fields rows={rows} />
    </div>
  )
}
