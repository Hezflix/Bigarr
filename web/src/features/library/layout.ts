import { useLayoutEffect, useState, type RefObject } from 'react'

/** Width of the element, kept current by a ResizeObserver: the poster grid derives its column count from it. */
export function useElementWidth(ref: RefObject<HTMLElement | null>): number {
  const [width, setWidth] = useState(0)
  useLayoutEffect(() => {
    const element = ref.current
    if (!element) return
    setWidth(element.clientWidth)
    const observer = new ResizeObserver(([entry]) => setWidth(entry.contentRect.width))
    observer.observe(element)
    return () => observer.disconnect()
  }, [ref])
  return width
}

/**
 * Distance from the top of the document to the element. The window virtualizers need it as `scrollMargin`; the
 * controls above the list can wrap or grow, so it is re-read after every render (a no-op when it did not move).
 */
export function useDocumentOffset(ref: RefObject<HTMLElement | null>): number {
  const [offset, setOffset] = useState(0)
  useLayoutEffect(() => {
    const element = ref.current
    if (!element) return
    const top = element.getBoundingClientRect().top + window.scrollY
    if (Math.abs(top - offset) > 0.5) setOffset(top)
  })
  return offset
}

/**
 * Whether the content is wider than its frame. The table and the matrix keep their header sticky (window scroll) when
 * the columns fit, and fall back to a horizontal scroll box, where a sticky header cannot work, when they do not.
 */
export function useOverflows(frame: RefObject<HTMLElement | null>, content: RefObject<HTMLElement | null>): boolean {
  const frameWidth = useElementWidth(frame)
  const contentWidth = useElementWidth(content)
  return frameWidth > 0 && contentWidth > frameWidth + 1
}
