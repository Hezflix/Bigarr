import { describe, expect, test } from 'bun:test'
import { asScope, scopeChange, scopesFor } from '../src/features/settings/apiKeyScope'

describe('scopesFor', () => {
  test('a key on the default view may take every scope', () => {
    expect(scopesFor('default')).toEqual(['compat', 'full', 'calendar'])
  })

  test('a key on another view cannot become a calendar key', () => {
    expect(scopesFor('all')).toEqual(['compat', 'full'])
    expect(scopesFor('version:4k')).toEqual(['compat', 'full'])
  })
})

describe('scopeChange', () => {
  test('the same scope changes nothing', () => {
    expect(scopeChange('compat', 'compat')).toBe('none')
  })

  test('only full widens a key', () => {
    expect(scopeChange('compat', 'full')).toBe('widen')
    expect(scopeChange('calendar', 'full')).toBe('widen')
  })

  test('every other change takes something away', () => {
    expect(scopeChange('full', 'compat')).toBe('narrow')
    expect(scopeChange('full', 'calendar')).toBe('narrow')
    expect(scopeChange('compat', 'calendar')).toBe('narrow')
    expect(scopeChange('calendar', 'compat')).toBe('narrow')
  })
})

describe('asScope', () => {
  test('an unknown value reads as compat', () => {
    expect(asScope('full')).toBe('full')
    expect(asScope('calendar')).toBe('calendar')
    expect(asScope('compat')).toBe('compat')
    expect(asScope('whatever')).toBe('compat')
  })
})
