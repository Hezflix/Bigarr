import type { MediaManagementRequest } from '../../api/client'

/** What a setting applies to: only films, only series, or both (one value read by the film and the series imports). Also the page's tabs. */
export type Scope = 'films' | 'series' | 'both'

/** The tabs, in the order they are shown; also the order the first tab with an error is looked for. */
export const scopes: Scope[] = ['films', 'series', 'both']

export const isScope = (value: unknown): value is Scope => value === 'films' || value === 'series' || value === 'both'

/**
 * The media management resource holds settings of all three scopes (the API has one resource for them), so each field says which tab it
 * lives in. A `Record` over every key makes a field added to the API fail to compile until it is given a tab.
 */
export const filmFieldScope: Record<keyof MediaManagementRequest, Scope> = {
  renameFiles: 'films',
  fileNameTemplate: 'films',
  folderNameTemplate: 'films',
  replaceIllegalCharacters: 'films',
  colonReplacement: 'films',
  seasonPackUpgrade: 'series',
  seasonPackUpgradeThreshold: 'series',
  transferMode: 'both',
  recycleBinPath: 'both',
  recycleBinRetentionDays: 'both',
  importExtraFiles: 'both',
  extraFileExtensions: 'both',
}

/** The tabs holding an edit not saved yet: the film-side resource split by field, the series naming form as a whole. */
export function dirtyScopes(form: MediaManagementRequest, saved: MediaManagementRequest, seriesDirty: boolean): Record<Scope, boolean> {
  const result: Record<Scope, boolean> = { films: false, series: seriesDirty, both: false }
  for (const key of Object.keys(filmFieldScope) as (keyof MediaManagementRequest)[]) {
    if (JSON.stringify(form[key]) !== JSON.stringify(saved[key])) result[filmFieldScope[key]] = true
  }
  return result
}

/** The first tab (in display order) that has something to fix, or undefined when every tab is valid. */
export function firstScopeWithError(errors: Record<Scope, boolean>): Scope | undefined {
  return scopes.find((scope) => errors[scope])
}
