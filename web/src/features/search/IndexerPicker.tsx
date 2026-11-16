import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { ChevronDownIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { indexers as indexersApi } from '@/api/client'
import { Button } from '@/components/ui/button'
import {
  DropdownMenu,
  DropdownMenuCheckboxItem,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { pickedIndexerIds, readExcluded, writeExcluded } from '@/features/search/searchIndexers'

export type IndexerPick = {
  /** The indexers with interactive search on, in priority order; empty until loaded. */
  searchable: { id: number; name: string }[]
  excluded: number[]
  setExcluded: (ids: number[]) => void
  /** What the next search sends: undefined for every indexer. */
  ids: number[] | undefined
  /** Every indexer is left out: nothing to search. */
  none: boolean
  /** The indexer list has answered (or failed: every indexer is then searched), so `ids` is final. */
  ready: boolean
}

/** The indexers the interactive search asks, every one with interactive search on unless the user leaves some out. */
export function useIndexerPick(): IndexerPick {
  const list = useQuery({ queryKey: ['indexers'], queryFn: indexersApi.list, staleTime: 60_000 })
  const [excluded, setState] = useState(readExcluded)
  const searchable = (list.data ?? []).filter((i) => i.enableInteractiveSearch).sort((a, b) => a.priority - b.priority || a.name.localeCompare(b.name))
  const ids = list.isSuccess ? pickedIndexerIds(searchable, excluded) : undefined
  return {
    searchable,
    excluded,
    setExcluded: (next) => {
      setState(next)
      writeExcluded(next)
    },
    ids,
    none: ids !== undefined && ids.length === 0,
    ready: !list.isPending,
  }
}

/** "Indexers: All" or "Indexers: 2 of 3", a menu of check boxes. The pick applies to the next search. */
export function IndexerPicker({ pick, testId }: { pick: IndexerPick; testId: string }) {
  const { t } = useTranslation()
  const { searchable, excluded, setExcluded, ids } = pick
  if (searchable.length < 2) return null

  const out = new Set(excluded)
  const toggle = (id: number, on: boolean) => setExcluded(on ? excluded.filter((e) => e !== id) : [...excluded, id])
  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button type="button" variant="outline" data-testid={testId} data-picked={ids === undefined ? 'all' : ids.join(',')}>
          {ids === undefined ? t('searchIndexers.all') : t('searchIndexers.some', { count: ids.length, total: searchable.length })}
          <ChevronDownIcon aria-hidden="true" />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="min-w-56">
        <DropdownMenuLabel className="font-normal text-xs text-muted-foreground">{t('searchIndexers.hint')}</DropdownMenuLabel>
        {searchable.map((indexer) => (
          <DropdownMenuCheckboxItem
            key={indexer.id}
            checked={!out.has(indexer.id)}
            onCheckedChange={(on) => toggle(indexer.id, on === true)}
            // Stays open: several indexers are usually ticked in a row.
            onSelect={(e) => e.preventDefault()}
            data-testid={`${testId}-item`}
          >
            {indexer.name}
          </DropdownMenuCheckboxItem>
        ))}
        <DropdownMenuSeparator />
        <DropdownMenuItem disabled={ids === undefined} onSelect={() => setExcluded([])} data-testid={`${testId}-all`}>
          {t('searchIndexers.reset')}
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  )
}
