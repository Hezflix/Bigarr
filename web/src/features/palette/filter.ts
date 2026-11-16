import { defaultFilter } from 'cmdk'

/** Entries the server ranked (films, series, "Add"): they always pass and keep their order. */
export const isServerItem = (value: string) => value.startsWith('content:') || value.startsWith('add:')

/**
 * How well a film or series found in the library matches what was typed, as the palette ranks it against pages and
 * actions. An exact title, or one that starts with the term, is what was asked for; a title where each word merely
 * starts a word of it is a loose hit; "typo" is what the server found by forgiving a slip, and ranks below anything
 * on the list that matches as typed.
 */
export type ContentTier = 'exact' | 'prefix' | 'words' | 'typo'

// A page or action named exactly like the term beats a library hit that starts with it; both beat a loose hit.
const contentScores: Record<ContentTier, number> = { exact: 1.6, prefix: 1.3, words: 1, typo: 0.3 }
const nameExact = 1.5
const namePrefix = 1.2

export const contentScore = (tier: ContentTier) => contentScores[tier]

/** "Add '…'" always shows but ranks last, below any page or action that matched. */
const serverScore = (value: string) => (value.startsWith('content:') ? contentScore((value.split(':')[3] as ContentTier | undefined) ?? 'words') : 0.001)

const articles = new Set(['the', 'a', 'an', 'le', 'la', 'les', 'l', 'un', 'une', 'el', 'los', 'las', 'una', 'il', 'lo', 'gli', 'der', 'die', 'das', 'ein', 'eine', 'het', 'een', 'o', 'os', 'as'])
const withoutArticle = (words: string[]) => (words.length > 1 && articles.has(words[0]) ? words.slice(1) : words)
const isYear = (word: string) => /^\d{4}$/.test(word) && Number(word) >= 1880 && Number(word) <= 2200

/** The tier of a library hit for a term. `shown` is the title that matched: the alternative one when the main title did not. */
export function contentTier(term: string, shown: string): ContentTier {
  const title = withoutArticle(paletteWords(shown))
  const typed = withoutArticle(paletteWords(term))
  // "alien 1979": the year narrows, it is not a word of the title.
  const readings = typed.length > 1 && isYear(typed[typed.length - 1]) ? [typed, typed.slice(0, -1)] : [typed]
  let best: ContentTier = 'typo'
  for (const words of readings) {
    if (words.length === 0) continue
    if (words.length === title.length && words.every((w, i) => title[i] === w)) return 'exact'
    if (words.length < title.length && words.every((w, i) => title[i] === w)) best = 'prefix'
    else if (best === 'typo' && words.every((w) => title.some((t) => t.startsWith(w)))) best = 'words'
  }
  return best
}

/** A page or action whose name is the term, or starts with it, ranks above a library hit that only has the words. */
function nameBoost(name: string | undefined, words: string[]): number {
  if (!name) return 0
  const label = paletteWords(name)
  if (label.length === words.length && words.every((w, i) => label[i] === w)) return nameExact
  if (words.length <= label.length && words.every((w, i) => (i < words.length - 1 ? label[i] === w : label[i].startsWith(w)))) return namePrefix
  return 0
}

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
  // The name is the first keyword (an action's label, a page's label); a parameter's value is not ranked against content.
  const boost = value.startsWith('param:') ? 0 : nameBoost(keywords?.[0], words)
  // Never 0 once the words matched: cmdk would hide the entry.
  return Math.max(defaultFilter(value, search, keywords), boost, 0.01)
}
