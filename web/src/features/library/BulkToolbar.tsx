import { useMemo, useState, type FormEvent } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { ChevronDownIcon, EyeIcon, EyeOffIcon, PlusIcon, SearchIcon, TagsIcon, Trash2Icon, XIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import {
  ApiError,
  movies as moviesApi,
  tags as tagsApi,
  type BulkDeleteResponse,
  type BulkUpdateRequest,
  type MinimumAvailability,
  type MovieSummary,
  type QualityProfile,
  type Tag,
  type TagsMode,
  type VersionSelector,
} from '@/api/client'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from '@/components/ui/dropdown-menu'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { BulkDeleteDialog } from './BulkDeleteDialog'
import type { BulkReport } from './BulkResults'
import { targetVersions, type VersionName } from './libraryView'

const availabilities: MinimumAvailability[] = ['announced', 'inCinemas', 'released']

type Props = {
  library: MovieSummary[]
  shown: MovieSummary[]
  filtered: boolean
  selected: ReadonlySet<number>
  onSelect: (ids: number[]) => void
  onDeselect: (ids: number[]) => void
  onClear: () => void
  onDone: () => void
  names: VersionName[]
  profiles: QualityProfile[]
  tags: Tag[]
  onReport: (report: BulkReport) => void
}

/**
 * Selection mode's bar, sticky at the bottom of the viewport: what is selected, which version of each film the
 * actions touch, and the actions. Every action is one request; the server reports film by film.
 */
export function BulkToolbar({ library, shown, filtered, selected, onSelect, onDeselect, onClear, onDone, names, profiles, tags, onReport }: Props) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [target, setTarget] = useState<VersionSelector>('default')
  const [deleting, setDeleting] = useState(false)

  const selectedMovies = useMemo(() => library.filter((m) => selected.has(m.id)), [library, selected])
  const ids = useMemo(() => selectedMovies.map((m) => m.id), [selectedMovies])
  const covered = useMemo(() => selectedMovies.filter((m) => targetVersions(m, target).length > 0).length, [selectedMovies, target])
  const count = selectedMovies.length
  const targetName = target.slice('version:'.length)
  // "the default version", "every version", "the 4k version": the target inside a sentence.
  const targetPhrase = target === 'default' || target === 'all' ? t(`bulk.inline.${target}`) : t('bulk.inline.version', { name: targetName })

  const invalidate = () => {
    void queryClient.invalidateQueries({ queryKey: ['movies'] })
    void queryClient.invalidateQueries({ queryKey: ['movie'] })
    void queryClient.invalidateQueries({ queryKey: ['wanted'] })
  }
  const onError = (e: Error) => toast.error(e instanceof ApiError ? e.message : t('bulk.failed'))

  const update = useMutation({
    mutationFn: ({ body }: { body: Omit<BulkUpdateRequest, 'movieIds' | 'versions'>; action: string }) => moviesApi.bulkUpdate({ movieIds: ids, versions: target, ...body }),
    onSuccess: (response, { action }) => {
      onReport({ action, results: response.results })
      invalidate()
    },
    onError,
  })
  const search = useMutation({
    mutationFn: () => moviesApi.bulkSearch(ids, target),
    onSuccess: (response) => {
      onReport({ action: t('bulk.actions.search', { target: targetPhrase }), results: response.results })
      void queryClient.invalidateQueries({ queryKey: ['queue'] })
    },
    onError,
  })
  const onDeleted = (response: BulkDeleteResponse) => {
    onReport({ action: t('bulk.actions.delete', { target: targetPhrase }), results: response.results })
    onDeselect(response.results.filter((r) => r.outcome === 'movieDeleted').map((r) => r.movieId))
    invalidate()
  }

  const busy = update.isPending || search.isPending
  const disabled = count === 0 || busy

  return (
    <div className="sticky bottom-3 z-20 flex flex-col gap-2 rounded-xl border bg-background/95 p-3 shadow-lg backdrop-blur" data-testid="bulk-toolbar">
      <div className="flex flex-wrap items-center gap-x-3 gap-y-2 text-sm">
        <span className="font-medium tabular-nums" data-testid="bulk-count">
          {t('bulk.selected', { count })}
        </span>
        <Button type="button" variant="link" size="xs" className="h-auto px-0" onClick={() => onSelect(library.map((m) => m.id))} disabled={count === library.length} data-testid="bulk-select-all">
          {t('bulk.selectAll', { count: library.length })}
        </Button>
        {filtered && (
          <Button type="button" variant="link" size="xs" className="h-auto px-0" onClick={() => onSelect(shown.map((m) => m.id))} disabled={shown.length === 0} data-testid="bulk-select-shown">
            {t('bulk.selectShown', { count: shown.length })}
          </Button>
        )}
        {count > 0 && (
          <Button type="button" variant="link" size="xs" className="h-auto px-0 text-muted-foreground" onClick={onClear} data-testid="bulk-clear">
            {t('bulk.clear')}
          </Button>
        )}

        <div className="ml-auto flex flex-wrap items-center gap-2">
          <Label htmlFor="bulk-target" className="text-muted-foreground">
            {t('bulk.target')}
          </Label>
          <Select value={target} onValueChange={(value) => setTarget(value as VersionSelector)}>
            <SelectTrigger id="bulk-target" size="sm" className="min-w-36" data-testid="bulk-target">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="default" data-testid="bulk-target-default">
                {t('bulk.targets.default')}
              </SelectItem>
              <SelectItem value="all" data-testid="bulk-target-all">
                {t('bulk.targets.all')}
              </SelectItem>
              {names.map(({ name }) => (
                <SelectItem key={name} value={`version:${name}`} data-testid={`bulk-target-${name}`}>
                  {name}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
          {count > 0 && covered < count && (
            <span className="text-xs text-muted-foreground" data-testid="bulk-coverage">
              {t('bulk.coverage', { count: count - covered, name: targetName })}
            </span>
          )}
          <Button type="button" variant="ghost" size="sm" onClick={onDone} data-testid="bulk-done">
            <XIcon />
            {t('bulk.done')}
          </Button>
        </div>
      </div>

      <div className="flex flex-wrap items-center gap-2">
        <Button
          type="button"
          variant="outline"
          size="sm"
          disabled={disabled}
          onClick={() => update.mutate({ body: { monitored: true }, action: t('bulk.actions.monitor', { target: targetPhrase }) })}
          data-testid="bulk-monitor"
        >
          <EyeIcon />
          {t('bulk.monitor')}
        </Button>
        <Button
          type="button"
          variant="outline"
          size="sm"
          disabled={disabled}
          onClick={() => update.mutate({ body: { monitored: false }, action: t('bulk.actions.unmonitor', { target: targetPhrase }) })}
          data-testid="bulk-unmonitor"
        >
          <EyeOffIcon />
          {t('bulk.unmonitor')}
        </Button>

        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <Button type="button" variant="outline" size="sm" disabled={disabled} data-testid="bulk-profile">
              {t('bulk.profile')}
              <ChevronDownIcon />
            </Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="start">
            {profiles.map((profile) => (
              <DropdownMenuItem
                key={profile.id}
                onSelect={() => update.mutate({ body: { qualityProfileId: profile.id }, action: t('bulk.actions.profile', { target: targetPhrase, name: profile.name }) })}
                data-testid={`bulk-profile-${profile.id}`}
              >
                {profile.name}
              </DropdownMenuItem>
            ))}
          </DropdownMenuContent>
        </DropdownMenu>

        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <Button type="button" variant="outline" size="sm" disabled={disabled} data-testid="bulk-availability">
              {t('bulk.availability')}
              <ChevronDownIcon />
            </Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="start">
            {availabilities.map((value) => (
              <DropdownMenuItem
                key={value}
                onSelect={() =>
                  update.mutate({ body: { minimumAvailability: value }, action: t('bulk.actions.availability', { target: targetPhrase, value: t(`availability.${value}`) }) })
                }
                data-testid={`bulk-availability-${value}`}
              >
                {t(`availability.${value}`)}
              </DropdownMenuItem>
            ))}
          </DropdownMenuContent>
        </DropdownMenu>

        <TagsPopover
          tags={tags}
          disabled={disabled}
          onApply={(mode, tagIds) => update.mutate({ body: { tagIds, applyTags: mode }, action: t('bulk.actions.tags', { target: targetPhrase, mode: t(`bulk.tagsMode.${mode}`) }) })}
        />

        <Button type="button" variant="outline" size="sm" disabled={disabled} onClick={() => search.mutate()} data-testid="bulk-search">
          <SearchIcon className={search.isPending ? 'animate-pulse' : undefined} />
          {t('bulk.search')}
        </Button>

        <Button type="button" variant="destructive" size="sm" className="ml-auto" disabled={disabled} onClick={() => setDeleting(true)} data-testid="bulk-delete">
          <Trash2Icon />
          {t('bulk.delete.button')}
        </Button>
      </div>

      <BulkDeleteDialog open={deleting} onOpenChange={setDeleting} movieIds={ids} target={target} targetPhrase={targetPhrase} onDeleted={onDeleted} />
    </div>
  )
}

/** Add, remove or replace version tags; a missing tag can be created on the spot. */
function TagsPopover({ tags, disabled, onApply }: { tags: Tag[]; disabled: boolean; onApply: (mode: TagsMode, tagIds: number[]) => void }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [open, setOpen] = useState(false)
  const [mode, setMode] = useState<TagsMode>('add')
  const [picked, setPicked] = useState<number[]>([])
  const [label, setLabel] = useState('')

  const create = useMutation({
    mutationFn: (value: string) => tagsApi.add(value),
    onSuccess: (tag) => {
      setPicked((current) => (current.includes(tag.id) ? current : [...current, tag.id]))
      setLabel('')
      void queryClient.invalidateQueries({ queryKey: ['tags'] })
    },
    onError: (e) => toast.error(e instanceof ApiError ? e.message : t('common.unexpectedError')),
  })

  const submitLabel = (event: FormEvent) => {
    event.preventDefault()
    if (label.trim() !== '') create.mutate(label.trim())
  }

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button type="button" variant="outline" size="sm" disabled={disabled} data-testid="bulk-tags">
          <TagsIcon />
          {t('bulk.tags')}
        </Button>
      </PopoverTrigger>
      <PopoverContent align="start" className="flex w-72 flex-col gap-3" data-testid="bulk-tags-popover">
        <div className="flex rounded-md border p-0.5" role="tablist">
          {(['add', 'remove', 'replace'] as const).map((value) => (
            <Button
              key={value}
              type="button"
              role="tab"
              aria-selected={mode === value}
              variant={mode === value ? 'secondary' : 'ghost'}
              size="xs"
              className="flex-1"
              onClick={() => setMode(value)}
              data-testid={`bulk-tags-${value}`}
            >
              {t(`bulk.tagsMode.${value}`)}
            </Button>
          ))}
        </div>
        <p className="text-xs text-muted-foreground">{t(`bulk.tagsHint.${mode}`)}</p>
        <div className="flex max-h-48 flex-col gap-1.5 overflow-y-auto">
          {tags.length === 0 && <p className="text-sm text-muted-foreground">{t('bulk.noTags')}</p>}
          {tags.map((tag) => (
            <label key={tag.id} className="flex items-center gap-2 text-sm">
              <Checkbox
                checked={picked.includes(tag.id)}
                onCheckedChange={(checked) => setPicked((current) => (checked === true ? [...current, tag.id] : current.filter((id) => id !== tag.id)))}
              />
              {tag.label}
            </label>
          ))}
        </div>
        <form onSubmit={submitLabel} className="flex gap-2">
          <Input value={label} onChange={(e) => setLabel(e.target.value)} placeholder={t('bulk.newTag')} className="h-8" maxLength={64} />
          <Button type="submit" variant="outline" size="sm" disabled={label.trim() === '' || create.isPending}>
            <PlusIcon />
            {t('bulk.createTag')}
          </Button>
        </form>
        <Button
          type="button"
          size="sm"
          disabled={picked.length === 0 && mode !== 'replace'}
          onClick={() => {
            onApply(mode, picked)
            setOpen(false)
          }}
          data-testid="bulk-tags-apply"
        >
          {mode === 'replace' && picked.length === 0 ? t('bulk.clearTags') : t('bulk.applyTags', { count: picked.length })}
        </Button>
      </PopoverContent>
    </Popover>
  )
}
