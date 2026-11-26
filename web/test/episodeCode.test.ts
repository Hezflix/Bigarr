import { describe, expect, test } from 'bun:test'
import { episodeCode } from '../src/lib/episodeCode'

describe('episode code', () => {
  test('pads the season and the episode to two digits', () => {
    expect(episodeCode(2, 13)).toBe('S02E13')
    expect(episodeCode(1, 5)).toBe('S01E05')
  })

  test('writes the specials as season 00', () => {
    expect(episodeCode(0, 3)).toBe('S00E03')
  })

  test('keeps numbers of three digits whole', () => {
    expect(episodeCode(2024, 110)).toBe('S2024E110')
  })
})
