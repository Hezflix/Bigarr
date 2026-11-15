import { FilmIcon, HistoryIcon, TvIcon } from 'lucide-react'
import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { type NavItem, useNavSections } from '@/components/nav'
import { CommandDialog, CommandEmpty, CommandGroup, CommandInput, CommandItem, CommandList } from '@/components/ui/command'
import { ContentGroups } from './ContentGroups'
import { paletteFilter } from './filter'
import { Kbd } from './Kbd'
import { type RecentItem, recentKey, useRecent } from './recent'
import { type PaletteTarget, usePaletteGo } from './target'


type Props = { open: boolean; onOpenChange: (open: boolean) => void }

export function CommandPalette({ open, onOpenChange }: Props) {
  const { t } = useTranslation()
  const navigateTo = usePaletteGo()
  const sections = useNavSections()
  const [recent, clearRecent] = useRecent()
  const [query, setQuery] = useState('')
  // Controlled so the content search can move the selection to its first result.
  const [selected, setSelected] = useState('')

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
  }
  const go = (target: PaletteTarget) => {
    close()
    navigateTo(target)
  }

  const recentRows = recent.flatMap((item) => {
    const row = recentRow(item, navByPath)
    return row ? [row] : []
  })

  return (
    <CommandDialog
      open={open}
      onOpenChange={(next) => (next ? onOpenChange(true) : close())}
      title={t('palette.title')}
      description={t('palette.description')}
      showCloseButton={false}
      className="top-[max(1rem,12vh)] translate-y-0 sm:max-w-xl"
      commandProps={{ loop: true, filter: paletteFilter, value: selected, onValueChange: setSelected, className: '[&_[cmdk-item]]:py-2' }}
    >
      <CommandInput placeholder={t('palette.placeholder')} value={query} onValueChange={setQuery} data-testid="palette-input" />
      <CommandList className="max-h-[min(60vh,28rem)]" data-testid="palette-list">
        <CommandEmpty>{t('palette.empty')}</CommandEmpty>

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
      </CommandList>
      <PaletteFooter />
    </CommandDialog>
  )
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
