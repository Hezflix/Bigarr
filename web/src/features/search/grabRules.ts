/** Release ordering and grab rules shared by the film and series searches, kept free of components so they can be tested. */

const collator = new Intl.Collator(undefined, { sensitivity: 'base', numeric: true })

type Ranked = { resolution: number; revisionVersion: number; quality: string }

/** Ascending: resolution, then the revision (a repack or proper after the original), then the name. */
export const qualityComparator = (a: Ranked, b: Ranked) => a.resolution - b.resolution || a.revisionVersion - b.revisionVersion || collator.compare(a.quality, b.quality)

/** Ascending by the languages' names, a release without any last. */
export const languagesComparator = (a: { languages: string[] }, b: { languages: string[] }) =>
  (a.languages.length === 0 ? 1 : 0) - (b.languages.length === 0 ? 1 : 0) || collator.compare(a.languages.join(', '), b.languages.join(', '))

/** What a version thinks of a release, as both searches return it. */
export type Verdict = { approved: boolean; rejections: { reason: string }[] } | undefined

/**
 * Why a grab goes against the engine for this version: the release is not the film or show, or the version rejects
 * it (temporary reasons included). Empty when the version accepts it: the grab then goes without a question.
 */
export function grabObjections(matches: boolean, mismatch: { reason: string }[], verdict: Verdict, noVerdict: string): string[] {
  const reasons = matches ? [] : mismatch.map((r) => r.reason)
  if (!verdict) return [...reasons, noVerdict]
  if (!verdict.approved) reasons.push(...verdict.rejections.map((r) => r.reason))
  return reasons
}
