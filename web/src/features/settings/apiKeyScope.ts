import type { ApiKeyScope } from '@/api/client'

export const API_KEY_SCOPES: readonly ApiKeyScope[] = ['compat', 'full', 'calendar']

/** The scope string an old or unknown server value falls back to: the narrowest useful one, as the server defaults it. */
export function asScope(value: string): ApiKeyScope {
  return value === 'full' || value === 'calendar' ? value : 'compat'
}

/**
 * The scopes a key may take. A `calendar` key has no view, so a key with a view other than `default` cannot become one
 * (the server answers 400).
 */
export function scopesFor(compatView: string): ApiKeyScope[] {
  return API_KEY_SCOPES.filter((scope) => scope !== 'calendar' || compatView === 'default')
}

/**
 * What a scope change does to a key's reach: `full` opens everything `compat` and `calendar` open and more, while
 * `compat` and `calendar` open disjoint surfaces, so any other change takes something away at once.
 */
export function scopeChange(current: ApiKeyScope, next: ApiKeyScope): 'none' | 'widen' | 'narrow' {
  if (current === next) return 'none'
  return next === 'full' ? 'widen' : 'narrow'
}
