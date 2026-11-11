import type { PlaybackUser } from '@/api/client'

/** How the Users table sorts; kept apart from the page so the order can be tested. */
export type SortKey = 'name' | 'plays' | 'lastSeen' | 'access'
export type Sort = { key: SortKey; direction: 'asc' | 'desc' }

/** Names sort A to Z first; numbers, dates and access the most first (on the server before off). */
export const firstDirection = (key: SortKey): Sort['direction'] => (key === 'name' ? 'asc' : 'desc')

/** On, then off, then not known: the unknown last whichever the direction. */
const accessRank = (u: PlaybackUser) => (u.onServer === null ? null : u.onServer ? 1 : 0)

export function compareUsers(a: PlaybackUser, b: PlaybackUser, sort: Sort): number {
  const sign = sort.direction === 'asc' ? 1 : -1
  const byName = a.name.localeCompare(b.name)
  switch (sort.key) {
    case 'name':
      return sign * byName
    case 'plays':
      return sign * (a.plays - b.plays) || byName
    case 'lastSeen':
      // Never seen goes last whatever the direction, like the library's empty values.
      if (a.lastSeenAt === null || b.lastSeenAt === null) return a.lastSeenAt === b.lastSeenAt ? byName : a.lastSeenAt === null ? 1 : -1
      return sign * a.lastSeenAt.localeCompare(b.lastSeenAt) || byName
    case 'access': {
      const ra = accessRank(a)
      const rb = accessRank(b)
      if (ra === rb) return b.plays - a.plays || byName
      if (ra === null) return 1
      if (rb === null) return -1
      return sign * (ra - rb)
    }
  }
}
