import type { ItemFilmPair, SureMatch } from '@/api/client'

/** The pairs to link: every match the owner left checked, in the list's order. */
export function checkedPairs(matches: SureMatch[], unchecked: ReadonlySet<number>): ItemFilmPair[] {
  return matches.filter((m) => !unchecked.has(m.item.id)).map((m) => ({ itemId: m.item.id, movieId: m.film.movieId }))
}

/** The header checkbox's state: all, none, or some (indeterminate). */
export function allChecked(matches: SureMatch[], unchecked: ReadonlySet<number>): boolean | 'indeterminate' {
  const off = matches.filter((m) => unchecked.has(m.item.id)).length
  return off === 0 ? true : off === matches.length ? false : 'indeterminate'
}
