import { describe, expect, test } from 'bun:test'
import type { TFunction } from 'i18next'
import type { Episode } from '../src/api/client'
import { scopeChoices } from '../src/features/search/seriesGrabScope'

const t = ((key: string, options?: Record<string, unknown>) => `${key}${options ? JSON.stringify(options) : ''}`) as unknown as TFunction
const ep = (id: number, season: number, patch: Partial<Episode> = {}) =>
  ({ id, seasonNumber: season, displaySeasonNumber: season, notInOrder: false, ownedByFilm: null, ...patch }) as unknown as Episode
const episodes = [ep(1, 1), ep(2, 1), ep(3, 2), ep(4, 0), ep(5, 0, { ownedByFilm: { movieId: 9, title: 'A film', year: 2001 } }), ep(6, 2, { displaySeasonNumber: 1 })]

describe('scopeChoices', () => {
  test('an episode search grabs for that episode', () => {
    expect(scopeChoices({ kind: 'episode', episodeId: 3, label: 'S02E01' }, episodes, t).map((c) => c.episodeIds)).toEqual([[3]])
  })

  test('a season search grabs for the season, the order season when searched there', () => {
    expect(scopeChoices({ kind: 'season', season: 1, label: 'Season 1' }, episodes, t)[0].episodeIds).toEqual([1, 2])
    expect(scopeChoices({ kind: 'season', season: 1, label: 'Season 1', orderSeason: true }, episodes, t)[0].episodeIds).toEqual([1, 2, 6])
  })

  test('a series search offers every episode, then each season, specials last and films left out', () => {
    const choices = scopeChoices({ kind: 'series', label: 'Show' }, episodes, t)
    expect(choices.map((c) => c.value)).toEqual(['all', 'season-1', 'season-2', 'season-0'])
    expect(choices[0].episodeIds).toEqual([1, 2, 3, 6])
    expect(choices[3].episodeIds).toEqual([4])
  })

  test('nothing to grab for when the season has no episodes', () => {
    expect(scopeChoices({ kind: 'season', season: 7, label: 'Season 7' }, episodes, t)).toEqual([])
  })
})
