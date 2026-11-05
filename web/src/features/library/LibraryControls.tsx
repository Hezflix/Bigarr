import { useState, type FormEvent, type ReactNode } from 'react'
import { BookmarkPlusIcon, Grid2x2Icon, LayoutGridIcon, ListFilterIcon, SearchIcon, Settings2Icon, SortAscIcon, TableIcon, XIcon } from 'lucide-react'
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
  sortKeys,
  statusFilters,
  tableColumns,
  viewModes,
  type FilterScope,
  type LibraryFilter,
  type LibraryPrefs,
  type MonitoredFilter,
  type Preset,
  type SavedView,
  type StatusFilter,
  type VersionName,
  type ViewMode,
} from './libraryView'

/** Menu items that change a setting keep the menu open: several are usually changed in a row. */
const keepOpen = (event: Event) => event.preventDefault()

const toggle = <T,>(values: T[], value: T) => (values.includes(value) ? values.filter((v) => v !== value) : [...values, value])

// ---- presets and saved views ------------------------------------------------

type SavedViewsProps = {
  presets: (Preset & { count: number })[]
  savedViews: SavedView[]
  activeId: string | null
  onPreset: (preset: Preset) => void
  onSaved: (view: SavedView) => void
  onSave: (name: string) => void
  onDelete: (id: string) => void
}

/** Built-in views, then the user's own; the tab of the view matching the current filter is highlighted. */
export function SavedViewsBar({ presets, savedViews, activeId, onPreset, onSaved, onSave, onDelete }: SavedViewsProps) {
  const { t } = useTranslation()
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
          {preset.version ? t('library.presets.missingVersion', { name: preset.version }) : t(`library.presets.${preset.id as 'all'}`)}
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
  matrix: <Grid2x2Icon />,
}

export function LibraryControls({ prefs, onChange, term, onTermChange, names, qualities, tags, rootFolders }: ControlsProps) {
  const { t } = useTranslation()

  return (
    <div className="flex flex-wrap items-center gap-2" data-testid="library-controls">
      <div className="relative w-full sm:w-64">
        <SearchIcon className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-muted-foreground" aria-hidden="true" />
        <Input
          type="search"
          value={term}
          onChange={(e) => onTermChange(e.target.value)}
          placeholder={t('library.searchPlaceholder')}
          aria-label={t('library.searchPlaceholder')}
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
            aria-selected={prefs.mode === mode}
            variant={prefs.mode === mode ? 'secondary' : 'ghost'}
            size="xs"
            className="h-7 px-2"
            onClick={() => onChange({ mode })}
            data-testid={`library-mode-${mode}`}
          >
            {modeIcons[mode]}
            <span className="hidden lg:inline">{t(`library.modes.${mode}`)}</span>
          </Button>
        ))}
      </div>

      <div className="ml-auto flex flex-wrap items-center gap-2">
        <SortMenu prefs={prefs} onChange={onChange} />
        <FilterMenu filter={prefs.filter} onChange={(filter) => onChange({ filter })} names={names} qualities={qualities} tags={tags} rootFolders={rootFolders} />
        <ViewMenu prefs={prefs} onChange={onChange} />
      </div>
    </div>
  )
}

function SortMenu({ prefs, onChange }: { prefs: LibraryPrefs; onChange: (patch: Partial<LibraryPrefs>) => void }) {
  const { t } = useTranslation()
  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button type="button" variant="outline" size="sm" data-testid="library-sort">
          <SortAscIcon />
          {t(`library.sort.${prefs.sort}`)}
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="w-48">
        <DropdownMenuLabel>{t('library.sort.label')}</DropdownMenuLabel>
        <DropdownMenuRadioGroup value={prefs.sort} onValueChange={(value) => onChange({ sort: value as LibraryPrefs['sort'] })}>
          {sortKeys.map((key) => (
            <DropdownMenuRadioItem key={key} value={key} onSelect={keepOpen} data-testid={`library-sort-${key}`}>
              {t(`library.sort.${key}`)}
            </DropdownMenuRadioItem>
          ))}
        </DropdownMenuRadioGroup>
        <DropdownMenuSeparator />
        <DropdownMenuRadioGroup value={prefs.direction} onValueChange={(value) => onChange({ direction: value as LibraryPrefs['direction'] })}>
          <DropdownMenuRadioItem value="asc" onSelect={keepOpen} data-testid="library-sort-asc">
            {t('library.sort.asc')}
          </DropdownMenuRadioItem>
          <DropdownMenuRadioItem value="desc" onSelect={keepOpen} data-testid="library-sort-desc">
            {t('library.sort.desc')}
          </DropdownMenuRadioItem>
        </DropdownMenuRadioGroup>
      </DropdownMenuContent>
    </DropdownMenu>
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
  const active = activeFilterCount(filter)
  const set = (patch: Partial<LibraryFilter>) => onChange({ ...filter, ...patch })
  const scopes: FilterScope[] = ['any', 'all', 'default', ...names.map((n) => `version:${n.name}` as const)]
  const scopeLabel = (scope: FilterScope) => (scope.startsWith('version:') ? scope.slice('version:'.length) : t(`library.filter.scopes.${scope as 'any'}`))

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
        <DropdownMenuSub>
          <DropdownMenuSubTrigger data-testid="library-filter-scope">
            <span className="flex-1">{t('library.filter.scope')}</span>
            <span className="text-xs text-muted-foreground">{scopeLabel(filter.scope)}</span>
          </DropdownMenuSubTrigger>
          <DropdownMenuSubContent>
            <DropdownMenuLabel className="max-w-56 text-xs font-normal text-muted-foreground">{t('library.filter.scopeHint')}</DropdownMenuLabel>
            <DropdownMenuRadioGroup value={filter.scope} onValueChange={(value) => set({ scope: value as FilterScope })}>
              {scopes.map((scope) => (
                <DropdownMenuRadioItem key={scope} value={scope} onSelect={keepOpen}>
                  {scopeLabel(scope)}
                </DropdownMenuRadioItem>
              ))}
            </DropdownMenuRadioGroup>
          </DropdownMenuSubContent>
        </DropdownMenuSub>
        <DropdownMenuSub>
          <DropdownMenuSubTrigger data-testid="library-filter-monitored">
            <span className="flex-1">{t('library.filter.monitored')}</span>
            <span className="text-xs text-muted-foreground">{t(`library.filter.monitoredValues.${filter.monitored}`)}</span>
          </DropdownMenuSubTrigger>
          <DropdownMenuSubContent>
            <DropdownMenuRadioGroup value={filter.monitored} onValueChange={(value) => set({ monitored: value as MonitoredFilter })}>
              {(['any', 'monitored', 'unmonitored'] as const).map((value) => (
                <DropdownMenuRadioItem key={value} value={value} onSelect={keepOpen}>
                  {t(`library.filter.monitoredValues.${value}`)}
                </DropdownMenuRadioItem>
              ))}
            </DropdownMenuRadioGroup>
          </DropdownMenuSubContent>
        </DropdownMenuSub>
        <DropdownMenuSub>
          <DropdownMenuSubTrigger data-testid="library-filter-status">
            <span className="flex-1">{t('library.filter.status')}</span>
            <span className="text-xs text-muted-foreground">{t(`library.filter.statuses.${filter.status}`)}</span>
          </DropdownMenuSubTrigger>
          <DropdownMenuSubContent>
            <DropdownMenuRadioGroup value={filter.status} onValueChange={(value) => set({ status: value as StatusFilter })}>
              {statusFilters.map((value) => (
                <DropdownMenuRadioItem key={value} value={value} onSelect={keepOpen} data-testid={`library-filter-status-${value}`}>
                  {t(`library.filter.statuses.${value}`)}
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
        <CheckboxSub
          label={t('library.filter.hasVersion')}
          options={names.map((n) => ({ value: n.name, label: n.name }))}
          values={filter.hasVersions}
          onToggle={(value) => set({ hasVersions: toggle(filter.hasVersions, value) })}
          testId="library-filter-has"
        />
        <CheckboxSub
          label={t('library.filter.lacksVersion')}
          options={names.map((n) => ({ value: n.name, label: n.name }))}
          values={filter.lacksVersions}
          onToggle={(value) => set({ lacksVersions: toggle(filter.lacksVersions, value) })}
          testId="library-filter-lacks"
        />
        <DropdownMenuSub>
          <DropdownMenuSubTrigger>
            <span className="flex-1">{t('library.filter.added')}</span>
            <span className="text-xs text-muted-foreground">
              {filter.addedWithinDays === null ? t('library.filter.addedAny') : t('library.filter.addedDays', { count: filter.addedWithinDays })}
            </span>
          </DropdownMenuSubTrigger>
          <DropdownMenuSubContent>
            <DropdownMenuRadioGroup
              value={String(filter.addedWithinDays ?? 'any')}
              onValueChange={(value) => set({ addedWithinDays: value === 'any' ? null : Number(value) })}
            >
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
        <DropdownMenuSeparator />
        <DropdownMenuItem onSelect={() => onChange(emptyFilter)} disabled={active === 0} data-testid="library-filter-clear">
          <XIcon />
          {t('library.clearFilters')}
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  )
}

function CheckboxSub<T extends string | number>({
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

function ViewMenu({ prefs, onChange }: { prefs: LibraryPrefs; onChange: (patch: Partial<LibraryPrefs>) => void }) {
  const { t } = useTranslation()
  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button type="button" variant="outline" size="sm" data-testid="library-view-options">
          <Settings2Icon />
          {t('library.view.label')}
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="w-56">
        <DropdownMenuSub>
          <DropdownMenuSubTrigger data-testid="library-poster-size">
            <span className="flex-1">{t('library.view.posterSize')}</span>
            <span className="text-xs text-muted-foreground">{t(`library.view.sizes.${prefs.posterSize}`)}</span>
          </DropdownMenuSubTrigger>
          <DropdownMenuSubContent>
            <DropdownMenuRadioGroup value={prefs.posterSize} onValueChange={(value) => onChange({ posterSize: value as LibraryPrefs['posterSize'] })}>
              {posterSizes.map((size) => (
                <DropdownMenuRadioItem key={size} value={size} onSelect={keepOpen} data-testid={`library-poster-${size}`}>
                  {t(`library.view.sizes.${size}`)}
                </DropdownMenuRadioItem>
              ))}
            </DropdownMenuRadioGroup>
          </DropdownMenuSubContent>
        </DropdownMenuSub>
        <DropdownMenuSub>
          <DropdownMenuSubTrigger data-testid="library-card-fields">{t('library.view.cardFields')}</DropdownMenuSubTrigger>
          <DropdownMenuSubContent>
            {cardFields.map((field) => (
              <DropdownMenuCheckboxItem
                key={field}
                checked={prefs.cardFields.includes(field)}
                onCheckedChange={() => onChange({ cardFields: toggle(prefs.cardFields, field) })}
                onSelect={keepOpen}
                data-testid={`library-card-field-${field}`}
              >
                {t(`library.view.fields.${field}`)}
              </DropdownMenuCheckboxItem>
            ))}
          </DropdownMenuSubContent>
        </DropdownMenuSub>
        <DropdownMenuSub>
          <DropdownMenuSubTrigger data-testid="library-columns">{t('library.view.columns')}</DropdownMenuSubTrigger>
          <DropdownMenuSubContent>
            {tableColumns.map((column) => (
              <DropdownMenuCheckboxItem
                key={column}
                checked={prefs.columns.includes(column)}
                // Keep the declared order whatever the order of the clicks.
                onCheckedChange={() => onChange({ columns: tableColumns.filter((c) => (c === column ? !prefs.columns.includes(c) : prefs.columns.includes(c))) })}
                onSelect={keepOpen}
                data-testid={`library-column-${column}`}
              >
                {t(`library.columns.${column}`)}
              </DropdownMenuCheckboxItem>
            ))}
          </DropdownMenuSubContent>
        </DropdownMenuSub>
        <DropdownMenuSeparator />
        <DropdownMenuLabel className="text-xs font-normal text-muted-foreground">{t('library.view.density')}</DropdownMenuLabel>
        <DropdownMenuRadioGroup value={prefs.density} onValueChange={(value) => onChange({ density: value as LibraryPrefs['density'] })}>
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
