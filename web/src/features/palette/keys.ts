/** Apple keyboards say ⌘ where the others say Ctrl. */
export function isApplePlatform(): boolean {
  if (typeof navigator === 'undefined') return false
  const platform = (navigator as Navigator & { userAgentData?: { platform?: string } }).userAgentData?.platform ?? navigator.platform ?? ''
  return /mac|iphone|ipad|ipod/i.test(platform) || /Mac OS X/.test(navigator.userAgent)
}

/** Cmd+K on a Mac, Ctrl+K elsewhere; both are accepted on every platform. */
export function isPaletteShortcut(event: Pick<KeyboardEvent, 'key' | 'metaKey' | 'ctrlKey' | 'altKey' | 'shiftKey'>): boolean {
  return event.key.toLowerCase() === 'k' && (event.metaKey || event.ctrlKey) && !event.altKey && !event.shiftKey
}

/** "/" jumps to the page's search box, unless the user is already typing somewhere. */
export function isSearchShortcut(event: Pick<KeyboardEvent, 'key' | 'metaKey' | 'ctrlKey' | 'altKey'>, target: EventTarget | null): boolean {
  return event.key === '/' && !event.metaKey && !event.ctrlKey && !event.altKey && !isEditable(target)
}

export function isEditable(target: EventTarget | null): boolean {
  if (typeof HTMLElement === 'undefined' || !(target instanceof HTMLElement)) return false
  if (target.isContentEditable) return true
  const tag = target.tagName
  return tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT'
}
