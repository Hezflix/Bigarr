import { useRouterState } from '@tanstack/react-router'
import { createContext, useContext, useEffect, useMemo, useState } from 'react'
import { useNavSections } from '@/components/nav'
import { CommandPalette } from './CommandPalette'
import { isPaletteShortcut, isSearchShortcut } from './keys'
import { recordRecent } from './recent'

type PaletteContextValue = {
  open: boolean
  setOpen: (open: boolean) => void
}

const PaletteContext = createContext<PaletteContextValue | null>(null)

/** The command palette of the shell: its open state, the global shortcuts, and the pages it remembers. */
export function PaletteProvider({ children }: { children: React.ReactNode }) {
  const [open, setOpen] = useState(false)

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (isPaletteShortcut(event)) {
        // Browsers bind Ctrl+K to their own search box: the palette wins inside the app.
        event.preventDefault()
        setOpen((value) => !value)
        return
      }
      if (isSearchShortcut(event, event.target) && !document.querySelector('[role="dialog"]')) {
        const input = document.querySelector<HTMLInputElement>('[data-page-search]')
        if (!input) return
        event.preventDefault()
        input.focus()
        input.select()
      }
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [])

  useRecordNavPages()

  const value = useMemo(() => ({ open, setOpen }), [open])
  return (
    <PaletteContext.Provider value={value}>
      {children}
      <CommandPalette open={open} onOpenChange={setOpen} />
    </PaletteContext.Provider>
  )
}

export function usePalette(): PaletteContextValue {
  const value = useContext(PaletteContext)
  if (!value) throw new Error('usePalette needs a <PaletteProvider>')
  return value
}

/** A visit to a page of the nav, however the user got there, puts it first under "Recent". */
function useRecordNavPages() {
  const pathname = useRouterState({ select: (state) => state.location.pathname })
  const sections = useNavSections()
  const paths = useMemo(() => new Set<string>(sections.flatMap((section) => section.items.map((item) => item.to))), [sections])
  useEffect(() => {
    if (paths.has(pathname)) recordRecent({ kind: 'page', to: pathname })
  }, [pathname, paths])
}
