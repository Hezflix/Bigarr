import { Fragment, useState, type FormEvent, type ReactNode } from 'react'
import { ArrowDownIcon, ArrowUpIcon, BookmarkPlusIcon, LayoutGridIcon, ListFilterIcon, SearchIcon, Settings2Icon, TableIcon, XIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import type { RootFolder, Tag } from '@/api/client'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import {
  DropdownMenu,
  DropdownMenuCheckboxItem,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
  DropdownMenuSeparator,
  DropdownMenuSub,
  DropdownMenuSubContent,
  DropdownMenuSubTrigger,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import {
  activeFilterCount,
  cardFields,
  densities,
  emptyFilter,
  posterSizes,
  playedFilters,
  sortKeys,
  statusFilters,
  type PlayedFilter,
  tableColumns,
  viewModes,
  type Density,
  type FilterScope,
  type LibraryFilter,
  type LibraryPrefs,
  type MonitoredFilter,
  type PosterSize,
  type Preset,
  type SortDirection,
  type SortKey,
  type StatusFilter,
  type VersionName,
  type ViewMode,
} from './libraryView'
import { useSortLabel } from './SizeSplit'
import { ViewToneDot } from './VersionChip'

/** Menu items that change a setting keep the menu open: several are usually changed in a row. */
export const keepOpen = (event: Event) => event.preventDefault()

export const toggle = <T,>(values: T[], value: T) => (values.includes(value) ? values.filter((v) => v !== value) : [...values, value])

// ---- presets and saved views ------------------------------------------------

/** What a view tab needs of a preset: its colour comes from the status and monitoring it lists. */
type PresetTab = { id: string; count: number; filter: { status: StatusFilter; monitored: MonitoredFilter } }

type SavedViewsProps<P extends PresetTab, V extends { id: string; name: string }> = {
  presets: P[]
  /** Defaults to the film library's preset labels. */
  presetLabel?: (preset: P) => string
  savedViews: V[]
  activeId: string | null
  onPreset: (preset: P) => void
  onSaved: (view: V) => void
  onSave: (name: string) => void
  onDelete: (id: string) => void
}

/** Built-in views, then the user's own; the tab of the view matching the current filter is highlighted. */
export function SavedViewsBar<P extends PresetTab, V extends { id: string; name: string }>({ presets, presetLabel, savedViews, activeId, onPreset, onSaved, onSave, onDelete }: SavedViewsProps<P, V>) {
  const { t } = useTranslation()
  const filmLabel = (preset: { id: string; version?: Preset['version'] }) => (preset.version ? t('library.presets.missingVersion', { name: preset.version }) : t(`library.presets.${preset.id as 'all'}`))
  const [saving, setSaving] = useState(false)

  return (
    <div className="flex flex-wrap items-center gap-1" role="tablist" aria-label={t('library.savedViews.label')} data-testid="library-views">
      {presets.map((preset) => (
        <Button
          key={preset.id}
          type="button"
          role="tab"
          aria-selected={activeId === preset.id}
          variant={activeId === preset.id ? 'secondary' : 'ghost'}
          size="sm"
          onClick={() => onPreset(preset)}
          data-testid={`library-view-${preset.id}`}
        >
          <ViewToneDot filter={preset.filter} />
          {presetLabel ? presetLabel(preset) : filmLabel(preset)}
          <span className="text-xs font-normal text-muted-foreground tabular-nums">{preset.count}</span>
        </Button>
      ))}
      {savedViews.length > 0 && <span className="mx-1 h-5 w-px bg-border" aria-hidden="true" />}
      {savedViews.map((view) => (
        <span key={view.id} className="group inline-flex items-center">
          <Button
            type="button"
            role="tab"
            aria-selected={activeId === view.id}
            variant={activeId === view.id ? 'secondary' : 'ghost'}
            size="sm"
            onClick={() => onSaved(view)}
            data-testid={`library-saved-view-${view.id}`}
          >
            {view.name}
          </Button>
          <Button
            type="button"
            variant="ghost"
            size="icon-xs"
            className="text-muted-foreground opacity-0 group-hover:opacity-100 focus-visible:opacity-100"
            onClick={() => onDelete(view.id)}
            aria-label={t('library.savedViews.delete', { name: view.name })}
          >
            <XIcon />
          </Button>
        </span>
      ))}
      <Button type="button" variant="ghost" size="sm" className="text-muted-foreground" onClick={() => setSaving(true)} data-testid="library-save-view">
        <BookmarkPlusIcon />
        {t('library.savedViews.save')}
      </Button>
      <SaveViewDialog
        open={saving}
        onOpenChange={setSaving}
        onSave={(name) => {
          onSave(name)
          setSaving(false)
        }}
      />
    </div>
  )
}

function SaveViewDialog({ open, onOpenChange, onSave }: { open: boolean; onOpenChange: (open: boolean) => void; onSave: (name: string) => void }) {
  const { t } = useTranslation()
  const [name, setName] = useState('')
  const submit = (event: FormEvent) => {
    event.preventDefault()
    if (name.trim() === '') return
    onSave(name.trim())
    setName('')
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        <form onSubmit={submit} className="grid gap-4">
          <DialogHeader>
            <DialogTitle>{t('library.savedViews.saveTitle')}</DialogTitle>
            <DialogDescription>{t('library.savedViews.saveHint')}</DialogDescription>
          </DialogHeader>
          <div className="grid gap-2">
            <Label htmlFor="view-name">{t('library.savedViews.name')}</Label>
            <Input id="view-name" value={name} onChange={(e) => setName(e.target.value)} autoFocus maxLength={40} data-testid="library-save-view-name" />
          </div>
          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
              {t('common.cancel')}
            </Button>
            <Button type="submit" disabled={name.trim() === ''} data-testid="library-save-view-confirm">
              {t('common.save')}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}

// ---- search, layout, sort, filter, view options ------------------------------

type ControlsProps = {
  prefs: LibraryPrefs
  onChange: (patch: Partial<LibraryPrefs>) => void
  term: string
  onTermChange: (term: string) => void
  names: VersionName[]
  qualities: string[]
  tags: Tag[]
  rootFolders: RootFolder[]
}

const modeIcons: Record<ViewMode, ReactNode> = {
  posters: <LayoutGridIcon />,
  table: <TableIcon />,
}

export function LibraryControls({ prefs, onChange, term, onTermChange, names, qualities, tags, rootFolders }: ControlsProps) {
  const { t } = useTranslation()

  return (
    <LibrarySearchBar term={term} onTermChange={onTermChange} placeholder={t('library.searchPlaceholder')} mode={prefs.mode} onMode={(mode) => onChange({ mode })}>
      <FilterMenu filter={prefs.filter} onChange={(filter) => onChange({ filter })} names={names} qualities={qualities} tags={tags} rootFolders={rootFolders} />
      <ViewMenu prefs={prefs} onChange={onChange} names={names} />
    </LibrarySearchBar>
  )
}

type SearchBarProps = {
  term: string
  onTermChange: (term: string) => void
  placeholder: string
  mode: ViewMode
  onMode: (mode: ViewMode) => void
  /** The menus on the right: filter, view options. */
  children: ReactNode
}

/** The search box and the layout tabs of a library page, its menus on the right. */
export function LibrarySearchBar({ term, onTermChange, placeholder, mode: current, onMode, children }: SearchBarProps) {
  const { t } = useTranslation()

  return (
    <div className="flex flex-wrap items-center gap-2" data-testid="library-controls">
      <div className="relative w-full sm:w-64">
        <SearchIcon className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-muted-foreground" aria-hidden="true" />
        <Input
          type="search"
          value={term}
          onChange={(e) => onTermChange(e.target.value)}
          placeholder={placeholder}
          aria-label={placeholder}
          className="h-8 pl-8"
          data-testid="library-search"
        />
      </div>

      <div className="flex rounded-md border p-0.5" role="tablist" aria-label={t('library.view.mode')}>
        {viewModes.map((mode) => (
          <Button
            key={mode}
            type="button"
            role="tab"
            aria-selected={current === mode}
            variant={current === mode ? 'secondary' : 'ghost'}
            size="xs"
            className="h-7 px-2"
            onClick={() => onMode(mode)}
            data-testid={`library-mode-${mode}`}
          >
            {modeIcons[mode]}
            <span className="hidden lg:inline">{t(`library.modes.${mode}`)}</span>
          </Button>
        ))}
      </div>

      <div className="ml-auto flex flex-wrap items-center gap-2">{children}</div>
    </div>
  )
}

type FilterMenuProps = {
  filter: LibraryFilter
  onChange: (filter: LibraryFilter) => void
  names: VersionName[]
  qualities: string[]
  tags: Tag[]
  rootFolders: RootFolder[]
}

function FilterMenu({ filter, onChange, names, qualities, tags, rootFolders }: FilterMenuProps) {
  const { t } = useTranslation()
  const set = (patch: Partial<LibraryFilter>) => onChange({ ...filter, ...patch })
  const playedLabel = (played: PlayedFilter) => {
    if (played === 'any' || played === 'never' || played === 'ever') return t(`library.filter.playedValues.${played}`)
    const [kind, days] = played.split(':')
    return t(kind === 'within' ? 'library.filter.playedWithin' : 'library.filter.notPlayedWithin', { count: Number(days) })
  }

  return (
    <FilterMenuShell active={activeFilterCount(filter)} onClear={() => onChange(emptyFilter)}>
      <ScopeSub value={filter.scope} onChange={(scope) => set({ scope })} names={names} />
      <MonitoredSub value={filter.monitored} onChange={(monitored) => set({ monitored })} />
      <StatusSub value={filter.status} onChange={(status) => set({ status })} />
      <DropdownMenuSub>
        <DropdownMenuSubTrigger data-testid="library-filter-played">
          <span className="flex-1">{t('library.filter.played')}</span>
          <span className="text-xs text-muted-foreground">{playedLabel(filter.played)}</span>
        </DropdownMenuSubTrigger>
        <DropdownMenuSubContent>
          <DropdownMenuRadioGroup value={filter.played} onValueChange={(value) => set({ played: value as PlayedFilter })}>
            {playedFilters.map((value) => (
              <DropdownMenuRadioItem key={value} value={value} onSelect={keepOpen} data-testid={`library-filter-played-${value}`}>
                {playedLabel(value)}
              </DropdownMenuRadioItem>
            ))}
          </DropdownMenuRadioGroup>
        </DropdownMenuSubContent>
      </DropdownMenuSub>
      <CheckboxSub
        label={t('library.filter.quality')}
        options={qualities.map((q) => ({ value: q, label: q }))}
        values={filter.qualities}
        onToggle={(value) => set({ qualities: toggle(filter.qualities, value) })}
      />
      <CheckboxSub
        label={t('library.filter.tags')}
        options={tags.map((tag) => ({ value: tag.id, label: tag.label }))}
        values={filter.tags}
        onToggle={(value) => set({ tags: toggle(filter.tags, value) })}
      />
      <CheckboxSub
        label={t('library.filter.rootFolder')}
        options={rootFolders.map((root) => ({ value: root.id, label: root.path }))}
        values={filter.rootFolders}
        onToggle={(value) => set({ rootFolders: toggle(filter.rootFolders, value) })}
      />
      <DropdownMenuSeparator />
      <VersionPresenceSubs names={names} has={filter.hasVersions} lacks={filter.lacksVersions} onChange={(patch) => set(patch)} />
      <AddedSub value={filter.addedWithinDays} onChange={(addedWithinDays) => set({ addedWithinDays })} />
    </FilterMenuShell>
  )
}

/** The Filter button, with the number of active conditions, and its menu ending on "Clear filters". */
export function FilterMenuShell({ active, onClear, children }: { active: number; onClear: () => void; children: ReactNode }) {
  const { t } = useTranslation()
  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button type="button" variant={active > 0 ? 'secondary' : 'outline'} size="sm" data-testid="library-filter">
          <ListFilterIcon />
          {t('library.filter.label')}
          {active > 0 && (
            <Badge variant="default" className="h-4 min-w-4 rounded-full px-1 text-[10px] tabular-nums">
              {active}
            </Badge>
          )}
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="w-60">
        {children}
        <DropdownMenuSeparator />
        <DropdownMenuItem onSelect={onClear} disabled={active === 0} data-testid="library-filter-clear">
          <XIcon />
          {t('library.clearFilters')}
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  )
}

/** A submenu of mutually exclusive values, the current one shown beside its label. */
export function RadioSub<T extends string>({
  label,
  options,
  value,
  onChange,
  testId,
  itemTestId,
}: {
  label: string
  options: readonly { value: T; label: string }[]
  value: T
  onChange: (value: T) => void
  testId?: string
  /** Each option's test id, when set: `${itemTestId}-${value}`. */
  itemTestId?: string
}) {
  return (
    <DropdownMenuSub>
      <DropdownMenuSubTrigger data-testid={testId}>
        <span className="flex-1">{label}</span>
        <span className="text-xs text-muted-foreground">{options.find((o) => o.value === value)?.label}</span>
      </DropdownMenuSubTrigger>
      <DropdownMenuSubContent>
        <DropdownMenuRadioGroup value={value} onValueChange={(next) => onChange(next as T)}>
          {options.map((option) => (
            <DropdownMenuRadioItem key={option.value} value={option.value} onSelect={keepOpen} data-testid={itemTestId ? `${itemTestId}-${option.value}` : undefined}>
              {option.label}
            </DropdownMenuRadioItem>
          ))}
        </DropdownMenuRadioGroup>
      </DropdownMenuSubContent>
    </DropdownMenuSub>
  )
}

/** Which version the version-level conditions look at. */
export function ScopeSub({ value, onChange, names }: { value: FilterScope; onChange: (scope: FilterScope) => void; names: VersionName[] }) {
  const { t } = useTranslation()
  const scopes: FilterScope[] = ['any', 'all', 'default', ...names.map((n) => `version:${n.name}` as const)]
  const scopeLabel = (scope: FilterScope) => (scope.startsWith('version:') ? scope.slice('version:'.length) : t(`library.filter.scopes.${scope as 'any'}`))
  return (
    <DropdownMenuSub>
      <DropdownMenuSubTrigger data-testid="library-filter-scope">
        <span className="flex-1">{t('library.filter.scope')}</span>
        <span className="text-xs text-muted-foreground">{scopeLabel(value)}</span>
      </DropdownMenuSubTrigger>
      <DropdownMenuSubContent>
        <DropdownMenuLabel className="max-w-56 text-xs font-normal text-muted-foreground">{t('library.filter.scopeHint')}</DropdownMenuLabel>
        <DropdownMenuRadioGroup value={value} onValueChange={(next) => onChange(next as FilterScope)}>
          {scopes.map((scope) => (
            <DropdownMenuRadioItem key={scope} value={scope} onSelect={keepOpen}>
              {scopeLabel(scope)}
            </DropdownMenuRadioItem>
          ))}
        </DropdownMenuRadioGroup>
      </DropdownMenuSubContent>
    </DropdownMenuSub>
  )
}

export function MonitoredSub({ value, onChange }: { value: MonitoredFilter; onChange: (value: MonitoredFilter) => void }) {
  const { t } = useTranslation()
  return (
    <DropdownMenuSub>
      <DropdownMenuSubTrigger data-testid="library-filter-monitored">
        <span className="flex-1">{t('library.filter.monitored')}</span>
        <span className="text-xs text-muted-foreground">{t(`library.filter.monitoredValues.${value}`)}</span>
      </DropdownMenuSubTrigger>
      <DropdownMenuSubContent>
        <DropdownMenuRadioGroup value={value} onValueChange={(next) => onChange(next as MonitoredFilter)}>
          {(['any', 'monitored', 'unmonitored'] as const).map((option) => (
            <DropdownMenuRadioItem key={option} value={option} onSelect={keepOpen}>
              {t(`library.filter.monitoredValues.${option}`)}
            </DropdownMenuRadioItem>
          ))}
        </DropdownMenuRadioGroup>
      </DropdownMenuSubContent>
    </DropdownMenuSub>
  )
}

/** `labels` overrides the film wording of some statuses ("Not available yet" reads "Nothing aired yet" for a series). */
export function StatusSub({ value, onChange, labels }: { value: StatusFilter; onChange: (value: StatusFilter) => void; labels?: Partial<Record<StatusFilter, string>> }) {
  const { t } = useTranslation()
  const label = (status: StatusFilter) => labels?.[status] ?? t(`library.filter.statuses.${status}`)
  return (
    <DropdownMenuSub>
      <DropdownMenuSubTrigger data-testid="library-filter-status">
        <span className="flex-1">{t('library.filter.status')}</span>
        <span className="text-xs text-muted-foreground">{label(value)}</span>
      </DropdownMenuSubTrigger>
      <DropdownMenuSubContent>
        <DropdownMenuRadioGroup value={value} onValueChange={(next) => onChange(next as StatusFilter)}>
          {statusFilters.map((option) => (
            <DropdownMenuRadioItem key={option} value={option} onSelect={keepOpen} data-testid={`library-filter-status-${option}`}>
              {label(option)}
            </DropdownMenuRadioItem>
          ))}
        </DropdownMenuRadioGroup>
      </DropdownMenuSubContent>
    </DropdownMenuSub>
  )
}

/** Has version / Lacks version: item-level conditions on the version names. */
export function VersionPresenceSubs({
  names,
  has,
  lacks,
  onChange,
}: {
  names: VersionName[]
  has: string[]
  lacks: string[]
  onChange: (patch: { hasVersions?: string[]; lacksVersions?: string[] }) => void
}) {
  const { t } = useTranslation()
  return (
    <>
      <CheckboxSub
        label={t('library.filter.hasVersion')}
        options={names.map((n) => ({ value: n.name, label: n.name }))}
        values={has}
        onToggle={(value) => onChange({ hasVersions: toggle(has, value) })}
        testId="library-filter-has"
      />
      <CheckboxSub
        label={t('library.filter.lacksVersion')}
        options={names.map((n) => ({ value: n.name, label: n.name }))}
        values={lacks}
        onToggle={(value) => onChange({ lacksVersions: toggle(lacks, value) })}
        testId="library-filter-lacks"
      />
    </>
  )
}

export function AddedSub({ value, onChange }: { value: number | null; onChange: (days: number | null) => void }) {
  const { t } = useTranslation()
  return (
    <DropdownMenuSub>
      <DropdownMenuSubTrigger>
        <span className="flex-1">{t('library.filter.added')}</span>
        <span className="text-xs text-muted-foreground">{value === null ? t('library.filter.addedAny') : t('library.filter.addedDays', { count: value })}</span>
      </DropdownMenuSubTrigger>
      <DropdownMenuSubContent>
        <DropdownMenuRadioGroup value={String(value ?? 'any')} onValueChange={(next) => onChange(next === 'any' ? null : Number(next))}>
          <DropdownMenuRadioItem value="any" onSelect={keepOpen}>
            {t('library.filter.addedAny')}
          </DropdownMenuRadioItem>
          {[7, 30, 90, 365].map((days) => (
            <DropdownMenuRadioItem key={days} value={String(days)} onSelect={keepOpen}>
              {t('library.filter.addedDays', { count: days })}
            </DropdownMenuRadioItem>
          ))}
        </DropdownMenuRadioGroup>
      </DropdownMenuSubContent>
    </DropdownMenuSub>
  )
}

export function CheckboxSub<T extends string | number>({
  label,
  options,
  values,
  onToggle,
  testId,
}: {
  label: string
  options: { value: T; label: string }[]
  values: T[]
  onToggle: (value: T) => void
  testId?: string
}) {
  const { t } = useTranslation()
  return (
    <DropdownMenuSub>
      <DropdownMenuSubTrigger data-testid={testId}>
        <span className="flex-1">{label}</span>
        {values.length > 0 && <span className="text-xs text-muted-foreground tabular-nums">{values.length}</span>}
      </DropdownMenuSubTrigger>
      <DropdownMenuSubContent className="max-h-80 max-w-80">
        {options.length === 0 && <DropdownMenuLabel className="text-xs font-normal text-muted-foreground">{t('library.filter.nothing')}</DropdownMenuLabel>}
        {options.map((option) => (
          <DropdownMenuCheckboxItem
            key={option.value}
            checked={values.includes(option.value)}
            onCheckedChange={() => onToggle(option.value)}
            onSelect={keepOpen}
            className="truncate"
            data-testid={testId ? `${testId}-${option.value}` : undefined}
          >
            {option.label}
          </DropdownMenuCheckboxItem>
        ))}
      </DropdownMenuSubContent>
    </DropdownMenuSub>
  )
}

/** The view settings both libraries share; the card fields and columns are each library's own. */
export type ViewSettings = { mode: ViewMode; posterSize: PosterSize; density: Density; sizeSplit: boolean }

type ViewOptionsProps<F extends string, C extends string> = {
  view: ViewSettings
  onChange: (patch: Partial<ViewSettings> & { cardFields?: F[]; columns?: C[] }) => void
  /** The Sort submenu, first in the menu. */
  sort: ReactNode
  cardFields: { all: readonly F[]; value: F[]; label: (field: F) => string }
  columns: { all: readonly C[]; value: C[]; label: (column: C) => string }
  /** More than one version in the library: the size can be split per version. */
  splittable: boolean
}

/**
 * Only the options of the current layout: card fields do nothing to the table, columns nothing to the posters. The
 * other layout's settings stay stored and apply again when it comes back. Sort and density apply to both, and so does
 * the poster size: the cards, or the thumbnail of comfortable table rows (compact rows have none, so it is greyed out).
 */
export function ViewOptionsMenu<F extends string, C extends string>({ view, onChange, sort, cardFields, columns, splittable }: ViewOptionsProps<F, C>) {
  const { t } = useTranslation()
  const noImage = view.mode === 'table' && view.density === 'compact'
  // Under Size, in both lists: one setting for the posters and the table. Without a second version there is nothing to split.
  const sizeSplit = (shown: boolean, testid: string) =>
    splittable && (
      <DropdownMenuCheckboxItem
        checked={view.sizeSplit}
        disabled={!shown}
        onCheckedChange={() => onChange({ sizeSplit: !view.sizeSplit })}
        onSelect={keepOpen}
        data-testid={testid}
      >
        {t('library.view.sizeSplit')}
      </DropdownMenuCheckboxItem>
    )
  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button type="button" variant="outline" size="sm" data-testid="library-view-options">
          <Settings2Icon />
          {t('library.view.label')}
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="w-60">
        {sort}
        <DropdownMenuSub>
          <DropdownMenuSubTrigger disabled={noImage} data-testid="library-poster-size">
            <span className="flex-1">{t('library.view.posterSize')}</span>
            <span className="text-xs text-muted-foreground">{noImage ? t('library.view.comfortableOnly') : t(`library.view.sizes.${view.posterSize}`)}</span>
          </DropdownMenuSubTrigger>
          <DropdownMenuSubContent>
            <DropdownMenuRadioGroup value={view.posterSize} onValueChange={(value) => onChange({ posterSize: value as PosterSize })}>
              {posterSizes.map((size) => (
                <DropdownMenuRadioItem key={size} value={size} onSelect={keepOpen} data-testid={`library-poster-${size}`}>
                  {t(`library.view.sizes.${size}`)}
                </DropdownMenuRadioItem>
              ))}
            </DropdownMenuRadioGroup>
          </DropdownMenuSubContent>
        </DropdownMenuSub>
        {view.mode === 'posters' && (
          <DropdownMenuSub>
            <DropdownMenuSubTrigger data-testid="library-card-fields">{t('library.view.cardFields')}</DropdownMenuSubTrigger>
            <DropdownMenuSubContent>
              {cardFields.all.map((field) => (
                <Fragment key={field}>
                  <DropdownMenuCheckboxItem
                    checked={cardFields.value.includes(field)}
                    onCheckedChange={() => onChange({ cardFields: toggle(cardFields.value, field) })}
                    onSelect={keepOpen}
                    data-testid={`library-card-field-${field}`}
                  >
                    {cardFields.label(field)}
                  </DropdownMenuCheckboxItem>
                  {field === 'size' && sizeSplit(cardFields.value.includes(field), 'library-card-field-size-split')}
                </Fragment>
              ))}
            </DropdownMenuSubContent>
          </DropdownMenuSub>
        )}
        {view.mode === 'table' && (
          <DropdownMenuSub>
            <DropdownMenuSubTrigger data-testid="library-columns">{t('library.view.columns')}</DropdownMenuSubTrigger>
            <DropdownMenuSubContent>
              {columns.all.map((column) => (
                <Fragment key={column}>
                  <DropdownMenuCheckboxItem
                    checked={columns.value.includes(column)}
                    // Keep the declared order whatever the order of the clicks.
                    onCheckedChange={() => onChange({ columns: columns.all.filter((c) => (c === column ? !columns.value.includes(c) : columns.value.includes(c))) })}
                    onSelect={keepOpen}
                    data-testid={`library-column-${column}`}
                  >
                    {columns.label(column)}
                  </DropdownMenuCheckboxItem>
                  {column === 'size' && sizeSplit(columns.value.includes(column), 'library-column-size-split')}
                </Fragment>
              ))}
            </DropdownMenuSubContent>
          </DropdownMenuSub>
        )}
        <DropdownMenuSeparator />
        <DropdownMenuLabel className="text-xs font-normal text-muted-foreground">{t('library.view.density')}</DropdownMenuLabel>
        <DropdownMenuRadioGroup value={view.density} onValueChange={(value) => onChange({ density: value as Density })}>
          {densities.map((density) => (
            <DropdownMenuRadioItem key={density} value={density} onSelect={keepOpen} data-testid={`library-density-${density}`}>
              {t(`library.view.densities.${density}`)}
            </DropdownMenuRadioItem>
          ))}
        </DropdownMenuRadioGroup>
      </DropdownMenuContent>
    </DropdownMenu>
  )
}

function ViewMenu({ prefs, onChange, names }: { prefs: LibraryPrefs; onChange: (patch: Partial<LibraryPrefs>) => void; names: VersionName[] }) {
  const { t } = useTranslation()
  const label = useSortLabel()
  // With several versions, each one's size sorts too, right after the total.
  const keys: SortKey[] = sortKeys.flatMap((key) =>
    (key === 'size' || key === 'plays' || key === 'lastPlayed') && names.length > 1 ? [key, ...names.map((n) => `${key}:${n.name}` as const)] : [key],
  )
  return (
    <ViewOptionsMenu
      view={prefs}
      onChange={onChange}
      sort={<SortSub keys={keys} label={label} sort={prefs.sort} direction={prefs.direction} onChange={onChange} />}
      cardFields={{ all: cardFields, value: prefs.cardFields, label: (field) => t(`library.view.fields.${field}`) }}
      columns={{ all: tableColumns, value: prefs.columns, label: (column) => t(`library.columns.${column}`) }}
      splittable={names.length > 1}
    />
  )
}

/** The posters have no column headers: their sort lives here. The table's headers set the same sort. */
export function SortSub<K extends string>({
  keys,
  label,
  sort,
  direction,
  onChange,
}: {
  keys: K[]
  label: (key: K) => string
  sort: K
  direction: SortDirection
  onChange: (patch: { sort?: K; direction?: SortDirection }) => void
}) {
  const { t } = useTranslation()
  const Arrow = direction === 'asc' ? ArrowUpIcon : ArrowDownIcon
  return (
    <DropdownMenuSub>
      <DropdownMenuSubTrigger data-testid="library-sort">
        <span className="flex-1">{t('library.sort.label')}</span>
        <span className="flex items-center gap-0.5 text-xs text-muted-foreground">
          {label(sort)}
          <Arrow className="size-3" aria-label={t(`library.sort.${direction}`)} />
        </span>
      </DropdownMenuSubTrigger>
      <DropdownMenuSubContent className="w-44">
        <DropdownMenuRadioGroup value={sort} onValueChange={(value) => onChange({ sort: value as K })}>
          {keys.map((key) => (
            <DropdownMenuRadioItem key={key} value={key} onSelect={keepOpen} data-testid={`library-sort-${key}`}>
              {label(key)}
            </DropdownMenuRadioItem>
          ))}
        </DropdownMenuRadioGroup>
        <DropdownMenuSeparator />
        <DropdownMenuRadioGroup value={direction} onValueChange={(value) => onChange({ direction: value as SortDirection })}>
          <DropdownMenuRadioItem value="asc" onSelect={keepOpen} data-testid="library-sort-asc">
            {t('library.sort.asc')}
          </DropdownMenuRadioItem>
          <DropdownMenuRadioItem value="desc" onSelect={keepOpen} data-testid="library-sort-desc">
            {t('library.sort.desc')}
          </DropdownMenuRadioItem>
        </DropdownMenuRadioGroup>
      </DropdownMenuSubContent>
    </DropdownMenuSub>
  )
}
