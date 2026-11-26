import { describe, expect, test } from 'bun:test'
import { userInitial } from '../src/components/userInitial'

describe('user initial', () => {
  test('is the first letter of the name', () => {
    expect(userInitial('kevin')).toBe('k')
  })

  test('skips the spaces before the name', () => {
    expect(userInitial('  marie')).toBe('m')
  })

  test('keeps an emoji whole', () => {
    expect(userInitial('🦊 fox')).toBe('🦊')
  })

  test('is empty for an empty name', () => {
    expect(userInitial('')).toBe('')
    expect(userInitial('   ')).toBe('')
  })
})
