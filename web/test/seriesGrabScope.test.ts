import { describe, expect, test } from 'bun:test'
import type { Episode } from '../src/api/client'
import { episodeCode, pickableEpisodes, pickerSeasons, prefillEpisodeIds, type SeriesSearchScope } from '../src/features/search/seriesGrabScope'

const ep = (id: number, season: number, number: number, patch: Partial<Episode> = {}) =>
  ({
    id,
    seasonNumber: season,
    episodeNumber: number,
    displaySeasonNumber: season,
    notInOrder: false,
    ownedByFilm: null,
    versions: [{ versionId: 1, targetId: id, monitored: true, hasFile: false, episodeFileId: null }],
    ...patch,
  }) as unknown as Episode
const film = { movieId: 9, title: 'A film', year: 2001 }
const episodes = [
  ep(2, 1, 2),
  ep(1, 1, 1),
  ep(3, 2, 1),
  ep(4, 0, 1),
  ep(5, 0, 2, { ownedByFilm: film }),
  ep(6, 2, 2, { displaySeasonNumber: 1 }),
  ep(7, 3, 1, { versions: [{ versionId: 2, targetId: 70, monitored: true, hasFile: false, episodeFileId: null }] as Episode['versions'] }),
]
const pickable = pickableEpisodes(episodes, 1)
const unmapped = { episodes: [] }
const episodeScope: SeriesSearchScope = { kind: 'episode', episodeId: 3, label: 'S02E01' }
const seasonScope: SeriesSearchScope = { kind: 'season', season: 1, label: 'Season 1' }
const seriesScope: SeriesSearchScope = { kind: 'series', label: 'Show' }

describe('pickableEpisodes', () => {
  test('keeps the version’s episodes and leaves film-owned specials out', () => {
    expect(pickable.map((e) => e.id).sort()).toEqual([1, 2, 3, 4, 6])
    expect(pickableEpisodes(episodes, 2).map((e) => e.id)).toEqual([7])
  })
})

describe('prefillEpisodeIds', () => {
  test('a matched release starts from Bigarr’s match, at every search level', () => {
    const matched = { episodes: [{ id: 3 }, { id: 6 }] }
    for (const scope of [episodeScope, seasonScope, seriesScope]) expect(prefillEpisodeIds(matched, scope, pickable)).toEqual([3, 6])
  })

  test('a match the version cannot take is not ticked', () => {
    expect(prefillEpisodeIds({ episodes: [{ id: 5 }, { id: 7 }, { id: 1 }] }, seriesScope, pickable)).toEqual([1])
  })

  test('an unmatched release from an episode search starts from that episode', () => {
    expect(prefillEpisodeIds(unmapped, episodeScope, pickable)).toEqual([3])
  })

  test('an unmatched release from a season search starts from the season, the order season when searched there', () => {
    expect(prefillEpisodeIds(unmapped, seasonScope, pickable).sort()).toEqual([1, 2])
    expect(prefillEpisodeIds(unmapped, { ...seasonScope, orderSeason: true }, pickable).sort()).toEqual([1, 2, 6])
  })

  test('an unmatched release from a series search starts empty: the user picks', () => {
    expect(prefillEpisodeIds(unmapped, seriesScope, pickable)).toEqual([])
  })
})

describe('pickerSeasons', () => {
  test('seasons in order with the specials last, episodes in order', () => {
    const seasons = pickerSeasons(pickable)
    expect(seasons.map((s) => s.season)).toEqual([1, 2, 0])
    expect(seasons[0].episodes.map((e) => e.id)).toEqual([1, 2])
    expect(seasons[1].episodes.map((e) => e.id)).toEqual([3, 6])
  })

  test('episode codes use TVDB numbers', () => {
    expect(episodeCode({ seasonNumber: 2, episodeNumber: 13 })).toBe('S02E13')
  })
})
