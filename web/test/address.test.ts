import { describe, expect, test } from 'bun:test'
import { sameDestination } from '../src/lib/address'

describe('sameDestination', () => {
  test('the path, the case and the spaces do not matter', () => {
    expect(sameDestination('http://indexer.local:9117/torznab', 'http://indexer.local:9117/other/')).toBe(true)
    expect(sameDestination('http://indexer.local:9117', ' HTTP://INDEXER.LOCAL:9117/ ')).toBe(true)
    expect(sameDestination('https://plex.local', 'https://plex.local:443/')).toBe(true)
    expect(sameDestination(null, '')).toBe(true)
  })

  test('another scheme, host or port, or an address that cannot be read, is another destination', () => {
    expect(sameDestination('http://indexer.local:9117', 'http://elsewhere.local:9117')).toBe(false)
    expect(sameDestination('http://indexer.local:9117', 'http://indexer.local:9118')).toBe(false)
    expect(sameDestination('http://indexer.local', 'https://indexer.local')).toBe(false)
    expect(sameDestination('http://indexer.local', null)).toBe(false)
    expect(sameDestination('http://indexer.local', 'not an address')).toBe(false)
  })
})
