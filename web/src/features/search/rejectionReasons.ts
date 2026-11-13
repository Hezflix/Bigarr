/**
 * How a release's rejection reasons are shown in the interactive searches, kept free of components so it can be tested.
 *
 * TEMPORARY: six displays are compared side by side (`?reasons=1..6`); once one is picked the switch and the other five go.
 */

export const reasonsVariants = [1, 2, 3, 4, 5, 6] as const
export type ReasonsVariant = (typeof reasonsVariants)[number]

/** The recommended display, shown when the URL does not pick one. */
export const defaultReasonsVariant: ReasonsVariant = 1

/** `?reasons=N` in the query string, the default otherwise (or for anything that is not 1 to 6). */
export function parseReasonsVariant(search: string): ReasonsVariant {
  const value = Number(new URLSearchParams(search).get('reasons'))
  return (reasonsVariants as readonly number[]).includes(value) ? (value as ReasonsVariant) : defaultReasonsVariant
}

/** True when the URL picks a display: the comparison picker is shown then. */
export const reasonsPicked = (search: string) => new URLSearchParams(search).has('reasons')

export type FitStatus = 'accepted' | 'temporary' | 'rejected'

/** One version's verdict on a release, with the reasons that apply to grabbing it there. */
export type VersionFit = {
  versionId: number
  versionName: string
  score: number
  status: FitStatus
  reasons: { reason: string; temporary: boolean }[]
}

type Decision = { versionId: number; versionName: string; customFormatScore: number; approved: boolean; rejections: { reason: string; temporary?: boolean }[] }

/**
 * Each version's verdict as one line. The release's own reasons (not this film or show) come first for every version,
 * as the grab confirmation asks them: a version that would accept the release still cannot take a release of another title.
 * With no verdict at all, the release's reasons stand alone under `releaseLabel`.
 */
export function versionFits(decisions: Decision[], releaseReasons: string[], releaseLabel: string): VersionFit[] {
  const own = releaseReasons.map((reason) => ({ reason, temporary: false }))
  // No version judged it (a title that cannot be read as a release): the release's reasons still get their line.
  if (decisions.length === 0) return own.length > 0 ? [{ versionId: -1, versionName: releaseLabel, score: 0, status: 'rejected', reasons: own }] : []
  return decisions.map((d) => {
    const reasons = [...own, ...(d.approved ? [] : d.rejections.map((r) => ({ reason: r.reason, temporary: !!r.temporary })))]
    const status: FitStatus = reasons.length === 0 ? 'accepted' : reasons.every((r) => r.temporary) ? 'temporary' : 'rejected'
    return { versionId: d.versionId, versionName: d.versionName, score: d.customFormatScore, status, reasons }
  })
}

const rank: Record<FitStatus, number> = { accepted: 0, temporary: 1, rejected: 2 }

/** The best verdict any version gives: what the row's colour says at a glance. */
export function bestStatus(fits: VersionFit[]): FitStatus {
  return fits.reduce<FitStatus>((best, f) => (rank[f.status] < rank[best] ? f.status : best), 'rejected')
}
