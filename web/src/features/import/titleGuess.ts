/** The library ranked against what a file name says, for the manual import's picker before anything is typed. */

export type GuessCandidate = { id: number; title: string; year: number | null }

const words = (text: string) =>
  text
    .toLowerCase()
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .split(/[^a-z0-9]+/)
    .filter((w) => w.length > 0)

/**
 * What a series file name says before its episode marker, quality or year: `Breaking.Bad.S02E01.1080p…` → `Breaking Bad`.
 * Empty when the name starts with the marker.
 */
export function titleFromFileName(name: string): string {
  const base = name.replace(/\.[a-z0-9]{2,4}$/i, '')
  const cut = base.search(/(^|[ ._-])(s\d{1,2}e\d{1,3}|s\d{1,2}\b|\d{1,2}x\d{2}|season[ ._-]?\d|\d{3,4}p|(19|20)\d{2})([ ._-]|$)/i)
  return (cut < 0 ? base : base.slice(0, cut)).replace(/[._]+/g, ' ').replace(/[\s-]+$/, '').trim()
}

/**
 * At most `count` candidates sharing a word with the parsed title, most shared words first, then the closest year, then
 * the title. `first` are ids to put on top whatever their score (the title other rows of the import already picked).
 */
export function bestGuesses<T extends GuessCandidate>(all: T[], parsed: string | null, year: number | null, count = 5, first: number[] = []): T[] {
  const wanted = new Set(words(parsed ?? ''))
  const shared = (c: T) => words(c.title).filter((w) => wanted.has(w)).length
  const distance = (c: T) => (year !== null && c.year !== null ? Math.abs(year - c.year) : 100)
  const pinned = first.map((id) => all.find((c) => c.id === id)).filter((c): c is T => c !== undefined)
  const ranked = all
    .filter((c) => !first.includes(c.id) && shared(c) > 0)
    .sort((a, b) => shared(b) - shared(a) || distance(a) - distance(b) || a.title.localeCompare(b.title))
  return [...new Set([...pinned, ...ranked])].slice(0, count)
}
