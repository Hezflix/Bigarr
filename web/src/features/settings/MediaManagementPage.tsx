import { useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { cn } from 'cn'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import {
  ApiError,
  filmColonReplacements,
  mediaManagement,
  multiEpisodeStyles,
  seriesColonReplacements,
  type ColonReplacement,
  type MediaManagement,
  type MediaManagementRequest,
  type MultiEpisodeStyle,
  type SeasonPackUpgrade,
  type SeriesNaming,
  type SeriesNamingRequest,
  type TransferMode,
} from '@/api/client'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { useDebounced } from '@/hooks/useDebounced'
import { useSchedulerEnabled } from '@/hooks/useSchedulerEnabled'

const modes: TransferMode[] = ['hardlinkOrCopy', 'copy', 'move']

/** Settings → Media management: naming templates, transfer mode, extra files, recycle bin, media info. */
export function MediaManagementPage() {
  const { t } = useTranslation()
  const query = useQuery({ queryKey: ['mediaManagement'], queryFn: mediaManagement.get })
  const seriesNaming = useQuery({ queryKey: ['seriesNaming'], queryFn: mediaManagement.seriesNaming })

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
      {seriesNaming.data && <SeriesNamingForm key={JSON.stringify(seriesNaming.data)} initial={seriesNaming.data} />}
    </>
  )
}

const seriesRequest = (n: SeriesNaming): SeriesNamingRequest => ({
  renameEpisodes: n.renameEpisodes,
  seriesFolderTemplate: n.seriesFolderTemplate,
  seasonFolderTemplate: n.seasonFolderTemplate,
  specialsFolderName: n.specialsFolderName,
  standardEpisodeTemplate: n.standardEpisodeTemplate,
  dailyEpisodeTemplate: n.dailyEpisodeTemplate,
  animeEpisodeTemplate: n.animeEpisodeTemplate,
  multiEpisodeStyle: n.multiEpisodeStyle,
  replaceIllegalCharacters: n.replaceIllegalCharacters,
  colonReplacement: n.colonReplacement,
  customColonReplacement: n.customColonReplacement,
})

/** A title with a colon, an ampersand and a dash: what the previews render so the two settings show. */
const filmSampleTitle = 'Mission: Impossible – Dead Reckoning'
const seriesSampleTitle = 'Star Wars: Andor'

/** Radarr's and Sonarr's "Replace Illegal Characters" and "Colon Replacement", with the title the previews try them on. */
function CleaningFields({
  idPrefix,
  replace,
  colon,
  custom,
  colons,
  customErrors,
  sampleTitle,
  onReplace,
  onColon,
  onCustom,
  onSampleTitle,
}: {
  idPrefix: string
  replace: boolean
  colon: ColonReplacement
  custom?: string
  colons: ColonReplacement[]
  customErrors?: string[]
  sampleTitle: string
  onReplace: (value: boolean) => void
  onColon: (value: ColonReplacement) => void
  onCustom?: (value: string) => void
  onSampleTitle: (value: string) => void
}) {
  const { t } = useTranslation()
  return (
    <div className="flex flex-col gap-4" data-testid={`${idPrefix}-cleaning`}>
      <div className="flex items-start gap-2">
        <Checkbox id={`${idPrefix}-replace`} checked={replace} onCheckedChange={(v) => onReplace(v === true)} className="mt-0.5" data-testid={`${idPrefix}-replace`} />
        <div className="grid gap-0.5">
          <Label htmlFor={`${idPrefix}-replace`} className="font-normal">
            {t('mediaManagement.cleaning.replaceIllegal')}
          </Label>
          <span className="max-w-3xl text-xs text-muted-foreground">{t('mediaManagement.cleaning.replaceIllegalHint')}</span>
        </div>
      </div>

      {replace && (
        <div className="grid max-w-md gap-2">
          <Label htmlFor={`${idPrefix}-colon`}>{t('mediaManagement.cleaning.colon')}</Label>
          <Select value={colon} onValueChange={(v) => onColon(v as ColonReplacement)}>
            <SelectTrigger id={`${idPrefix}-colon`} className="w-full" data-testid={`${idPrefix}-colon`}>
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {colons.map((option) => (
                <SelectItem key={option} value={option}>
                  {t(`mediaManagement.cleaning.colons.${option}`)}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
          <span className="text-xs text-muted-foreground">{t('mediaManagement.cleaning.colonHint')}</span>
        </div>
      )}

      {replace && colon === 'custom' && onCustom && (
        <div className="grid max-w-md gap-2">
          <Label htmlFor={`${idPrefix}-custom`}>{t('mediaManagement.cleaning.custom')}</Label>
          <Input
            id={`${idPrefix}-custom`}
            value={custom ?? ''}
            onChange={(e) => onCustom(e.target.value)}
            className={cn('font-mono text-xs', (customErrors?.length ?? 0) > 0 && 'border-destructive')}
            spellCheck={false}
            data-testid={`${idPrefix}-custom`}
          />
          {(customErrors?.length ?? 0) > 0 ? (
            customErrors?.map((error) => (
              <span key={error} className="text-xs text-destructive">
                {error}
              </span>
            ))
          ) : (
            <span className="text-xs text-muted-foreground">{t('mediaManagement.cleaning.customHint')}</span>
          )}
        </div>
      )}

      <div className="grid max-w-md gap-2">
        <Label htmlFor={`${idPrefix}-sample`}>{t('mediaManagement.cleaning.sampleTitle')}</Label>
        <Input id={`${idPrefix}-sample`} value={sampleTitle} onChange={(e) => onSampleTitle(e.target.value)} spellCheck={false} data-testid={`${idPrefix}-sample`} />
        <span className="text-xs text-muted-foreground">{t('mediaManagement.cleaning.sampleTitleHint')}</span>
      </div>
    </div>
  )
}

type SeriesTemplateField = 'seriesFolderTemplate' | 'seasonFolderTemplate' | 'specialsFolderName' | 'standardEpisodeTemplate' | 'dailyEpisodeTemplate' | 'animeEpisodeTemplate'

/** Sonarr's episode naming: the series templates, with a live preview on Sonarr's sample series. Saved on its own. */
function SeriesNamingForm({ initial }: { initial: SeriesNaming }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [form, setForm] = useState<SeriesNamingRequest>(() => seriesRequest(initial))
  const [focused, setFocused] = useState<SeriesTemplateField>('standardEpisodeTemplate')
  const [sampleTitle, setSampleTitle] = useState(seriesSampleTitle)
  const debounced = useDebounced({ ...form, sampleTitle }, 300)
  const preview = useQuery({
    queryKey: ['seriesNamingPreview', debounced],
    queryFn: () => mediaManagement.previewSeriesNaming(debounced),
    staleTime: Number.POSITIVE_INFINITY,
    placeholderData: (previous) => previous,
  })
  const save = useMutation({
    mutationFn: () => mediaManagement.updateSeriesNaming(form),
    onSuccess: (saved) => {
      queryClient.setQueryData(['seriesNaming'], saved)
      toast.success(t('seriesNaming.saved'))
    },
    onError: (e) => toast.error(e instanceof ApiError ? e.message : t('mediaManagement.saveFailed')),
  })

  const errors = preview.data?.errors ?? {}
  const hasErrors = Object.values(errors).some((e) => e.length > 0)
  const dirty = JSON.stringify(form) !== JSON.stringify(seriesRequest(initial))
  const fileField = focused === 'standardEpisodeTemplate' || focused === 'dailyEpisodeTemplate' || focused === 'animeEpisodeTemplate'
  const tokens = initial.tokens.filter((token) =>
    fileField ? true : focused === 'seriesFolderTemplate' ? token.scope === 'series' : token.scope !== 'episode',
  )
  const appendToken = (token: string) => setForm((f) => ({ ...f, [focused]: `${f[focused].trimEnd()} ${token}`.trim() }))

  const field = (key: SeriesTemplateField, errorKey: string, previewValue: string | null | undefined, extra?: string | null) => (
    <div className="grid gap-2">
      <Label htmlFor={`sn-${key}`}>{t(`seriesNaming.${key}`)}</Label>
      <Input
        id={`sn-${key}`}
        value={form[key]}
        onFocus={() => setFocused(key)}
        onChange={(e) => setForm({ ...form, [key]: e.target.value })}
        className={cn('font-mono text-xs', (errors[errorKey]?.length ?? 0) > 0 && 'border-destructive')}
        spellCheck={false}
        data-testid={`sn-${key}`}
      />
      <TemplatePreview label={t('mediaManagement.preview')} value={previewValue ?? null} errors={errors[errorKey] ?? []} testId={`sn-${key}-preview`} />
      {extra && (errors[errorKey]?.length ?? 0) === 0 && (
        <span className="text-xs text-muted-foreground">
          {t('seriesNaming.multiPreview')}: <code className="rounded bg-muted px-1.5 py-0.5 break-all">{extra}</code>
        </span>
      )}
    </div>
  )

  return (
    <form
      className="flex flex-col gap-4 border-t pt-8"
      onSubmit={(e) => {
        e.preventDefault()
        save.mutate()
      }}
      data-testid="series-naming-form"
    >
      <div>
        <h2 className="text-xl font-semibold tracking-tight">{t('seriesNaming.title')}</h2>
        <p className="mt-1 max-w-3xl text-sm text-muted-foreground">{t('seriesNaming.hint')}</p>
      </div>

      <div className="flex items-start gap-2">
        <Checkbox id="sn-rename" checked={form.renameEpisodes} onCheckedChange={(v) => setForm({ ...form, renameEpisodes: v === true })} className="mt-0.5" />
        <div className="grid gap-0.5">
          <Label htmlFor="sn-rename" className="font-normal">
            {t('seriesNaming.renameEpisodes')}
          </Label>
          <span className="text-xs text-muted-foreground">{t('seriesNaming.renameEpisodesHint')}</span>
        </div>
      </div>

      <CleaningFields
        idPrefix="sn"
        replace={form.replaceIllegalCharacters}
        colon={form.colonReplacement}
        custom={form.customColonReplacement}
        colons={seriesColonReplacements}
        customErrors={errors.customColonReplacement}
        sampleTitle={sampleTitle}
        onReplace={(v) => setForm({ ...form, replaceIllegalCharacters: v })}
        onColon={(v) => setForm({ ...form, colonReplacement: v })}
        onCustom={(v) => setForm({ ...form, customColonReplacement: v })}
        onSampleTitle={setSampleTitle}
      />

      {field('standardEpisodeTemplate', 'standardEpisode', preview.data?.standardEpisode, preview.data?.multiEpisode)}
      {field('dailyEpisodeTemplate', 'dailyEpisode', preview.data?.dailyEpisode)}
      {field('animeEpisodeTemplate', 'animeEpisode', preview.data?.animeEpisode)}

      <div className="grid max-w-md gap-2">
        <Label htmlFor="sn-style">{t('seriesNaming.multiEpisodeStyle')}</Label>
        <Select value={form.multiEpisodeStyle} onValueChange={(v) => setForm({ ...form, multiEpisodeStyle: v as MultiEpisodeStyle })}>
          <SelectTrigger id="sn-style" className="w-full">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {multiEpisodeStyles.map((style) => (
              <SelectItem key={style} value={style}>
                {t(`seriesNaming.styles.${style}`)}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>

      {field('seriesFolderTemplate', 'seriesFolder', preview.data?.seriesFolder)}
      <span className="-mt-2 text-xs text-muted-foreground">{t('seriesNaming.seriesFolderHint')}</span>
      {field('seasonFolderTemplate', 'seasonFolder', preview.data?.seasonFolder)}
      {field('specialsFolderName', 'specialsFolder', preview.data?.specialsFolder)}

      <div className="grid gap-2">
        <span className="text-sm font-medium">{t('mediaManagement.tokens')}</span>
        <p className="max-w-3xl text-xs text-muted-foreground">{t('seriesNaming.tokensHint', { field: t(`seriesNaming.${focused}`) })}</p>
        <div className="flex flex-wrap gap-1" data-testid="sn-tokens">
          {tokens.map((token) => (
            <Tooltip key={token.token}>
              <TooltipTrigger asChild>
                <button type="button" onClick={() => appendToken(token.token)} className="rounded-md focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring">
                  <Badge variant={token.scope === 'episode' ? 'outline' : 'secondary'} className="cursor-pointer font-mono text-[11px]">
                    {token.token}
                  </Badge>
                </button>
              </TooltipTrigger>
              <TooltipContent className="max-w-xs">
                <p>{token.description}</p>
                <p className="font-mono text-xs opacity-80">{token.example}</p>
              </TooltipContent>
            </Tooltip>
          ))}
        </div>
      </div>

      <div className="flex items-center gap-2">
        <Button type="submit" disabled={save.isPending || !dirty || hasErrors} data-testid="sn-save">
          {save.isPending ? t('common.saving') : t('common.save')}
        </Button>
      </div>
    </form>
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
    seasonPackUpgrade: config.seasonPackUpgrade,
    seasonPackUpgradeThreshold: config.seasonPackUpgradeThreshold,
    replaceIllegalCharacters: config.replaceIllegalCharacters,
    colonReplacement: config.colonReplacement,
  }
}

function MediaManagementForm({ initial }: { initial: MediaManagement }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [form, setForm] = useState<MediaManagementRequest>(() => toRequest(initial))
  // The bin is emptied by the maintenance task, which only runs by hand while the scheduler is off.
  const schedulerOff = useSchedulerEnabled() === false
  const [sampleTitle, setSampleTitle] = useState(filmSampleTitle)
  const debouncedFile = useDebounced(form.fileNameTemplate, 300)
  const debouncedFolder = useDebounced(form.folderNameTemplate, 300)
  const debouncedCleaning = useDebounced({ replaceIllegalCharacters: form.replaceIllegalCharacters, colonReplacement: form.colonReplacement, sampleTitle }, 300)
  const preview = useQuery({
    queryKey: ['namingPreview', debouncedFile, debouncedFolder, debouncedCleaning],
    queryFn: () => mediaManagement.preview(debouncedFile, debouncedFolder, debouncedCleaning),
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

        <CleaningFields
          idPrefix="mm"
          replace={form.replaceIllegalCharacters}
          colon={form.colonReplacement}
          colons={filmColonReplacements}
          sampleTitle={sampleTitle}
          onReplace={(v) => setForm({ ...form, replaceIllegalCharacters: v })}
          onColon={(v) => setForm({ ...form, colonReplacement: v })}
          onSampleTitle={setSampleTitle}
        />

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
        <h2 className="text-xl font-semibold tracking-tight">{t('mediaManagement.seasonPacks')}</h2>
        <div className="grid max-w-xl gap-4 sm:grid-cols-[minmax(0,1fr)_10rem]">
          <div className="grid gap-2">
            <Label htmlFor="mm-pack-upgrade">{t('mediaManagement.seasonPackUpgrade')}</Label>
            <Select value={form.seasonPackUpgrade} onValueChange={(v) => setForm({ ...form, seasonPackUpgrade: v as SeasonPackUpgrade })}>
              <SelectTrigger id="mm-pack-upgrade" className="w-full" data-testid="mm-pack-upgrade">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {(['all', 'threshold', 'any'] as const).map((mode) => (
                  <SelectItem key={mode} value={mode}>
                    {t(`mediaManagement.seasonPackUpgradeModes.${mode}`)}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          {form.seasonPackUpgrade === 'threshold' && (
            <div className="grid gap-2">
              <Label htmlFor="mm-pack-threshold">{t('mediaManagement.seasonPackUpgradeThreshold')}</Label>
              <Input
                id="mm-pack-threshold"
                type="number"
                min={0}
                max={100}
                value={form.seasonPackUpgradeThreshold}
                onChange={(e) => setForm({ ...form, seasonPackUpgradeThreshold: e.target.value === '' ? 100 : Number(e.target.value) })}
                data-testid="mm-pack-threshold"
              />
            </div>
          )}
        </div>
        <span className="max-w-3xl text-xs text-muted-foreground">{t('mediaManagement.seasonPackUpgradeHint')}</span>
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
