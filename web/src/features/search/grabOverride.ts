/**
 * Sonarr's "override and grab": the release is grabbed as what the user says it is when the title reads wrong. Only what
 * differs from the parse is sent, so a grab left as parsed stays a plain grab (no override in the history).
 */
export type GrabOverride = { qualityId?: number; languages?: string[]; episodesOverridden?: boolean }

/** The choice in the dialog: a quality id (0: as parsed), languages (empty: as parsed), the episodes ticked (series). */
export type OverrideChoice = { qualityId: number; languages: string[]; episodeIds?: number[] }

const sameSet = (a: (string | number)[], b: (string | number)[]) => {
  const left = new Set(a)
  const right = new Set(b)
  return left.size === right.size && [...left].every((x) => right.has(x))
}

export function grabOverride(release: { qualityId: number; languages: string[]; episodes?: { id: number }[] }, choice: OverrideChoice): GrabOverride {
  const override: GrabOverride = {}
  if (choice.qualityId > 0 && choice.qualityId !== release.qualityId) override.qualityId = choice.qualityId
  if (choice.languages.length > 0 && !sameSet(choice.languages, release.languages)) override.languages = [...choice.languages]
  if (choice.episodeIds && release.episodes && !sameSet(choice.episodeIds, release.episodes.map((e) => e.id))) override.episodesOverridden = true
  return override
}

/** What is overridden, for the button and the toast: `quality`, `languages`, `episodes`. */
export const overriddenParts = (override: GrabOverride) =>
  [override.qualityId !== undefined ? 'quality' : null, override.languages ? 'languages' : null, override.episodesOverridden ? 'episodes' : null].filter(
    (p): p is 'quality' | 'languages' | 'episodes' => p !== null,
  )
