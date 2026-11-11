import { useCallback, useEffect, useRef, useState } from 'react'

/** What every library view needs to show and change the bulk selection. */
export type Selection = {
  selecting: boolean
  selected: ReadonlySet<number>
  /** `range`: shift-click, everything between the last clicked item and this one is selected. */
  onToggle: (id: number, range: boolean) => void
}

/** Selection mode of a library page (films or series). `ordered` is the list as shown, for shift-click ranges. */
export function useSelection(ordered: { id: number }[]) {
  const [selecting, setSelecting] = useState(false)
  const [selected, setSelected] = useState<ReadonlySet<number>>(() => new Set())
  const anchor = useRef<number | null>(null)

  const onToggle = useCallback(
    (movieId: number, range: boolean) => {
      const from = range && anchor.current !== null ? ordered.findIndex((m) => m.id === anchor.current) : -1
      const to = from >= 0 ? ordered.findIndex((m) => m.id === movieId) : -1
      setSelected((current) => {
        const next = new Set(current)
        if (from >= 0 && to >= 0) {
          for (let i = Math.min(from, to); i <= Math.max(from, to); i++) next.add(ordered[i].id)
        } else if (next.has(movieId)) {
          next.delete(movieId)
        } else {
          next.add(movieId)
        }
        return next
      })
      anchor.current = movieId
    },
    [ordered],
  )

  const add = useCallback((ids: Iterable<number>) => setSelected((current) => new Set([...current, ...ids])), [])
  const remove = useCallback((ids: Iterable<number>) => {
    setSelected((current) => {
      const next = new Set(current)
      for (const id of ids) next.delete(id)
      return next
    })
  }, [])
  const clear = useCallback(() => {
    setSelected(new Set())
    anchor.current = null
  }, [])
  const stop = useCallback(() => {
    setSelecting(false)
    clear()
  }, [clear])

  // Esc leaves selection mode, unless a dialog or menu is open and takes the key first.
  useEffect(() => {
    if (!selecting) return
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape' && !event.defaultPrevented && !document.querySelector('[role="dialog"], [role="menu"]')) stop()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [selecting, stop])

  return { selecting, start: () => setSelecting(true), stop, selected, onToggle, add, remove, clear }
}
