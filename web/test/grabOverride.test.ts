import { describe, expect, test } from 'bun:test'
import { grabOverride, overriddenParts } from '../src/features/search/grabOverride'

const release = { qualityId: 4, languages: ['English'], episodes: [{ id: 10 }, { id: 11 }] }

describe('grab override', () => {
  test('a choice left as parsed sends nothing', () => {
    expect(grabOverride(release, { qualityId: 4, languages: ['English'], episodeIds: [11, 10] })).toEqual({})
    expect(overriddenParts({})).toEqual([])
  })

  test('only what differs from the parse is sent', () => {
    expect(grabOverride(release, { qualityId: 7, languages: ['English'] })).toEqual({ qualityId: 7 })
    expect(grabOverride(release, { qualityId: 4, languages: ['French', 'English'] })).toEqual({ languages: ['French', 'English'] })
    expect(grabOverride(release, { qualityId: 4, languages: ['English'], episodeIds: [12] })).toEqual({ episodesOverridden: true })
  })

  test('no quality and no language mean as the title says', () => {
    expect(grabOverride(release, { qualityId: 0, languages: [] })).toEqual({})
  })

  test('episodes picked for a release matching none are an override', () => {
    expect(grabOverride({ ...release, episodes: [] }, { qualityId: 4, languages: ['English'], episodeIds: [10] })).toEqual({ episodesOverridden: true })
  })

  test('the parts overridden are named in order', () => {
    expect(overriddenParts({ episodesOverridden: true, languages: ['French'], qualityId: 7 })).toEqual(['quality', 'languages', 'episodes'])
  })
})
