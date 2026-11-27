import { describe, expect, test } from 'bun:test'
import { releasePage } from '../src/features/activity/releasePage'

describe('release page', () => {
  test('is the web address the grab kept', () => {
    expect(releasePage({ data: { nzbInfoUrl: 'https://tracker.example/torrents/42' } })).toBe('https://tracker.example/torrents/42')
  })

  test('is nothing without one, or for anything but a web address', () => {
    expect(releasePage({ data: {} })).toBeNull()
    expect(releasePage({ data: { nzbInfoUrl: '' } })).toBeNull()
    expect(releasePage({ data: { nzbInfoUrl: 'javascript:alert(1)' } })).toBeNull()
  })
})
