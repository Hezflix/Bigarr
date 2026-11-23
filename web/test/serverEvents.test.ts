import { describe, expect, test } from 'bun:test'
import { eventTypes, invalidations, keysFor } from '../src/hooks/serverEvents'

const catalogueUrl = new URL('../../src/Bigarr.Domain/Events/DomainEvents.cs', import.meta.url)

describe('server events', () => {
  test('every event the server publishes is handled', async () => {
    const source = await Bun.file(catalogueUrl).text()
    const published = [...source.matchAll(/\(typeof\(\w+\), "(\w+)",/g)].map((match) => match[1] as string)
    expect(published.length).toBeGreaterThan(30)
    expect(published.filter((name) => name !== 'test').sort()).toEqual([...eventTypes].sort())
  })

  test('every handled event refreshes something', () => {
    for (const type of eventTypes) expect(invalidations[type].length).toBeGreaterThan(0)
  })

  test('a film event also refreshes that film', () => {
    const keys = keysFor('imported', { movie: { id: 7 } })
    expect(keys).toContainEqual(['movie', 7])
    expect(keys).toContainEqual(['search', 7])
  })

  test('an episode event also refreshes that series', () => {
    const keys = keysFor('episodeImported', { series: { id: 3 } })
    expect(keys).toContainEqual(['seriesDetail', 3])
    expect(keys).toContainEqual(['seriesEpisodes', 3])
    expect(keys).toContainEqual(['seriesFiles', 3])
    expect(keys).toContainEqual(['series'])
  })

  test('an event with no subject refreshes only its lists', () => {
    expect(keysFor('healthIssue', {})).toEqual([['health']])
  })
})
