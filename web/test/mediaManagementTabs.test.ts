import { describe, expect, test } from 'bun:test'
import type { MediaManagementRequest } from '../src/api/client'
import { dirtyScopes, filmFieldScope, firstScopeWithError, isScope, type Scope } from '../src/features/settings/mediaManagementTabs'

const saved: MediaManagementRequest = {
  renameFiles: true,
  fileNameTemplate: '{Movie Title}',
  folderNameTemplate: '{Movie Title} ({Release Year})',
  transferMode: 'hardlinkOrCopy',
  recycleBinPath: null,
  recycleBinRetentionDays: 7,
  importExtraFiles: true,
  extraFileExtensions: 'srt',
  seasonPackUpgrade: 'all',
  seasonPackUpgradeThreshold: 100,
  replaceIllegalCharacters: true,
  colonReplacement: 'delete',
}

const none = (over: Partial<Record<Scope, boolean>> = {}): Record<Scope, boolean> => ({ films: false, series: false, both: false, ...over })

describe('dirtyScopes', () => {
  test('nothing is dirty for an untouched form', () => {
    expect(dirtyScopes({ ...saved }, saved, false)).toEqual(none())
  })

  test('each field marks the tab it lives in', () => {
    expect(dirtyScopes({ ...saved, fileNameTemplate: 'x' }, saved, false)).toEqual(none({ films: true }))
    expect(dirtyScopes({ ...saved, seasonPackUpgrade: 'any' }, saved, false)).toEqual(none({ series: true }))
    expect(dirtyScopes({ ...saved, extraFileExtensions: 'srt,nfo' }, saved, false)).toEqual(none({ both: true }))
  })

  test('the series naming form marks the series tab, with film-side fields of other tabs unaffected', () => {
    expect(dirtyScopes({ ...saved, recycleBinRetentionDays: 30 }, saved, true)).toEqual(none({ series: true, both: true }))
  })

  test('every field of the resource has a tab', () => {
    expect(Object.keys(filmFieldScope).sort()).toEqual(Object.keys(saved).sort())
  })
})

describe('firstScopeWithError', () => {
  test('picks the first tab in display order', () => {
    expect(firstScopeWithError(none({ both: true, series: true }))).toBe('series')
    expect(firstScopeWithError(none({ both: true }))).toBe('both')
  })

  test('is undefined when every tab is valid', () => {
    expect(firstScopeWithError(none())).toBeUndefined()
  })
})

describe('isScope', () => {
  test('accepts the three tabs only', () => {
    expect(['films', 'series', 'both'].every(isScope)).toBe(true)
    expect(isScope('other')).toBe(false)
    expect(isScope(undefined)).toBe(false)
  })
})
