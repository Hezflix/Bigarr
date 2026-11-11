import { describe, expect, test } from 'bun:test'
import type { PlaybackUser } from '../src/api/client'
import { compareUsers, type Sort } from '../src/features/playback/userSort'

const user = (name: string, lastSeenAt: string | null): PlaybackUser => ({
  id: name.length,
  name,
  thumbUrl: null,
  isOwner: false,
  plays: 0,
  lastSeenAt,
  serverName: name,
  keepHistory: true,
  onServer: true,
  onServerCheckedAt: null,
})

const users = [user('ana', '2026-11-01T20:00:00Z'), user('zoe', null), user('ben', '2026-11-09T21:00:00Z'), user('eve', null), user('cy', '2026-10-15T08:00:00Z')]
const order = (sort: Sort) => [...users].sort((a, b) => compareUsers(a, b, sort)).map((u) => u.name)

describe('sorting the users by last seen', () => {
  test('the most recent first, the never seen last by name', () => {
    expect(order({ key: 'lastSeen', direction: 'desc' })).toEqual(['ben', 'ana', 'cy', 'eve', 'zoe'])
  })

  test('the oldest first, the never seen still last', () => {
    expect(order({ key: 'lastSeen', direction: 'asc' })).toEqual(['cy', 'ana', 'ben', 'eve', 'zoe'])
  })
})
