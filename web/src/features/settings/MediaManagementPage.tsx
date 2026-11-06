import { useEffect, useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { cn } from 'cn'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ApiError, mediaManagement, type MediaManagement, type MediaManagementRequest, type TransferMode } from '@/api/client'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { useSchedulerEnabled } from '@/hooks/useSchedulerEnabled'

const modes: TransferMode[] = ['hardlinkOrCopy', 'copy', 'move']

/** A value that follows `value` after `delay` ms of quiet: the template preview is asked once per pause, not per keystroke. */
function useDebounced<T>(value: T, delay: number): T {
  const [debounced, setDebounced] = useState(value)
  useEffect(() => {
    const handle = setTimeout(() => setDebounced(value), delay)
    return () => clearTimeout(handle)
  }, [value, delay])
  return debounced
}

/** Settings → Media management (blueprint §3.7): naming templates, transfer mode, extra files, recycle bin, media info. */
export function MediaManagementPage() {
  const { t } = useTranslation()
  const query = useQuery({ queryKey: ['mediaManagement'], queryFn: mediaManagement.get })

  return (
    <>
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">{t('mediaManagement.title')}</h1>
        <p className="mt-1 max-w-3xl text-sm text-muted-foreground">{t('mediaManagement.hint')}</p>
      </div>
      {query.isPending && (
        <div className="flex flex-col gap-2">
          <Skeleton className="h-8 w-1/2" />
          <Skeleton className="h-8 w-full" />
          <Skeleton className="h-8 w-2/3" />
        </div>
      )}
      {query.error && (
        <Alert variant="destructive">
          <AlertDescription>{query.error instanceof ApiError ? query.error.message : t('common.unexpectedError')}</AlertDescription>
        </Alert>
      )}
      {query.data && <MediaManagementForm key={JSON.stringify(query.data)} initial={query.data} />}
    </>
  )
}

function toRequest(config: MediaManagement): MediaManagementRequest {
  return {
    renameFiles: config.renameFiles,
    fileNameTemplate: config.fileNameTemplate,
    folderNameTemplate: config.folderNameTemplate,
    transferMode: config.transferMode,
    recycleBinPath: config.recycleBinPath,
    recycleBinRetentionDays: config.recycleBinRetentionDays,
    importExtraFiles: config.importExtraFiles,
    extraFileExtensions: config.extraFileExtensions,
  }
}

function MediaManagementForm({ initial }: { initial: MediaManagement }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [form, setForm] = useState<MediaManagementRequest>(() => toRequest(initial))
  // The bin is emptied by the maintenance task, which only runs by hand while the scheduler is off.
  const schedulerOff = useSchedulerEnabled() === false
  const debouncedFile = useDebounced(form.fileNameTemplate, 300)
  const debouncedFolder = useDebounced(form.folderNameTemplate, 300)
  const preview = useQuery({
    queryKey: ['namingPreview', debouncedFile, debouncedFolder],
    queryFn: () => mediaManagement.preview(debouncedFile, debouncedFolder),
    staleTime: Number.POSITIVE_INFINITY,
    placeholderData: (previous) => previous,
  })

  const save = useMutation({
    mutationFn: () => mediaManagement.update({ ...form, recycleBinPath: form.recycleBinPath?.trim() ? form.recycleBinPath.trim() : null }),
    onSuccess: (saved) => {
      queryClient.setQueryData(['mediaManagement'], saved)
      toast.success(t('mediaManagement.saved'))
    },
    onError: (e) => toast.error(e instanceof ApiError ? e.message : t('mediaManagement.saveFailed')),
  })
  const backfill = useMutation({
    mutationFn: mediaManagement.backfill,
    onSuccess: (result) => toast.success(t('mediaManagement.backfillDone', { count: result.probed, remaining: result.remaining })),
    onError: (e) => toast.error(e instanceof ApiError ? e.message : t('common.unexpectedError')),
  })

  const fileErrors = preview.data?.fileErrors ?? []
  const folderErrors = preview.data?.folderErrors ?? []
  const dirty = JSON.stringify(form) !== JSON.stringify(toRequest(initial))
  const appendToken = (token: string) => setForm((f) => ({ ...f, fileNameTemplate: `${f.fileNameTemplate.trimEnd()} ${token}`.trim() }))

  const submit = (e: FormEvent) => {
    e.preventDefault()
    save.mutate()
  }

  return (
    <form className="flex flex-col gap-8" onSubmit={submit} data-testid="mediamanagement-form">
      <section className="flex flex-col gap-4">
        <h2 className="text-xl font-semibold tracking-tight">{t('mediaManagement.naming')}</h2>

        <div className="flex items-start gap-2">
          <Checkbox id="mm-rename" checked={form.renameFiles} onCheckedChange={(v) => setForm({ ...form, renameFiles: v === true })} className="mt-0.5" />
          <div className="grid gap-0.5">
            <Label htmlFor="mm-rename" className="font-normal">
              {t('mediaManagement.renameFiles')}
            </Label>
            <span className="text-xs text-muted-foreground">{t('mediaManagement.renameFilesHint')}</span>
          </div>
        </div>

        <div className="grid gap-2">
          <Label htmlFor="mm-file-template">{t('mediaManagement.fileTemplate')}</Label>
          <Input
            id="mm-file-template"
            value={form.fileNameTemplate}
            onChange={(e) => setForm({ ...form, fileNameTemplate: e.target.value })}
            className={cn('font-mono text-xs', fileErrors.length > 0 && 'border-destructive')}
            spellCheck={false}
            data-testid="mm-file-template"
          />
          <TemplatePreview label={t('mediaManagement.preview')} value={preview.data?.fileName ?? null} errors={fileErrors} testId="mm-file-preview" />
        </div>

        <div className="grid gap-2">
          <Label htmlFor="mm-folder-template">{t('mediaManagement.folderTemplate')}</Label>
          <Input
            id="mm-folder-template"
            value={form.folderNameTemplate}
            onChange={(e) => setForm({ ...form, folderNameTemplate: e.target.value })}
            className={cn('font-mono text-xs', folderErrors.length > 0 && 'border-destructive')}
            spellCheck={false}
          />
          <TemplatePreview label={t('mediaManagement.preview')} value={preview.data?.folderName ?? null} errors={folderErrors} testId="mm-folder-preview" />
          <span className="text-xs text-muted-foreground">{t('mediaManagement.folderTemplateHint')}</span>
        </div>

        <div className="grid gap-2">
          <span className="text-sm font-medium">{t('mediaManagement.tokens')}</span>
          <p className="max-w-3xl text-xs text-muted-foreground">{t('mediaManagement.tokensHint')}</p>
          <div className="flex flex-wrap gap-1" data-testid="mm-tokens">
            {initial.tokens.map((token) => (
              <Tooltip key={token.token}>
                <TooltipTrigger asChild>
                  <button type="button" onClick={() => appendToken(token.token)} className="rounded-md focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring">
                    <Badge variant={token.folder ? 'secondary' : 'outline'} className="cursor-pointer font-mono text-[11px]">
                      {token.token}
                    </Badge>
                  </button>
                </TooltipTrigger>
                <TooltipContent className="max-w-xs">
                  <p>{token.description}</p>
                  <p className="font-mono text-xs opacity-80">{token.example}</p>
                  {!token.folder && <p className="text-xs opacity-80">{t('mediaManagement.fileOnly')}</p>}
                </TooltipContent>
              </Tooltip>
            ))}
          </div>
        </div>
      </section>

      <section className="flex flex-col gap-4">
        <h2 className="text-xl font-semibold tracking-tight">{t('mediaManagement.transfer')}</h2>
        <div className="grid max-w-md gap-2">
          <Label htmlFor="mm-mode">{t('mediaManagement.transferMode')}</Label>
          <Select value={form.transferMode} onValueChange={(v) => setForm({ ...form, transferMode: v as TransferMode })}>
            <SelectTrigger id="mm-mode" className="w-full">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {modes.map((mode) => (
                <SelectItem key={mode} value={mode}>
                  {t(`mediaManagement.transferModes.${mode}`)}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
          <span className="text-xs text-muted-foreground">{t('mediaManagement.transferModeHint')}</span>
        </div>
      </section>

      <section className="flex flex-col gap-4">
        <h2 className="text-xl font-semibold tracking-tight">{t('mediaManagement.extraFiles')}</h2>
        <div className="flex items-start gap-2">
          <Checkbox
            id="mm-extras"
            checked={form.importExtraFiles}
            onCheckedChange={(v) => setForm({ ...form, importExtraFiles: v === true })}
            className="mt-0.5"
            data-testid="mm-import-extras"
          />
          <div className="grid gap-0.5">
            <Label htmlFor="mm-extras" className="font-normal">
              {t('mediaManagement.importExtraFiles')}
            </Label>
            <span className="max-w-3xl text-xs text-muted-foreground">{t('mediaManagement.importExtraFilesHint')}</span>
          </div>
        </div>
        <div className="grid max-w-md gap-2">
          <Label htmlFor="mm-extra-extensions">{t('mediaManagement.extraFileExtensions')}</Label>
          <Input
            id="mm-extra-extensions"
            value={form.extraFileExtensions}
            onChange={(e) => setForm({ ...form, extraFileExtensions: e.target.value })}
            disabled={!form.importExtraFiles}
            className="font-mono text-xs"
            spellCheck={false}
            data-testid="mm-extra-extensions"
          />
          <span className="text-xs text-muted-foreground">{t('mediaManagement.extraFileExtensionsHint')}</span>
        </div>
      </section>

      <section className="flex flex-col gap-4">
        <h2 className="text-xl font-semibold tracking-tight">{t('mediaManagement.recycleBin')}</h2>
        <div className="grid gap-4 sm:grid-cols-[minmax(0,1fr)_10rem]">
          <div className="grid gap-2">
            <Label htmlFor="mm-bin">{t('mediaManagement.recycleBinPath')}</Label>
            <Input id="mm-bin" value={form.recycleBinPath ?? ''} onChange={(e) => setForm({ ...form, recycleBinPath: e.target.value })} placeholder="/data/recycle" data-testid="mm-bin" />
            <span className="text-xs text-muted-foreground">{t('mediaManagement.recycleBinPathHint')}</span>
          </div>
          <div className="grid gap-2">
            <Label htmlFor="mm-retention">{t('mediaManagement.retention')}</Label>
            <Input
              id="mm-retention"
              type="number"
              min={0}
              max={3650}
              value={form.recycleBinRetentionDays}
              onChange={(e) => setForm({ ...form, recycleBinRetentionDays: Number(e.target.value) })}
            />
            <span className="text-xs text-muted-foreground">{t(schedulerOff ? 'mediaManagement.retentionHintManual' : 'mediaManagement.retentionHint')}</span>
          </div>
        </div>
      </section>

      <section className="flex flex-col gap-3">
        <h2 className="text-xl font-semibold tracking-tight">{t('mediaManagement.mediaInfo')}</h2>
        {initial.mediaInfoAvailable ? (
          <p className="text-sm text-muted-foreground">{t('mediaManagement.mediaInfoAvailable', { path: initial.ffprobePath })}</p>
        ) : (
          <Alert>
            <AlertDescription>{t('mediaManagement.mediaInfoMissing')}</AlertDescription>
          </Alert>
        )}
        <div>
          <Button type="button" variant="outline" size="sm" onClick={() => backfill.mutate()} disabled={!initial.mediaInfoAvailable || backfill.isPending}>
            {backfill.isPending ? t('mediaManagement.backfilling') : t('mediaManagement.backfill')}
          </Button>
        </div>
      </section>

      <div className="flex items-center gap-2">
        <Button type="submit" disabled={save.isPending || !dirty || fileErrors.length > 0 || folderErrors.length > 0} data-testid="mm-save">
          {save.isPending ? t('common.saving') : t('common.save')}
        </Button>
      </div>
    </form>
  )
}

function TemplatePreview({ label, value, errors, testId }: { label: string; value: string | null; errors: string[]; testId: string }) {
  return (
    <div className="flex flex-col gap-1 text-xs" data-testid={testId}>
      {errors.length === 0 ? (
        <span className="text-muted-foreground">
          {label}: <code className="rounded bg-muted px-1.5 py-0.5 break-all">{value ?? '…'}</code>
        </span>
      ) : (
        errors.map((error) => (
          <span key={error} className="text-destructive">
            {error}
          </span>
        ))
      )}
    </div>
  )
}
