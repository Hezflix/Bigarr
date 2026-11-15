import { createContext, useContext, useEffect, useRef } from 'react'

/** Something the palette runs rather than opens. */
export type PaletteAction = {
  /** Unique within its group; also what cmdk selects by. */
  id: string
  label: string
  icon: React.ComponentType<{ className?: string }>
  /** More words the filter matches on ("rss", "sync", "backup"). */
  keywords?: string[]
  /** Heavy or hard to undo: the palette asks before running it. */
  confirm?: { title: string; description: string }
  /** Started once the palette has closed; it reports its own outcome (toasts). */
  run: () => unknown
}

/** The actions of the page on screen: a film, a series. */
export type PaletteContextActions = { heading: string; get: () => PaletteAction[] }

export type ContextRegistry = (entry: PaletteContextActions) => () => void

export const ContextRegistryContext = createContext<ContextRegistry | null>(null)

/**
 * Offers `actions` in the palette while the calling page is mounted, under `heading`. The latest closures are read when
 * the palette renders, so a page re-rendering on every poll does not re-register anything; only a change of labels does.
 */
export function usePaletteActions(heading: string, actions: PaletteAction[] | null) {
  const register = useContext(ContextRegistryContext)
  const latest = useRef(actions)
  latest.current = actions
  const key = actions ? `${heading}\n${actions.map((a) => `${a.id}:${a.label}`).join('\n')}` : null
  useEffect(() => {
    if (!register || key === null) return
    return register({ heading: key.split('\n')[0], get: () => latest.current ?? [] })
  }, [register, key])
}
