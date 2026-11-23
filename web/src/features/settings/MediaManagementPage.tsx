import { useMemo, useState, type FormEvent, type KeyboardEvent, type ReactNode } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useNavigate, useSearch } from '@tanstack/react-router'
import { cn } from 'cn'
import { FilmIcon, LayersIcon, TvIcon, type LucideIcon } from 'lucide-react'
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
import { dirtyScopes, firstScopeWithError, scopes, type Scope } from './mediaManagementTabs'

const modes: TransferMode[] = ['hardlinkOrCopy', 'copy', 'move']

const scopeIcons: Record<Scope, LucideIcon> = { films: FilmIcon, series: TvIcon, both: LayersIcon }

/** Settings → Media management: what applies to films, to series and to both, one tab each, saved together. */
export function MediaManagementPage() {
  const { t } = useTranslation()
  const query = useQuery({ queryKey: ['mediaManagement'], queryFn: mediaManagement.get })
  const seriesNaming = useQuery({ queryKey: ['seriesNaming'], queryFn: mediaManagement.seriesNaming })
  const error = query.error ?? seriesNaming.error

  return (
    <>
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">{t('mediaManagement.title')}</h1>
        <p className="mt-1 max-w-3xl text-sm text-muted-foreground">{t('mediaManagement.hint')}</p>
      </div>
      {!error && (query.isPending || seriesNaming.isPending) && (
        <div className="flex flex-col gap-2">
          <Skeleton className="h-8 w-1/2" />
          <Skeleton className="h-8 w-full" />
          <Skeleton className="h-8 w-2/3" />
        </div>
      )}
      {error && (
        <Alert variant="destructive">
          <AlertDescription>{error instanceof ApiError ? error.message : t('common.unexpectedError')}</AlertDescription>
        </Alert>
      )}
      {query.data && seriesNaming.data && <MediaManagementForm film={query.data} series={seriesNaming.data} />}
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
    alignSeasonGroups: config.alignSeasonGroups,
  }
}

/** A titled group of settings inside a scope section. */
function Group({ title, children, testId }: { title: string; children: ReactNode; testId?: string }) {
  return (
    <div className="flex flex-col gap-4" data-testid={testId}>
      <h3 className="text-base font-medium">{title}</h3>
      {children}
    </div>
  )
}

const scopeTone: Record<Scope, string> = {
  films: 'border-sky-500/40 bg-sky-500/10 text-sky-700 dark:text-sky-300',
  series: 'border-violet-500/40 bg-violet-500/10 text-violet-700 dark:text-violet-300',
  both: 'border-emerald-500/40 bg-emerald-500/10 text-emerald-700 dark:text-emerald-300',
}

const tabId = (scope: Scope) => `mm-tab-${scope}`
const panelId = (scope: Scope) => `mm-panel-${scope}`

/**
 * The three tabs, one per scope. Three equal columns, so they never overflow a phone: the icon sits above the label there and the
 * longest label wraps. A dot marks a tab holding an unsaved edit (primary) or something to fix (red).
 */
function ScopeTabs({ active, dirty, invalid, onSelect }: { active: Scope; dirty: Record<Scope, boolean>; invalid: Record<Scope, boolean>; onSelect: (scope: Scope) => void }) {
  const { t } = useTranslation()
  const onKeyDown = (e: KeyboardEvent<HTMLDivElement>) => {
    const index = scopes.indexOf(active)
    const next = e.key === 'ArrowRight' ? (index + 1) % scopes.length : e.key === 'ArrowLeft' ? (index + scopes.length - 1) % scopes.length : e.key === 'Home' ? 0 : e.key === 'End' ? scopes.length - 1 : -1
    if (next < 0) return
    e.preventDefault()
    onSelect(scopes[next])
    document.getElementById(tabId(scopes[next]))?.focus()
  }
  return (
    <div role="tablist" aria-label={t('mediaManagement.title')} onKeyDown={onKeyDown} className="grid grid-cols-3 gap-1 rounded-lg border bg-muted/40 p-1" data-testid="scope-tabs">
      {scopes.map((scope) => {
        const Icon = scopeIcons[scope]
        const selected = scope === active
        const marker = invalid[scope] ? 'error' : dirty[scope] ? 'unsaved' : null
        return (
          <button
            key={scope}
            type="button"
            role="tab"
            id={tabId(scope)}
            aria-selected={selected}
            aria-controls={panelId(scope)}
            tabIndex={selected ? 0 : -1}
            onClick={() => onSelect(scope)}
            className={cn(
              'relative flex min-w-0 flex-col items-center justify-center gap-1 rounded-md px-2 py-2 text-center text-xs font-medium leading-tight transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring sm:flex-row sm:gap-2 sm:text-sm',
              selected ? 'bg-background shadow-sm' : 'text-muted-foreground hover:bg-background/60 hover:text-foreground',
            )}
            data-testid={`tab-${scope}`}
          >
            <span className={cn('flex size-6 shrink-0 items-center justify-center rounded-md border [&>svg]:size-3.5', scopeTone[scope])}>
              <Icon />
            </span>
            <span className="min-w-0">{t(`mediaManagement.scopes.${scope}`)}</span>
            {marker && (
              <>
                <span
                  aria-hidden
                  className={cn('absolute right-1.5 top-1.5 size-2 rounded-full', marker === 'error' ? 'bg-destructive' : 'bg-primary')}
                  data-testid={`tab-${scope}-${marker}`}
                />
                <span className="sr-only">{t(marker === 'error' ? 'mediaManagement.tabError' : 'mediaManagement.tabUnsaved')}</span>
              </>
            )}
          </button>
        )
      })}
    </div>
  )
}

/** One scope's panel: what it covers, then its groups. Only the open tab is rendered; the form state lives above, so nothing is lost on a switch. */
function ScopePanel({ scope, children }: { scope: Scope; children: ReactNode }) {
  const { t } = useTranslation()
  return (
    <section id={panelId(scope)} role="tabpanel" aria-labelledby={tabId(scope)} className="flex flex-col gap-6" data-testid={`scope-${scope}`}>
      <p className="max-w-3xl text-sm text-muted-foreground">{t(`mediaManagement.scopeHints.${scope}`)}</p>
      {children}
    </section>
  )
}

function MediaManagementForm({ film, series }: { film: MediaManagement; series: SeriesNaming }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [filmForm, setFilmForm] = useState<MediaManagementRequest>(() => toRequest(film))
  const [seriesForm, setSeriesForm] = useState<SeriesNamingRequest>(() => seriesRequest(series))
  // The bin is emptied by the maintenance task, which only runs by hand while the scheduler is off.
  const schedulerOff = useSchedulerEnabled() === false
  const [filmSample, setFilmSample] = useState(filmSampleTitle)
  const [seriesSample, setSeriesSample] = useState(seriesSampleTitle)
  const [focused, setFocused] = useState<SeriesTemplateField>('standardEpisodeTemplate')
  // The open tab lives in the URL (`?tab=series`), so a link opens the right one; films, the default, keeps the URL bare.
  const tab: Scope = useSearch({ from: '/shell/app/settings/mediamanagement' }).tab ?? 'films'
  const navigate = useNavigate()
  const selectTab = (scope: Scope) => void navigate({ to: '/settings/mediamanagement', search: scope === 'films' ? {} : { tab: scope }, replace: true })

  const debouncedFile = useDebounced(filmForm.fileNameTemplate, 300)
  const debouncedFolder = useDebounced(filmForm.folderNameTemplate, 300)
  const cleaning = useMemo(
    () => ({ replaceIllegalCharacters: filmForm.replaceIllegalCharacters, colonReplacement: filmForm.colonReplacement, sampleTitle: filmSample }),
    [filmForm.replaceIllegalCharacters, filmForm.colonReplacement, filmSample],
  )
  const debouncedCleaning = useDebounced(cleaning, 300)
  const filmPreview = useQuery({
    queryKey: ['namingPreview', debouncedFile, debouncedFolder, debouncedCleaning],
    queryFn: () => mediaManagement.preview(debouncedFile, debouncedFolder, debouncedCleaning),
    staleTime: Number.POSITIVE_INFINITY,
    placeholderData: (previous) => previous,
  })
  const seriesInput = useMemo(() => ({ ...seriesForm, sampleTitle: seriesSample }), [seriesForm, seriesSample])
  const debouncedSeries = useDebounced(seriesInput, 300)
  const seriesPreview = useQuery({
    queryKey: ['seriesNamingPreview', debouncedSeries],
    queryFn: () => mediaManagement.previewSeriesNaming(debouncedSeries),
    staleTime: Number.POSITIVE_INFINITY,
    placeholderData: (previous) => previous,
  })

  const filmDirty = JSON.stringify(filmForm) !== JSON.stringify(toRequest(film))
  const seriesDirty = JSON.stringify(seriesForm) !== JSON.stringify(seriesRequest(series))
  const dirty = filmDirty || seriesDirty
  const dirtyTabs = dirtyScopes(filmForm, toRequest(film), seriesDirty)

  // Two requests, as the API has two resources: each part that changed is saved, and the form takes back what the server kept.
  const save = useMutation({
    mutationFn: async () => {
      const saved: { film?: MediaManagement; series?: SeriesNaming } = {}
      if (filmDirty) {
        saved.film = await mediaManagement.update({ ...filmForm, recycleBinPath: filmForm.recycleBinPath?.trim() ? filmForm.recycleBinPath.trim() : null })
        queryClient.setQueryData(['mediaManagement'], saved.film)
        setFilmForm(toRequest(saved.film))
      }
      if (seriesDirty) {
        saved.series = await mediaManagement.updateSeriesNaming(seriesForm)
        queryClient.setQueryData(['seriesNaming'], saved.series)
        setSeriesForm(seriesRequest(saved.series))
      }
      return saved
    },
    onSuccess: () => toast.success(t('mediaManagement.saved')),
    onError: (e) => toast.error(e instanceof ApiError ? e.message : t('mediaManagement.saveFailed')),
  })
  const backfill = useMutation({
    mutationFn: mediaManagement.backfill,
    onSuccess: (result) => toast.success(t('mediaManagement.backfillDone', { count: result.probed, remaining: result.remaining })),
    onError: (e) => toast.error(e instanceof ApiError ? e.message : t('common.unexpectedError')),
  })

  const fileErrors = filmPreview.data?.fileErrors ?? []
  const folderErrors = filmPreview.data?.folderErrors ?? []
  const errors = seriesPreview.data?.errors ?? {}
  const seriesHasErrors = Object.values(errors).some((e) => e.length > 0)
  // Nothing to validate on the shared tab: its fields take any value the API accepts.
  const invalidTabs: Record<Scope, boolean> = { films: fileErrors.length > 0 || folderErrors.length > 0, series: seriesHasErrors, both: false }
  const firstInvalid = firstScopeWithError(invalidTabs)

  const fileField = focused === 'standardEpisodeTemplate' || focused === 'dailyEpisodeTemplate' || focused === 'animeEpisodeTemplate'
  const seriesTokens = series.tokens.filter((token) => (fileField ? true : focused === 'seriesFolderTemplate' ? token.scope === 'series' : token.scope !== 'episode'))
  const appendFilmToken = (token: string) => setFilmForm((f) => ({ ...f, fileNameTemplate: `${f.fileNameTemplate.trimEnd()} ${token}`.trim() }))
  const appendSeriesToken = (token: string) => setSeriesForm((f) => ({ ...f, [focused]: `${f[focused].trimEnd()} ${token}`.trim() }))

  // Save stays available while something is invalid: it then opens the first tab with an error instead of saving.
  const submit = (e: FormEvent) => {
    e.preventDefault()
    if (firstInvalid) {
      selectTab(firstInvalid)
      toast.error(t('mediaManagement.fixErrors'))
      return
    }
    save.mutate()
  }

  const seriesField = (key: SeriesTemplateField, errorKey: string, previewValue: string | null | undefined, extra?: string | null) => (
    <div className="grid gap-2">
      <Label htmlFor={`sn-${key}`}>{t(`seriesNaming.${key}`)}</Label>
      <Input
        id={`sn-${key}`}
        value={seriesForm[key]}
        onFocus={() => setFocused(key)}
        onChange={(e) => setSeriesForm({ ...seriesForm, [key]: e.target.value })}
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

  // ---- films -------------------------------------------------------------------------------------------------------

  const filmNaming = (
    <Group title={t('mediaManagement.filmNaming')} testId="film-naming">
      <div className="flex items-start gap-2">
        <Checkbox id="mm-rename" checked={filmForm.renameFiles} onCheckedChange={(v) => setFilmForm({ ...filmForm, renameFiles: v === true })} className="mt-0.5" />
        <div className="grid gap-0.5">
          <Label htmlFor="mm-rename" className="font-normal">
            {t('mediaManagement.renameFiles')}
          </Label>
          <span className="text-xs text-muted-foreground">{t('mediaManagement.renameFilesHint')}</span>
        </div>
      </div>

      <CleaningFields
        idPrefix="mm"
        replace={filmForm.replaceIllegalCharacters}
        colon={filmForm.colonReplacement}
        colons={filmColonReplacements}
        sampleTitle={filmSample}
        onReplace={(v) => setFilmForm({ ...filmForm, replaceIllegalCharacters: v })}
        onColon={(v) => setFilmForm({ ...filmForm, colonReplacement: v })}
        onSampleTitle={setFilmSample}
      />

      <div className="grid gap-2">
        <Label htmlFor="mm-file-template">{t('mediaManagement.fileTemplate')}</Label>
        <Input
          id="mm-file-template"
          value={filmForm.fileNameTemplate}
          onChange={(e) => setFilmForm({ ...filmForm, fileNameTemplate: e.target.value })}
          className={cn('font-mono text-xs', fileErrors.length > 0 && 'border-destructive')}
          spellCheck={false}
          data-testid="mm-file-template"
        />
        <TemplatePreview label={t('mediaManagement.preview')} value={filmPreview.data?.fileName ?? null} errors={fileErrors} testId="mm-file-preview" />
      </div>

      <div className="grid gap-2">
        <Label htmlFor="mm-folder-template">{t('mediaManagement.folderTemplate')}</Label>
        <Input
          id="mm-folder-template"
          value={filmForm.folderNameTemplate}
          onChange={(e) => setFilmForm({ ...filmForm, folderNameTemplate: e.target.value })}
          className={cn('font-mono text-xs', folderErrors.length > 0 && 'border-destructive')}
          spellCheck={false}
        />
        <TemplatePreview label={t('mediaManagement.preview')} value={filmPreview.data?.folderName ?? null} errors={folderErrors} testId="mm-folder-preview" />
        <span className="text-xs text-muted-foreground">{t('mediaManagement.folderTemplateHint')}</span>
      </div>

      <div className="grid gap-2">
        <span className="text-sm font-medium">{t('mediaManagement.tokens')}</span>
        <p className="max-w-3xl text-xs text-muted-foreground">{t('mediaManagement.tokensHint')}</p>
        <div className="flex flex-wrap gap-1" data-testid="mm-tokens">
          {film.tokens.map((token) => (
            <Tooltip key={token.token}>
              <TooltipTrigger asChild>
                <button type="button" onClick={() => appendFilmToken(token.token)} className="rounded-md focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring">
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
    </Group>
  )

  // ---- series ------------------------------------------------------------------------------------------------------

  const seriesNamingGroup = (
    <Group title={t('seriesNaming.title')} testId="series-naming-form">
      <p className="-mt-2 max-w-3xl text-xs text-muted-foreground">{t('seriesNaming.hint')}</p>

      <div className="flex items-start gap-2">
        <Checkbox id="sn-rename" checked={seriesForm.renameEpisodes} onCheckedChange={(v) => setSeriesForm({ ...seriesForm, renameEpisodes: v === true })} className="mt-0.5" />
        <div className="grid gap-0.5">
          <Label htmlFor="sn-rename" className="font-normal">
            {t('seriesNaming.renameEpisodes')}
          </Label>
          <span className="text-xs text-muted-foreground">{t('seriesNaming.renameEpisodesHint')}</span>
        </div>
      </div>

      <CleaningFields
        idPrefix="sn"
        replace={seriesForm.replaceIllegalCharacters}
        colon={seriesForm.colonReplacement}
        custom={seriesForm.customColonReplacement}
        colons={seriesColonReplacements}
        customErrors={errors.customColonReplacement}
        sampleTitle={seriesSample}
        onReplace={(v) => setSeriesForm({ ...seriesForm, replaceIllegalCharacters: v })}
        onColon={(v) => setSeriesForm({ ...seriesForm, colonReplacement: v })}
        onCustom={(v) => setSeriesForm({ ...seriesForm, customColonReplacement: v })}
        onSampleTitle={setSeriesSample}
      />

      {seriesField('standardEpisodeTemplate', 'standardEpisode', seriesPreview.data?.standardEpisode, seriesPreview.data?.multiEpisode)}
      {seriesField('dailyEpisodeTemplate', 'dailyEpisode', seriesPreview.data?.dailyEpisode)}
      {seriesField('animeEpisodeTemplate', 'animeEpisode', seriesPreview.data?.animeEpisode)}

      <div className="grid max-w-md gap-2">
        <Label htmlFor="sn-style">{t('seriesNaming.multiEpisodeStyle')}</Label>
        <Select value={seriesForm.multiEpisodeStyle} onValueChange={(v) => setSeriesForm({ ...seriesForm, multiEpisodeStyle: v as MultiEpisodeStyle })}>
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

      {seriesField('seriesFolderTemplate', 'seriesFolder', seriesPreview.data?.seriesFolder)}
      <span className="-mt-2 text-xs text-muted-foreground">{t('seriesNaming.seriesFolderHint')}</span>
      {seriesField('seasonFolderTemplate', 'seasonFolder', seriesPreview.data?.seasonFolder)}
      {seriesField('specialsFolderName', 'specialsFolder', seriesPreview.data?.specialsFolder)}

      <div className="grid gap-2">
        <span className="text-sm font-medium">{t('mediaManagement.tokens')}</span>
        <p className="max-w-3xl text-xs text-muted-foreground">{t('seriesNaming.tokensHint', { field: t(`seriesNaming.${focused}`) })}</p>
        <div className="flex flex-wrap gap-1" data-testid="sn-tokens">
          {seriesTokens.map((token) => (
            <Tooltip key={token.token}>
              <TooltipTrigger asChild>
                <button type="button" onClick={() => appendSeriesToken(token.token)} className="rounded-md focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring">
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
    </Group>
  )

  const seasonPacks = (
    <Group title={t('mediaManagement.seasonPacks')} testId="season-packs">
      <div className="grid max-w-xl gap-4 sm:grid-cols-[minmax(0,1fr)_10rem]">
        <div className="grid content-start gap-2">
          <Label htmlFor="mm-pack-upgrade">{t('mediaManagement.seasonPackUpgrade')}</Label>
          <Select value={filmForm.seasonPackUpgrade} onValueChange={(v) => setFilmForm({ ...filmForm, seasonPackUpgrade: v as SeasonPackUpgrade })}>
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
        {filmForm.seasonPackUpgrade === 'threshold' && (
          <div className="grid content-start gap-2">
            <Label htmlFor="mm-pack-threshold">{t('mediaManagement.seasonPackUpgradeThreshold')}</Label>
            <Input
              id="mm-pack-threshold"
              type="number"
              min={0}
              max={100}
              value={filmForm.seasonPackUpgradeThreshold}
              onChange={(e) => setFilmForm({ ...filmForm, seasonPackUpgradeThreshold: e.target.value === '' ? 100 : Number(e.target.value) })}
              data-testid="mm-pack-threshold"
            />
          </div>
        )}
      </div>
      <span className="-mt-2 max-w-3xl text-xs text-muted-foreground">{t('mediaManagement.seasonPackUpgradeHint')}</span>
    </Group>
  )

  const seasonGroups = (
    <Group title={t('mediaManagement.seasonGroups')} testId="season-groups">
      <div className="flex items-start gap-2">
        <Checkbox
          id="mm-align-groups"
          checked={filmForm.alignSeasonGroups}
          onCheckedChange={(v) => setFilmForm({ ...filmForm, alignSeasonGroups: v === true })}
          className="mt-0.5"
          data-testid="mm-align-groups"
        />
        <div className="grid gap-0.5">
          <Label htmlFor="mm-align-groups" className="font-normal">
            {t('mediaManagement.alignSeasonGroups')}
          </Label>
          <span className="max-w-3xl text-xs text-muted-foreground">{t('mediaManagement.alignSeasonGroupsHint')}</span>
        </div>
      </div>
    </Group>
  )

  // ---- films and series --------------------------------------------------------------------------------------------

  const transfer = (
    <Group title={t('mediaManagement.transfer')} testId="transfer">
      <div className="grid max-w-md gap-2">
        <Label htmlFor="mm-mode">{t('mediaManagement.transferMode')}</Label>
        <Select value={filmForm.transferMode} onValueChange={(v) => setFilmForm({ ...filmForm, transferMode: v as TransferMode })}>
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
    </Group>
  )

  const extraFiles = (
    <Group title={t('mediaManagement.extraFiles')} testId="extra-files">
      <div className="flex items-start gap-2">
        <Checkbox
          id="mm-extras"
          checked={filmForm.importExtraFiles}
          onCheckedChange={(v) => setFilmForm({ ...filmForm, importExtraFiles: v === true })}
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
          value={filmForm.extraFileExtensions}
          onChange={(e) => setFilmForm({ ...filmForm, extraFileExtensions: e.target.value })}
          disabled={!filmForm.importExtraFiles}
          className="font-mono text-xs"
          spellCheck={false}
          data-testid="mm-extra-extensions"
        />
        <span className="text-xs text-muted-foreground">{t('mediaManagement.extraFileExtensionsHint')}</span>
      </div>
    </Group>
  )

  const recycleBin = (
    <Group title={t('mediaManagement.recycleBin')} testId="recycle-bin">
      <div className="grid gap-4 sm:grid-cols-[minmax(0,1fr)_10rem]">
        <div className="grid content-start gap-2">
          <Label htmlFor="mm-bin">{t('mediaManagement.recycleBinPath')}</Label>
          <Input id="mm-bin" value={filmForm.recycleBinPath ?? ''} onChange={(e) => setFilmForm({ ...filmForm, recycleBinPath: e.target.value })} placeholder="/data/recycle" data-testid="mm-bin" />
          <span className="text-xs text-muted-foreground">{t('mediaManagement.recycleBinPathHint')}</span>
        </div>
        <div className="grid content-start gap-2">
          <Label htmlFor="mm-retention">{t('mediaManagement.retention')}</Label>
          <Input
            id="mm-retention"
            type="number"
            min={0}
            max={3650}
            value={filmForm.recycleBinRetentionDays}
            onChange={(e) => setFilmForm({ ...filmForm, recycleBinRetentionDays: Number(e.target.value) })}
          />
          <span className="text-xs text-muted-foreground">{t(schedulerOff ? 'mediaManagement.retentionHintManual' : 'mediaManagement.retentionHint')}</span>
        </div>
      </div>
    </Group>
  )

  const mediaInfo = (
    <Group title={t('mediaManagement.mediaInfo')} testId="media-info">
      {film.mediaInfoAvailable ? (
        <p className="-mt-2 text-sm text-muted-foreground">{t('mediaManagement.mediaInfoAvailable', { path: film.ffprobePath })}</p>
      ) : (
        <Alert>
          <AlertDescription>{t('mediaManagement.mediaInfoMissing')}</AlertDescription>
        </Alert>
      )}
      <div>
        <Button type="button" variant="outline" size="sm" onClick={() => backfill.mutate()} disabled={!film.mediaInfoAvailable || backfill.isPending}>
          {backfill.isPending ? t('mediaManagement.backfilling') : t('mediaManagement.backfill')}
        </Button>
      </div>
    </Group>
  )

  return (
    <form className="flex flex-col gap-6" onSubmit={submit} data-testid="mediamanagement-form">
      <ScopeTabs active={tab} dirty={dirtyTabs} invalid={invalidTabs} onSelect={selectTab} />

      {tab === 'films' && <ScopePanel scope="films">{filmNaming}</ScopePanel>}

      {tab === 'series' && (
        <ScopePanel scope="series">
          {seriesNamingGroup}
          {seasonPacks}
          {seasonGroups}
        </ScopePanel>
      )}

      {tab === 'both' && (
        <ScopePanel scope="both">
          {transfer}
          {extraFiles}
          {recycleBin}
          {mediaInfo}
        </ScopePanel>
      )}

      <div className="sticky bottom-0 z-10 -mx-1 flex items-center gap-3 border-t bg-background/95 px-1 py-3 backdrop-blur" data-testid="save-bar">
        <Button type="submit" disabled={save.isPending || !dirty} data-testid="mm-save">
          {save.isPending ? t('common.saving') : t('common.save')}
        </Button>
        {firstInvalid ? (
          <span className="text-sm text-destructive" data-testid="mm-invalid">
            {t('mediaManagement.hasErrors')}
          </span>
        ) : (
          dirty && <span className="text-sm text-muted-foreground">{t('mediaManagement.unsaved')}</span>
        )}
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
