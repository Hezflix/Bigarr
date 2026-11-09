import { useMemo, useState } from 'react'
import { CheckIcon, ChevronDownIcon, SearchIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import type { PlaybackUser } from '@/api/client'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import { cn } from 'cn'

/** A user filter that scales to a hundred people: type a few letters, pick one; most active users first. */
export function UserPicker({ users, value, onChange }: { users: PlaybackUser[]; value: number | null; onChange: (id: number | null) => void }) {
  const { t } = useTranslation()
  const [open, setOpen] = useState(false)
  const [search, setSearch] = useState('')
  const selected = users.find((u) => u.id === value)
  const shown = useMemo(() => {
    const needle = search.trim().toLowerCase()
    return users.filter((u) => u.name.toLowerCase().includes(needle)).sort((a, b) => b.plays - a.plays || a.name.localeCompare(b.name))
  }, [users, search])

  const pick = (id: number | null) => {
    onChange(id)
    setOpen(false)
    setSearch('')
  }

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button id="plays-user" variant="outline" role="combobox" aria-expanded={open} className="min-w-44 justify-between font-normal">
          <span className="truncate">{selected?.name ?? t('playback.allUsers')}</span>
          <ChevronDownIcon className="size-4 opacity-50" />
        </Button>
      </PopoverTrigger>
      <PopoverContent align="end" className="w-64 p-0">
        <div className="flex items-center gap-2 border-b px-3">
          <SearchIcon className="size-4 shrink-0 opacity-50" />
          <Input
            autoFocus
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter' && shown.length > 0) pick(shown[0].id)
            }}
            placeholder={t('playback.searchUser')}
            aria-label={t('playback.searchUser')}
            className="h-9 border-0 px-0 shadow-none focus-visible:ring-0"
          />
        </div>
        <ul className="max-h-72 overflow-y-auto p-1" role="listbox">
          {!search && (
            <Option selected={value === null} onSelect={() => pick(null)}>
              {t('playback.allUsers')}
            </Option>
          )}
          {shown.map((user) => (
            <Option key={user.id} selected={user.id === value} onSelect={() => pick(user.id)}>
              <span className="truncate">{user.name}</span>
              <span className="ml-auto text-xs tabular-nums text-muted-foreground">{t('playback.playsCount', { count: user.plays })}</span>
            </Option>
          ))}
          {shown.length === 0 && <li className="px-2 py-3 text-center text-sm text-muted-foreground">{t('playback.noUserFound')}</li>}
        </ul>
      </PopoverContent>
    </Popover>
  )
}

function Option({ selected, onSelect, children }: { selected: boolean; onSelect: () => void; children: React.ReactNode }) {
  return (
    <li role="option" aria-selected={selected}>
      <button type="button" onClick={onSelect} className={cn('flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-left text-sm hover:bg-accent hover:text-accent-foreground', selected && 'font-medium')}>
        <CheckIcon className={cn('size-4 shrink-0', selected ? 'opacity-100' : 'opacity-0')} />
        {children}
      </button>
    </li>
  )
}
