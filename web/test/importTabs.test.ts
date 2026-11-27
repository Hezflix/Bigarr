import { describe, expect, test } from 'bun:test'
import { importSources, importTone, isImportSource } from '../src/features/settings/importTabs'

describe('import tabs', () => {
  test('a tab is one of the four sources', () => {
    for (const source of importSources) expect(isImportSource(source)).toBe(true)
    expect(isImportSource('lidarr')).toBe(false)
    expect(isImportSource(undefined)).toBe(false)
  })

  test('the icon tone follows the last run', () => {
    expect(importTone(undefined)).toBe('idle')
    expect(importTone('idle')).toBe('idle')
    expect(importTone('queued')).toBe('running')
    expect(importTone('running')).toBe('running')
    expect(importTone('completed')).toBe('done')
    expect(importTone('failed')).toBe('failed')
  })
})
