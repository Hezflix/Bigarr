import { useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { cn } from 'cn'
import { FlaskConicalIcon, PencilIcon, PlusIcon, Trash2Icon } from 'lucide-react'
import type { TFunction } from 'i18next'
import { useTranslation } from 'react-i18next'
import { formatTime } from '@/i18n'
import { toast } from 'sonner'
import { ApiError, indexers, type Indexer, type IndexerImplementation, type IndexerRequest, type IndexerTestResult } from '@/api/client'
import { Alert, AlertDescription } from '@/components/ui/alert'
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'

const DEFAULT_CATEGORIES = [2000, 2010, 2020, 2030, 2040, 2045, 2050, 2060, 2070, 2080]

const implementations: IndexerImplementation[] = ['torznab', 'mock']

const emptyForm = (): IndexerRequest => ({
  name: '',
  implementation: 'torznab',
  baseUrl: '',
  apiPath: '/api',
  apiKey: '',
  categories: DEFAULT_CATEGORIES,
  priority: 25,
  enableRss: true,
  enableAutomaticSearch: true,
  enableInteractiveSearch: true,
  minimumSeeders: 1,
  seedRatio: null,
  seedTimeMinutes: null,
  rateLimitSeconds: 2,
  tvCategories: [],
  animeCategories: [],
})

const toForm = (indexer: Indexer): IndexerRequest => ({
  name: indexer.name,
  implementation: indexer.implementation,
  baseUrl: indexer.baseUrl ?? '',
  apiPath: indexer.apiPath,
  apiKey: indexer.apiKey ?? '',
  categories: indexer.categories,
  priority: indexer.priority,
  enableRss: indexer.enableRss,
  enableAutomaticSearch: indexer.enableAutomaticSearch,
  enableInteractiveSearch: indexer.enableInteractiveSearch,
  minimumSeeders: indexer.minimumSeeders,
  seedRatio: indexer.seedRatio,
  seedTimeMinutes: indexer.seedTimeMinutes,
  rateLimitSeconds: indexer.rateLimitSeconds,
  tvCategories: indexer.tvCategories,
  animeCategories: indexer.animeCategories,
})

function parseCategories(text: string): number[] {
  const ids = text
    .split(/[\s,;]+/)
    .map((v) => Number.parseInt(v, 10))
    .filter((v) => Number.isFinite(v) && v > 0)
  return [...new Set(ids)].sort((a, b) => a - b)
}

function testSummary(t: TFunction, result: IndexerTestResult): string {
  const ids = [result.supportsTmdbSearch && 'TMDB', result.supportsImdbSearch && 'IMDb'].filter(Boolean).join(' / ')
  const movies = t('indexers.testOk', { categories: result.categoryCount, releases: result.recentReleases, ids: ids || t('indexers.textSearchOnly') })
  const tvIds = (result.tvSearchParams ?? []).filter((p) => p.endsWith('id')).map((p) => p.replace(/id$/, '').toUpperCase()).join(' / ')
  return `${movies} ${result.supportsTvSearch ? t('indexers.testTv', { ids: tvIds || t('indexers.textSearchOnly') }) : t('indexers.testNoTv')}`
}


export function IndexersSettingsPage() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const list = useQuery({ queryKey: ['indexers'], queryFn: indexers.list, refetchInterval: 30_000 })
  const [editing, setEditing] = useState<Indexer | 'new' | null>(null)
  const [pendingRemove, setPendingRemove] = useState<Indexer | null>(null)
  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['indexers'] })
  const fail = (e: unknown, fallback: string) => toast.error(e instanceof ApiError ? e.message : fallback)

  const remove = useMutation({
    mutationFn: (id: number) => indexers.remove(id),
    onSuccess: async () => {
      setPendingRemove(null)
      await invalidate()
    },
    onError: (e) => {
      setPendingRemove(null)
      fail(e, t('indexers.removeFailed'))
    },
  })
  const test = useMutation({
    mutationFn: (id: number) => indexers.testSaved(id),
    onSuccess: (result) => {
      toast.success(testSummary(t, result))
      void invalidate()
    },
    onError: (e) => {
      fail(e, t('indexers.testFailed'))
      void invalidate()
    },
  })

  return (
    <>
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="text-2xl font-semibold tracking-tight">{t('indexers.title')}</h1>
        <Button type="button" size="sm" className="ml-auto" onClick={() => setEditing('new')} data-testid="add-indexer">
          <PlusIcon />
          {t('indexers.add')}
        </Button>
      </div>
      <p className="max-w-3xl text-sm text-muted-foreground">{t('indexers.hint')}</p>

      <p className="max-w-3xl rounded-lg border px-4 py-3 text-sm text-muted-foreground" data-testid="prowlarr-hint">
        {t('indexers.prowlarrHint')}{' '}
        <Link to="/settings/connect" className="underline underline-offset-4 hover:text-foreground">
          {t('indexers.prowlarrHowTo')}
        </Link>
      </p>

      <div className="overflow-x-auto rounded-lg border">
        <Table data-testid="indexers-table">
          <TableHeader>
            <TableRow>
              <TableHead>{t('indexers.name')}</TableHead>
              <TableHead>{t('indexers.implementation')}</TableHead>
              <TableHead>{t('indexers.priority')}</TableHead>
              <TableHead>{t('indexers.enabledFor')}</TableHead>
              <TableHead>{t('indexers.status')}</TableHead>
              <TableHead className="text-right">{t('versions.actions')}</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {list.data?.map((indexer) => (
              <TableRow key={indexer.id} data-testid={`indexer-${indexer.id}`}>
                <TableCell className="align-top">
                  <span className="flex flex-wrap items-center gap-2 font-medium">
                    {indexer.name}
                    {indexer.prowlarrId !== null && (
                      <Tooltip>
                        <TooltipTrigger asChild>
                          <Badge variant="secondary">{t('indexers.prowlarrBadge')}</Badge>
                        </TooltipTrigger>
                        <TooltipContent>{t('indexers.prowlarrManaged')}</TooltipContent>
                      </Tooltip>
                    )}
                  </span>
                  {indexer.baseUrl && (
                    <span className="block max-w-[32ch] truncate text-xs text-muted-foreground" title={indexer.baseUrl}>
                      {indexer.baseUrl}
                    </span>
                  )}
                </TableCell>
                <TableCell className="align-top">{t(`indexers.implementations.${indexer.implementation}`)}</TableCell>
                <TableCell className="align-top">{indexer.priority}</TableCell>
                <TableCell className="align-top">
                  <span className="flex flex-wrap gap-1">
                    {indexer.enableRss && <Badge variant="outline">{t('indexers.rss')}</Badge>}
                    {indexer.enableAutomaticSearch && <Badge variant="outline">{t('indexers.automatic')}</Badge>}
                    {indexer.enableInteractiveSearch && <Badge variant="outline">{t('indexers.interactive')}</Badge>}
                  </span>
                </TableCell>
                <TableCell className="align-top">
                  <IndexerStatusBadge indexer={indexer} />
                </TableCell>
                <TableCell className="align-top">
                  <div className="flex items-center justify-end gap-1">
                    <Tooltip>
                      <TooltipTrigger asChild>
                        <Button
                          type="button"
                          variant="ghost"
                          size="icon"
                          onClick={() => test.mutate(indexer.id)}
                          disabled={test.isPending}
                          aria-label={t('indexers.test')}
                        >
                          <FlaskConicalIcon className={test.isPending && test.variables === indexer.id ? 'animate-pulse' : undefined} />
                        </Button>
                      </TooltipTrigger>
                      <TooltipContent>{t('indexers.test')}</TooltipContent>
                    </Tooltip>
                    <Tooltip>
                      <TooltipTrigger asChild>
                        <Button type="button" variant="ghost" size="icon" onClick={() => setEditing(indexer)} aria-label={t('indexers.edit')}>
                          <PencilIcon />
                        </Button>
                      </TooltipTrigger>
                      <TooltipContent>{t('indexers.edit')}</TooltipContent>
                    </Tooltip>
                    <Tooltip>
                      <TooltipTrigger asChild>
                        <Button
                          type="button"
                          variant="ghost"
                          size="icon"
                          className="text-destructive hover:text-destructive"
                          onClick={() => setPendingRemove(indexer)}
                          aria-label={t('indexers.delete')}
                        >
                          <Trash2Icon />
                        </Button>
                      </TooltipTrigger>
                      <TooltipContent>{t('indexers.delete')}</TooltipContent>
                    </Tooltip>
                  </div>
                </TableCell>
              </TableRow>
            ))}
            {list.data?.length === 0 && (
              <TableRow>
                <TableCell colSpan={6} className="text-muted-foreground">
                  {t('indexers.empty')}
                </TableCell>
              </TableRow>
            )}
          </TableBody>
        </Table>
      </div>

      {editing !== null && (
        <IndexerDialog
          key={editing === 'new' ? 'new' : editing.id}
          indexer={editing === 'new' ? null : editing}
          onClose={() => setEditing(null)}
          onSaved={() => void invalidate()}
        />
      )}

      <AlertDialog open={pendingRemove !== null} onOpenChange={(open) => !open && setPendingRemove(null)}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>{t('indexers.deleteTitle')}</AlertDialogTitle>
            <AlertDialogDescription>{t('indexers.confirmDelete', { name: pendingRemove?.name ?? '' })}</AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>{t('common.cancel')}</AlertDialogCancel>
            <AlertDialogAction
              onClick={(event) => {
                event.preventDefault()
                if (pendingRemove) remove.mutate(pendingRemove.id)
              }}
              disabled={remove.isPending}
            >
              {t('indexers.delete')}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </>
  )
}

function IndexerStatusBadge({ indexer }: { indexer: Indexer }) {
  const { t } = useTranslation()
  const { status } = indexer
  if (status.healthy) {
    return (
      <Badge variant="outline" className="border-emerald-600/30 bg-emerald-500/15 text-emerald-700 dark:border-emerald-400/30 dark:text-emerald-300">
        {t('indexers.healthy')}
      </Badge>
    )
  }

  return (
    <Tooltip>
      <TooltipTrigger asChild>
        <Badge variant="outline" className="border-amber-600/30 bg-amber-500/15 text-amber-700 dark:border-amber-400/30 dark:text-amber-300">
          {t('indexers.paused', { until: status.disabledUntil ? formatTime(status.disabledUntil) : '' })}
        </Badge>
      </TooltipTrigger>
      <TooltipContent className="max-w-xs">
        {t('indexers.failures', { count: status.failures })}
        {status.lastError ? ` · ${status.lastError}` : ''}
      </TooltipContent>
    </Tooltip>
  )
}

function IndexerDialog({ indexer, onClose, onSaved }: { indexer: Indexer | null; onClose: () => void; onSaved: () => void }) {
  const { t } = useTranslation()
  const [form, setForm] = useState<IndexerRequest>(indexer ? toForm(indexer) : emptyForm())
  const [categories, setCategories] = useState(form.categories.join(', '))
  const [tvCategories, setTvCategories] = useState(form.tvCategories.join(', '))
  const [animeCategories, setAnimeCategories] = useState(form.animeCategories.join(', '))
  const set = <K extends keyof IndexerRequest>(key: K, value: IndexerRequest[K]) => setForm((f) => ({ ...f, [key]: value }))
  const isTorznab = form.implementation === 'torznab'

  const payload = (): IndexerRequest => ({
    ...form,
    name: form.name.trim(),
    baseUrl: isTorznab ? form.baseUrl?.trim() || null : null,
    apiPath: isTorznab ? form.apiPath?.trim() || '/api' : '/api',
    apiKey: isTorznab ? form.apiKey?.trim() || null : null,
    categories: parseCategories(categories),
    tvCategories: parseCategories(tvCategories),
    animeCategories: parseCategories(animeCategories),
  })

  const save = useMutation({
    mutationFn: () => (indexer ? indexers.update(indexer.id, payload()) : indexers.add(payload())),
    onSuccess: () => {
      onSaved()
      onClose()
    },
  })
  const test = useMutation({
    mutationFn: () => indexers.test(payload()),
    onSuccess: (result) => {
      toast.success(testSummary(t, result))
      // Empty series fields take what the caps list, so the user sees (and can trim) what searches will use.
      if (!tvCategories.trim() && result.tvCategories?.length) setTvCategories(result.tvCategories.join(', '))
      if (!animeCategories.trim() && result.animeCategories?.length) setAnimeCategories(result.animeCategories.join(', '))
    },
    onError: (e) => toast.error(e instanceof ApiError ? e.message : t('indexers.testFailed')),
  })

  const submit = (e: FormEvent) => {
    e.preventDefault()
    save.mutate()
  }

  const canSubmit = form.name.trim().length > 0 && (!isTorznab || (form.baseUrl ?? '').trim().length > 0)
  const number = (value: string, fallback: number) => (value === '' ? fallback : Number(value))
  const optionalNumber = (value: string) => (value === '' ? null : Number(value))

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="sm:max-w-2xl">
        <form onSubmit={submit} data-testid="indexer-form">
          <DialogHeader>
            <DialogTitle>{indexer ? t('indexers.editTitle', { name: indexer.name }) : t('indexers.new')}</DialogTitle>
          </DialogHeader>
          {indexer?.prowlarrId !== null && indexer?.prowlarrId !== undefined && (
            <Alert className="mt-4">
              <AlertDescription>{t('indexers.prowlarrManaged')}</AlertDescription>
            </Alert>
          )}

          <div className="my-6 grid gap-4 sm:grid-cols-2">
            <div className="grid grid-cols-1 gap-2">
              <Label htmlFor="indexer-name">{t('indexers.name')}</Label>
              <Input id="indexer-name" value={form.name} onChange={(e) => set('name', e.target.value)} placeholder="Prowlarr" autoFocus />
            </div>

            <div className="grid grid-cols-1 gap-2">
              <Label htmlFor="indexer-implementation">{t('indexers.implementation')}</Label>
              <Select value={form.implementation} onValueChange={(value) => set('implementation', value as IndexerImplementation)}>
                <SelectTrigger id="indexer-implementation" className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {implementations.map((i) => (
                    <SelectItem key={i} value={i}>
                      {t(`indexers.implementations.${i}`)}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              {!isTorznab && <p className="text-xs text-muted-foreground">{t('indexers.mockHint')}</p>}
            </div>

            {isTorznab && (
              <>
                <div className="grid grid-cols-1 gap-2 sm:col-span-2">
                  <Label htmlFor="indexer-url">{t('indexers.url')}</Label>
                  <Input
                    id="indexer-url"
                    value={form.baseUrl ?? ''}
                    onChange={(e) => set('baseUrl', e.target.value)}
                    placeholder="http://prowlarr:9696/1"
                    inputMode="url"
                  />
                  <p className="text-xs text-muted-foreground">{t('indexers.urlHint')}</p>
                </div>
                <div className="grid grid-cols-1 gap-2">
                  <Label htmlFor="indexer-apipath">{t('indexers.apiPath')}</Label>
                  <Input id="indexer-apipath" value={form.apiPath} onChange={(e) => set('apiPath', e.target.value)} placeholder="/api" />
                </div>
                <div className="grid grid-cols-1 gap-2">
                  <Label htmlFor="indexer-apikey">{t('indexers.apiKey')}</Label>
                  <Input
                    id="indexer-apikey"
                    type="password"
                    autoComplete="off"
                    value={form.apiKey ?? ''}
                    onChange={(e) => set('apiKey', e.target.value)}
                  />
                </div>
              </>
            )}

            <div className="grid grid-cols-1 gap-2 sm:col-span-2">
              <Label htmlFor="indexer-categories">{t('indexers.categories')}</Label>
              <Input id="indexer-categories" value={categories} onChange={(e) => setCategories(e.target.value)} placeholder={DEFAULT_CATEGORIES.join(', ')} />
              <p className="text-xs text-muted-foreground">{t('indexers.categoriesHint')}</p>
            </div>

            <div className="grid grid-cols-1 gap-2">
              <Label htmlFor="indexer-tv-categories">{t('indexers.tvCategories')}</Label>
              <Input
                id="indexer-tv-categories"
                value={tvCategories}
                onChange={(e) => setTvCategories(e.target.value)}
                placeholder={t('indexers.fromCaps')}
                data-testid="indexer-tv-categories"
              />
            </div>
            <div className="grid grid-cols-1 gap-2">
              <Label htmlFor="indexer-anime-categories">{t('indexers.animeCategories')}</Label>
              <Input
                id="indexer-anime-categories"
                value={animeCategories}
                onChange={(e) => setAnimeCategories(e.target.value)}
                placeholder={t('indexers.fromCaps')}
                data-testid="indexer-anime-categories"
              />
            </div>
            <p className="text-xs text-muted-foreground sm:col-span-2">{t('indexers.seriesCategoriesHint')}</p>

            <div className="grid grid-cols-1 gap-2">
              <Label htmlFor="indexer-priority">{t('indexers.priority')}</Label>
              <Input
                id="indexer-priority"
                type="number"
                min={1}
                max={50}
                value={form.priority}
                onChange={(e) => set('priority', number(e.target.value, 25))}
              />
              <p className="text-xs text-muted-foreground">{t('indexers.priorityHint')}</p>
            </div>

            <div className="grid grid-cols-1 gap-2">
              <Label htmlFor="indexer-seeders">{t('indexers.minimumSeeders')}</Label>
              <Input
                id="indexer-seeders"
                type="number"
                min={0}
                value={form.minimumSeeders}
                onChange={(e) => set('minimumSeeders', number(e.target.value, 0))}
              />
            </div>

            <div className="grid grid-cols-1 gap-2">
              <Label htmlFor="indexer-ratelimit">{t('indexers.rateLimit')}</Label>
              <Input
                id="indexer-ratelimit"
                type="number"
                min={0}
                max={3600}
                value={form.rateLimitSeconds}
                onChange={(e) => set('rateLimitSeconds', number(e.target.value, 0))}
              />
              <p className="text-xs text-muted-foreground">{t('indexers.rateLimitHint')}</p>
            </div>

            <div className="grid grid-cols-2 gap-2">
              <div className="grid grid-cols-1 gap-2">
                <Label htmlFor="indexer-seedratio">{t('indexers.seedRatio')}</Label>
                <Input
                  id="indexer-seedratio"
                  type="number"
                  min={0}
                  step={0.1}
                  value={form.seedRatio ?? ''}
                  onChange={(e) => set('seedRatio', optionalNumber(e.target.value))}
                />
              </div>
              <div className="grid grid-cols-1 gap-2">
                <Label htmlFor="indexer-seedtime">{t('indexers.seedTime')}</Label>
                <Input
                  id="indexer-seedtime"
                  type="number"
                  min={0}
                  value={form.seedTimeMinutes ?? ''}
                  onChange={(e) => set('seedTimeMinutes', optionalNumber(e.target.value))}
                />
              </div>
            </div>

            <div className="flex flex-col gap-3 sm:col-span-2">
              <Toggle id="indexer-rss" label={t('indexers.enableRss')} checked={form.enableRss} onChange={(v) => set('enableRss', v)} />
              <Toggle
                id="indexer-auto"
                label={t('indexers.enableAutomaticSearch')}
                checked={form.enableAutomaticSearch}
                onChange={(v) => set('enableAutomaticSearch', v)}
              />
              <Toggle
                id="indexer-interactive"
                label={t('indexers.enableInteractiveSearch')}
                checked={form.enableInteractiveSearch}
                onChange={(v) => set('enableInteractiveSearch', v)}
              />
            </div>
          </div>

          {save.error && (
            <Alert variant="destructive" className="mb-4">
              <AlertDescription>{save.error instanceof ApiError ? save.error.message : t('common.unexpectedError')}</AlertDescription>
            </Alert>
          )}

          <DialogFooter className="items-center sm:justify-between">
            <Button type="button" variant="outline" onClick={() => test.mutate()} disabled={test.isPending || !canSubmit} data-testid="indexer-test">
              <FlaskConicalIcon className={test.isPending ? 'animate-pulse' : undefined} />
              {test.isPending ? t('indexers.testing') : t('indexers.test')}
            </Button>
            <div className="flex gap-2">
              <Button type="button" variant="ghost" onClick={onClose}>
                {t('common.cancel')}
              </Button>
              <Button type="submit" disabled={save.isPending || !canSubmit} data-testid="indexer-save">
                {t('indexers.save')}
              </Button>
            </div>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}

function Toggle({ id, label, checked, onChange }: { id: string; label: string; checked: boolean; onChange: (value: boolean) => void }) {
  return (
    <div className={cn('flex items-center gap-2')}>
      <Checkbox id={id} checked={checked} onCheckedChange={(value) => onChange(value === true)} />
      <Label htmlFor={id} className="font-normal">
        {label}
      </Label>
    </div>
  )
}
