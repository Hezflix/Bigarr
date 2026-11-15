import { useRouterState } from '@tanstack/react-router'
import { FilmIcon, HistoryIcon, TriangleAlertIcon, TvIcon, XIcon } from 'lucide-react'
import { useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { type NavItem, useNavSections } from '@/components/nav'
import { CommandDialog, CommandEmpty, CommandGroup, CommandInput, CommandItem, CommandList } from '@/components/ui/command'
import type { PaletteAction, PaletteContextActions } from './actions'
import { AddGroup, ContentGroups } from './ContentGroups'
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
  const globalActions = useGlobalActions()
  const pageActions = open && contextual ? contextual.get() : []

  // Where a recent page sends: its nav entry, for the label and the icon.
  const navByPath = useMemo(() => {
    const map = new Map<string, { item: NavItem; section: string }>()
    for (const section of sections) for (const item of section.items) map.set(item.to, { item, section: section.title })
    return map
  }, [sections])

  const close = () => {
    onOpenChange(false)
    setQuery('')
    setSelected('')
    setConfirming(null)
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
    >
      <action.icon />
      <span className="truncate">
        {action.label}
        {/* The ellipsis says a question comes before anything runs. */}
        {action.confirm && '…'}
      </span>
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
      onOpenChange={(next) => (next ? onOpenChange(true) : confirming ? setConfirming(null) : close())}
      title={t('palette.title')}
      description={t('palette.description')}
      showCloseButton={false}
      className="top-[max(1rem,12vh)] translate-y-0 sm:max-w-xl"
      commandProps={{ loop: true, filter: paletteFilter, shouldFilter: !confirming, value: selected, onValueChange: setSelected, className: '[&_[cmdk-item]]:py-2' }}
    >
      {confirming ? (
        <ConfirmView action={confirming} onRun={() => run(confirming)} onCancel={() => setConfirming(null)} />
      ) : (
        <>
          <CommandInput
            placeholder={t('palette.placeholder')}
            value={query}
            onValueChange={setQuery}
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

            <ContentGroups query={query} onSelect={go} onFirstResult={setSelected} />

            <CommandGroup heading={t('palette.actionsHeading')}>{globalActions.map(actionItem)}</CommandGroup>

            {sections.map((section) => (
              <CommandGroup key={section.title} heading={t('palette.goToSection', { section: section.title })}>
                {section.items.map((item) => (
                  <CommandItem
                    key={item.to}
                    // The value must be unique (three pages are called "Library"): the section and the path make it so,
                    // and they also let "settings lib" find the right one.
                    value={`${section.title} ${item.label} ${item.to}`}
                    onSelect={() => go({ kind: 'page', to: item.to })}
                    data-testid={`palette-go-${item.to}`}
                  >
                    <item.icon />
                    <span className="truncate">{item.label}</span>
                  </CommandItem>
                ))}
              </CommandGroup>
            ))}

            <AddGroup query={query} onSelect={go} />
          </CommandList>
        </>
      )}
      <PaletteFooter />
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

/** The keys that drive the list; hidden on touch screens, where they mean nothing. */
function PaletteFooter() {
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
      <span className="ml-auto flex items-center gap-1">
        <Kbd>Esc</Kbd>
        {t('palette.keys.close')}
      </span>
    </div>
  )
}
