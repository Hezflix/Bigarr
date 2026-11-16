import { useRouterState } from '@tanstack/react-router'
import { ChevronRightIcon, FilmIcon, HistoryIcon, TriangleAlertIcon, TvIcon, XIcon } from 'lucide-react'
import { useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { type NavItem, useNavSections } from '@/components/nav'
import { CommandDialog, CommandEmpty, CommandGroup, CommandInput, CommandItem, CommandList } from '@/components/ui/command'
import type { PaletteAction, PaletteContextActions, PaletteParamOption } from './actions'
import { AddGroup, ContentGroups, firstResult, useContentSearch } from './ContentGroups'
import { paletteFilter } from './filter'
import { Kbd } from './Kbd'
import { type RecentItem, recentKey, useRecent } from './recent'
import { type PaletteTarget, usePaletteGo } from './target'
import { useGlobalActions } from './useGlobalActions'

type Props = {
  open: boolean
  onOpenChange: (open: boolean) => void
  /** The page's own actions (a film, a series), listed first. */
  contextual: PaletteContextActions | null
}

export function CommandPalette({ open, onOpenChange, contextual }: Props) {
  const { t } = useTranslation()
  const navigateTo = usePaletteGo()
  const sections = useNavSections()
  const [recent, clearRecent] = useRecent()
  const pathname = useRouterState({ select: (state) => state.location.pathname })
  const [query, setQuery] = useState('')
  // Controlled so the content search can move the selection to its first result.
  const [selected, setSelected] = useState('')
  // A heavy action waiting for a yes: the list gives way to the question.
  const [confirming, setConfirming] = useState<PaletteAction | null>(null)
  // Tab on an action that takes a parameter: the list gives way to its values; `query` is what to restore on the way back.
  const [param, setParam] = useState<{ action: PaletteAction & { param: NonNullable<PaletteAction['param']> }; query: string } | null>(null)
  // Tab, → or Cmd/Ctrl+Enter on a season: its episodes replace the list; `query` is what to restore on the way back.
  const [drill, setDrill] = useState<{ option: PaletteParamOption; query: string } | null>(null)
  const globalActions = useGlobalActions()
  const pageActions = open && contextual ? contextual.get() : []

  // Where a recent page sends: its nav entry, for the label and the icon.
  const navByPath = useMemo(() => {
    const map = new Map<string, { item: NavItem; section: string }>()
    for (const section of sections) for (const item of section.items) map.set(item.to, { item, section: section.title })
    return map
  }, [sections])

  // The library search is only for the list itself: the question and a parameter's values reuse the input for other text.
  const content = useContentSearch(confirming || param ? '' : query)

  // What a library hit competes with for the first place: the pages and actions that match the term.
  const rivals = [...pageActions, ...globalActions]
    .map((a) => ({ value: `action:${a.id}`, keywords: [a.label, ...(a.keywords ?? [])] }))
    .concat(sections.flatMap((section) => section.items.map((item) => ({ value: `${section.title} ${item.label} ${item.to}`, keywords: [item.label] }))))
    .reduce<{ value: string; score: number } | null>((top, e) => {
      const score = query === '' ? 0 : paletteFilter(e.value, query, e.keywords)
      return score > (top?.score ?? 0) ? { value: e.value, score } : top
    }, null)
  // A page or action named like the term (or starting with it) comes before library hits that only hold its words:
  // typing "Discover" lists the Discover page first, not under every film with the word in its title.
  const rivalFirst = rivals !== null && content.top !== null && rivals.score > content.top.score
  // cmdk selects the first row when the term changes, but not when the library rows arrive later: say which one it is.
  const rivalRef = useRef(rivals)
  rivalRef.current = rivals
  const { fresh, debounced } = content
  useEffect(() => {
    const first = firstResult(fresh, debounced)
    if (!first) return
    const rival = rivalRef.current
    // Only when new rows arrive, through the ref: running it again at every render would undo the arrow keys.
    setSelected(rival && rival.score > first.score ? rival.value : first.value)
  }, [fresh, debounced])

  // The values of the parameter on screen: the action's, or those of the season drilled into.
  const paramOptions = useMemo(() => param?.action.param.options() ?? [], [param])
  const visibleOptions = drill?.option.narrow?.options ?? paramOptions

  const close = () => {
    onOpenChange(false)
    setQuery('')
    setSelected('')
    setConfirming(null)
    setParam(null)
    setDrill(null)
  }
  const leaveParam = () => {
    if (!param) return
    setQuery(param.query)
    setSelected(`action:${param.action.id}`)
    setParam(null)
    setDrill(null)
  }
  function enterDrill(option: PaletteParamOption) {
    if (!option.narrow) return
    setDrill({ option, query })
    setQuery('')
    setSelected('')
  }
  const leaveDrill = () => {
    if (!drill) return
    setQuery(drill.query)
    setSelected(`param:${drill.option.id}`)
    setDrill(null)
  }
  const onInputKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (param) {
      if (e.key === 'Backspace' && query === '') {
        e.preventDefault()
        if (drill) leaveDrill()
        else leaveParam()
        return
      }
      // The focus stays in the field.
      if (e.key === 'Tab') e.preventDefault()
      const option = drill ? undefined : visibleOptions.find((o) => `param:${o.id}` === selected)
      if (!option?.narrow) return
      const input = e.currentTarget
      const caretAtEnd = input.selectionStart === input.value.length && input.selectionEnd === input.value.length
      const plain = !e.shiftKey && !e.altKey && !e.metaKey && !e.ctrlKey
      if ((e.key === 'Tab' && !e.shiftKey) || (e.key === 'ArrowRight' && plain && caretAtEnd) || (e.key === 'Enter' && (e.metaKey || e.ctrlKey))) {
        e.preventDefault()
        enterDrill(option)
      }
      return
    }
    if (e.key !== 'Tab' || e.shiftKey) return
    const action = [...pageActions, ...globalActions].find((a) => `action:${a.id}` === selected)
    if (!action?.param) return
    e.preventDefault()
    enterParam(action)
  }
  function enterParam(action: PaletteAction) {
    if (!action.param) return
    setParam({ action: { ...action, param: action.param }, query })
    setQuery('')
    setSelected('')
  }
  const go = (target: PaletteTarget) => {
    close()
    navigateTo(target)
  }
  const run = (action: PaletteAction) => {
    if (action.confirm && confirming !== action) {
      setConfirming(action)
      setSelected('confirm:run')
      return
    }
    close()
    action.run()
  }
  const actionItem = (action: PaletteAction) => (
    <CommandItem
      key={action.id}
      value={`action:${action.id}`}
      keywords={[action.label, ...(action.keywords ?? [])]}
      onSelect={() => run(action)}
      data-testid={`palette-action-${action.id}`}
      className="group"
    >
      <action.icon />
      <span className="truncate">
        {action.label}
        {/* The ellipsis says a question comes before anything runs. */}
        {action.confirm && '…'}
      </span>
      {action.param && <ParamHint hint={action.param.hint} onOpen={() => enterParam(action)} />}
    </CommandItem>
  )

  const recentRows = recent.flatMap((item) => {
    const row = recentRow(item, navByPath)
    // The page on screen is not somewhere to go back to.
    return row && targetPath(row.target) !== pathname ? [row] : []
  })

  return (
    <CommandDialog
      open={open}
      // Esc or a click outside while asking only steps back to the list.
      onOpenChange={(next) => (next ? onOpenChange(true) : confirming ? setConfirming(null) : drill ? leaveDrill() : param ? leaveParam() : close())}
      title={t('palette.title')}
      description={t('palette.description')}
      showCloseButton={false}
      className="top-[max(1rem,12vh)] translate-y-0 sm:max-w-xl"
      commandProps={{ loop: true, filter: paletteFilter, shouldFilter: !confirming, value: selected, onValueChange: setSelected, className: '[&_[cmdk-item]]:py-2' }}
    >
      {confirming ? (
        <ConfirmView action={confirming} onRun={() => run(confirming)} onCancel={() => setConfirming(null)} />
      ) : param ? (
        <ParamView
          action={param.action}
          options={visibleOptions}
          drilled={drill?.option.narrow?.label ?? null}
          onDrill={enterDrill}
          query={query}
          onQueryChange={setQuery}
          onKeyDown={onInputKeyDown}
          onRun={(option) => {
            close()
            param.action.param.run(option)
          }}
        />
      ) : (
        <>
          <CommandInput
            placeholder={t('palette.placeholder')}
            value={query}
            onValueChange={setQuery}
            onKeyDown={onInputKeyDown}
            // Back from a question, the input is mounted again and takes the focus back.
            autoFocus
            data-testid="palette-input"
          />
          <CommandList className="max-h-[min(60vh,28rem)]" data-testid="palette-list">
            <CommandEmpty>{t('palette.empty')}</CommandEmpty>

            {contextual && pageActions.length > 0 && <CommandGroup heading={contextual.heading}>{pageActions.map(actionItem)}</CommandGroup>}

            {query === '' && recentRows.length > 0 && (
              <CommandGroup heading={t('palette.recent')}>
                {recentRows.map((row) => (
                  <CommandItem key={row.key} value={`recent ${row.key}`} onSelect={() => go(row.target)}>
                    <row.icon />
                    <span className="truncate">{row.label}</span>
                    <span className="truncate text-xs text-muted-foreground">{row.hint}</span>
                    <HistoryIcon className="ml-auto size-3.5!" aria-hidden="true" />
                  </CommandItem>
                ))}
                <CommandItem value="recent clear" onSelect={clearRecent} className="text-muted-foreground">
                  <span className="pl-7 text-xs">{t('palette.clearRecent')}</span>
                </CommandItem>
              </CommandGroup>
            )}

            {!rivalFirst && <ContentGroups search={content} onSelect={go} />}

            <CommandGroup heading={t('palette.actionsHeading')}>{globalActions.map(actionItem)}</CommandGroup>

            {sections.map((section) => (
              <CommandGroup key={section.title} heading={t('palette.goToSection', { section: section.title })}>
                {section.items.map((item) => (
                  <CommandItem
                    key={item.to}
                    // The value must be unique (three pages are called "Library"): the section and the path make it so,
                    // and they also let "settings lib" find the right one.
                    value={`${section.title} ${item.label} ${item.to}`}
                    // The first keyword is the page's name: a page named like the term ranks above library hits.
                    keywords={[item.label]}
                    onSelect={() => go({ kind: 'page', to: item.to })}
                    data-testid={`palette-go-${item.to}`}
                  >
                    <item.icon />
                    <span className="truncate">{item.label}</span>
                  </CommandItem>
                ))}
              </CommandGroup>
            ))}

            {rivalFirst && <ContentGroups search={content} onSelect={go} />}

            <AddGroup query={query} onSelect={go} />
          </CommandList>
        </>
      )}
      <PaletteFooter param={param !== null} />
    </CommandDialog>
  )
}

function targetPath(target: PaletteTarget): string | null {
  if (target.kind === 'page') return target.to
  if (target.kind === 'movie') return `/movies/${target.id}`
  if (target.kind === 'series') return `/series/${target.id}`
  return null
}

type RecentRow = { key: string; target: PaletteTarget; label: string; hint: string; icon: NavItem['icon'] }

function recentRow(item: RecentItem, navByPath: Map<string, { item: NavItem; section: string }>): RecentRow | null {
  const key = recentKey(item)
  const hint = item.kind === 'page' ? '' : (item.year?.toString() ?? '')
  if (item.kind === 'movie') return { key, target: { kind: 'movie', id: item.id }, label: item.title, hint, icon: FilmIcon }
  if (item.kind === 'series') return { key, target: { kind: 'series', id: item.id }, label: item.title, hint, icon: TvIcon }
  const entry = navByPath.get(item.to)
  // A page that left the nav since it was visited is simply not offered any more.
  if (!entry) return null
  return { key, target: { kind: 'page', to: entry.item.to }, label: entry.item.label, hint: entry.section, icon: entry.item.icon }
}

/** "Search every missing movie?": Enter runs it, Esc goes back to the list. */
function ConfirmView({ action, onRun, onCancel }: { action: PaletteAction; onRun: () => void; onCancel: () => void }) {
  const { t } = useTranslation()
  // The input that held the focus is gone: without this, Enter and the arrows reach nothing and cmdk never sees them.
  const ref = useRef<HTMLDivElement>(null)
  useEffect(() => ref.current?.focus(), [])
  return (
    <div ref={ref} tabIndex={-1} className="outline-none" data-testid="palette-confirm">
      <div className="flex gap-3 border-b px-4 py-3">
        <TriangleAlertIcon className="mt-0.5 size-4 shrink-0 text-amber-600 dark:text-amber-400" aria-hidden="true" />
        <div className="grid gap-1">
          <p className="text-sm font-medium">{action.confirm?.title}</p>
          <p className="text-sm text-muted-foreground">{action.confirm?.description}</p>
        </div>
      </div>
      <CommandList>
        <CommandGroup>
          <CommandItem value="confirm:run" onSelect={onRun} data-testid="palette-confirm-run">
            <action.icon />
            {t('palette.confirm.run')}
          </CommandItem>
          <CommandItem value="confirm:cancel" onSelect={onCancel}>
            <XIcon />
            {t('palette.confirm.back')}
          </CommandItem>
        </CommandGroup>
      </CommandList>
    </div>
  )
}

/**
 * Raycast's argument placeholder: a dashed field after the label naming what Tab asks for, and the Tab key on the row
 * the arrows are on.
 */
function ParamHint({ hint, onOpen }: { hint: string; onOpen: () => void }) {
  const { t } = useTranslation()
  return (
    <>
      {/* A button too: touch screens have no Tab key, and a mouse may prefer to click. */}
      <button
        type="button"
        // Handled here, not by the row: a click on the row runs the action on the whole series.
        onClick={(e) => {
          e.stopPropagation()
          onOpen()
        }}
        onPointerDown={(e) => e.preventDefault()}
        className="-my-0.5 min-w-0 shrink truncate rounded-md border border-dashed border-muted-foreground/40 px-2 py-0.5 text-xs text-muted-foreground hover:border-foreground/50 hover:text-foreground"
        aria-label={t('palette.param.choose', { hint })}
        data-testid="palette-param-hint"
      >
        {hint}
      </button>
      <Kbd className="ml-auto hidden group-data-[selected=true]:sm:inline-flex" aria-hidden="true">
        Tab
      </Kbd>
    </>
  )
}

/** The values of an action's parameter, behind a chip naming the action. Backspace on an empty field or Esc goes back. */
function ParamView({
  action,
  options,
  drilled,
  onDrill,
  query,
  onQueryChange,
  onKeyDown,
  onRun,
}: {
  action: PaletteAction & { param: NonNullable<PaletteAction['param']> }
  options: PaletteParamOption[]
  /** The season whose episodes are listed, once drilled into one. */
  drilled: string | null
  onDrill: (option: PaletteParamOption) => void
  query: string
  onQueryChange: (query: string) => void
  onKeyDown: (e: React.KeyboardEvent<HTMLInputElement>) => void
  onRun: (option: PaletteParamOption) => void
}) {
  const { t } = useTranslation()
  const shown = query === '' ? options.filter((o) => !o.onlyWhenTyping) : options
  const groups = [...new Set(shown.map((o) => o.group))]
  const hidden = query === '' && shown.length < options.length
  return (
    <>
      <CommandInput
        before={
          <span className="flex max-w-[65%] shrink-0 items-center gap-1 rounded-md bg-muted px-1.5 py-0.5 text-xs font-medium" data-testid="palette-param-chip">
            <action.icon className="size-3.5 shrink-0" />
            <span className="truncate">{action.label}</span>
            {drilled && (
              <>
                <ChevronRightIcon className="size-3 shrink-0 text-muted-foreground" aria-hidden="true" />
                <span className="shrink-0">{drilled}</span>
              </>
            )}
          </span>
        }
        placeholder={drilled ? t('palette.param.episodePlaceholder') : action.param.hint}
        value={query}
        onValueChange={onQueryChange}
        onKeyDown={onKeyDown}
        autoFocus
        data-testid="palette-input"
      />
      <CommandList className="max-h-[min(60vh,28rem)]" data-testid="palette-list">
        <CommandEmpty>{t('palette.empty')}</CommandEmpty>
        {groups.map((group) => (
          <CommandGroup key={group} heading={group}>
            {shown
              .filter((o) => o.group === group)
              .map((o) => (
                <CommandItem key={o.id} value={`param:${o.id}`} keywords={[o.label, ...(o.keywords ?? [])]} onSelect={() => onRun(o)} data-testid={`palette-param-${o.id}`} className="group">
                  <span className="truncate">{o.label}</span>
                  {o.hint && <span className="ml-auto shrink-0 pl-2 text-xs text-muted-foreground tabular-nums">{o.hint}</span>}
                  {o.narrow && <NarrowButton option={o} onOpen={() => onDrill(o)} />}
                </CommandItem>
              ))}
          </CommandGroup>
        ))}
        {hidden && <p className="px-3 py-2 text-xs text-muted-foreground">{t('palette.param.typeForEpisodes')}</p>}
      </CommandList>
    </>
  )
}

/**
 * Enter on a season runs the action on all of it; this lists its episodes instead. A button for touch screens, which
 * have no Tab, and the key hints on the row the arrows are on.
 */
function NarrowButton({ option, onOpen }: { option: PaletteParamOption; onOpen: () => void }) {
  const { t } = useTranslation()
  return (
    <>
      <button
        type="button"
        // Handled here, not by the row: a click on the row runs the action on the whole season.
        onClick={(e) => {
          e.stopPropagation()
          onOpen()
        }}
        onPointerDown={(e) => e.preventDefault()}
        className="-my-1 flex min-h-8 shrink-0 items-center gap-0.5 rounded-md border px-2 text-xs text-muted-foreground hover:border-foreground/40 hover:text-foreground"
        aria-label={t('palette.param.pickEpisode', { season: option.narrow?.label })}
        data-testid={`palette-param-narrow-${option.id}`}
      >
        {t('palette.param.episodesGroup')}
        <ChevronRightIcon className="size-3.5" aria-hidden="true" />
      </button>
      <Kbd className="hidden group-data-[selected=true]:sm:inline-flex" aria-hidden="true">
        Tab
      </Kbd>
    </>
  )
}

/** The keys that drive the list; hidden on touch screens, where they mean nothing. */
function PaletteFooter({ param }: { param: boolean }) {
  const { t } = useTranslation()
  return (
    <div className="hidden items-center gap-4 border-t px-3 py-2 text-xs text-muted-foreground sm:flex">
      <span className="flex items-center gap-1">
        <Kbd>↑</Kbd>
        <Kbd>↓</Kbd>
        {t('palette.keys.move')}
      </span>
      <span className="flex items-center gap-1">
        <Kbd>↵</Kbd>
        {t('palette.keys.open')}
      </span>
      {param ? (
        <span className="ml-auto flex items-center gap-1">
          <Kbd>⌫</Kbd>
          {t('palette.keys.back')}
        </span>
      ) : (
        <span className="ml-auto flex items-center gap-1">
          <Kbd>Esc</Kbd>
          {t('palette.keys.close')}
        </span>
      )}
    </div>
  )
}
