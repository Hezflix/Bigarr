import { describe, expect, test } from 'bun:test'
import { formatShare, formatShares } from '@/i18n'

describe('formatShare', () => {
  test('whole percentages from 1 % up', () => {
    expect(formatShare(42, 66)).toBe('64%')
    expect(formatShare(1, 3)).toBe('33%')
    expect(formatShare(2, 3)).toBe('67%')
    expect(formatShare(5, 5)).toBe('100%')
  })

  test('one decimal under 1 %', () => {
    expect(formatShare(4, 1000)).toBe('0.4%')
    expect(formatShare(9, 1000)).toBe('0.9%')
    expect(formatShare(99, 10000)).toBe('1%')
  })

  test('too small to show, and none', () => {
    expect(formatShare(1, 10000)).toBe('<0.1%')
    expect(formatShare(0, 10)).toBe('0%')
    expect(formatShare(3, 0)).toBe('0%')
  })
})

describe('formatShares', () => {
  test('always adds up to 100 %', () => {
    expect(formatShares([1, 1, 1])).toEqual(['34%', '33%', '33%'])
    expect(formatShares([5, 3])).toEqual(['63%', '37%'])
    expect(formatShares([1, 1, 1, 1, 1, 1])).toEqual(['17%', '17%', '17%', '17%', '16%', '16%'])
  })

  test('a tiny non-zero share, zeros, and nothing at all', () => {
    expect(formatShares([999, 1])).toEqual(['100%', '<1%'])
    expect(formatShares([4, 0])).toEqual(['100%', '0%'])
    expect(formatShares([0, 0])).toEqual(['0%', '0%'])
  })
})
