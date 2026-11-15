import { defaultFilter } from 'cmdk'

/** Entries the server ranked (films, series, "Add"): they always pass and keep their order. */
export const isServerItem = (value: string) => value.startsWith('content:') || value.startsWith('add:')

/** The films and series found rank first; "Add '…'" always shows but ranks last, below any page or action that matched. */
const serverScore = (value: string) => (value.startsWith('content:') ? 1 : 0.001)

/** Lower case, no accents, letters and digits only: "Média" and "/settings/media" both give words to match. */
export function paletteWords(text: string): string[] {
  return text
    .normalize('NFD')
    .replace(/\p{M}/gu, '')
    .toLowerCase()
    .split(/[^\p{L}\p{N}]+/u)
    .filter(Boolean)
}

/**
 * cmdk's own filter matches letters in order anywhere ("chihiro" found "History"), which floods a list of short page
 * names. Here every typed word must start a word of the entry; cmdk's score then ranks what is left.
 */
export function paletteFilter(value: string, search: string, keywords?: string[]): number {
  if (isServerItem(value)) return serverScore(value)
  const words = paletteWords(search)
  if (words.length === 0) return 1
  // A parameter's value is only an id ("param:episode:12"): matching on it would find episode 12 for "12".
  const haystack = paletteWords([value.startsWith('param:') ? '' : value, ...(keywords ?? [])].join(' '))
  if (!words.every((word) => haystack.some((candidate) => candidate.startsWith(word)))) return 0
  // Never 0 once the words matched: cmdk would hide the entry.
  return Math.max(defaultFilter(value, search, keywords), 0.01)
}
