/**
 * The seasons a picker lists: each once, in order, the specials (season 0) last. A file or an issue is rarely about a
 * special, so the list opens on season 1.
 */
export function seasonOptions(seasons: readonly number[]): number[] {
  return [...new Set(seasons)].sort((a, b) => (a === 0 ? 1 : b === 0 ? -1 : a - b))
}
