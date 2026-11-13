import { describe, expect, test } from 'bun:test'
import { grabObjections, languagesComparator, qualityComparator } from '../src/features/search/grabRules'

describe('grabObjections', () => {
  test('an accepted release of the right show grabs without a question', () => {
    expect(grabObjections(true, [], { approved: true, rejections: [] }, 'none')).toEqual([])
  })

  test('the version rejection reasons are listed, temporary ones too', () => {
    expect(grabObjections(true, [], { approved: false, rejections: [{ reason: 'Not wanted in profile' }, { reason: 'Already downloading' }] }, 'none')).toEqual([
      'Not wanted in profile',
      'Already downloading',
    ])
  })

  test('a release of another title says so first, even when the version accepts it', () => {
    expect(grabObjections(false, [{ reason: 'Title does not match' }], { approved: true, rejections: [] }, 'none')).toEqual(['Title does not match'])
  })

  test('no verdict at all is an objection', () => {
    expect(grabObjections(true, [], undefined, 'no verdict')).toEqual(['no verdict'])
  })
})

describe('comparators', () => {
  const q = (quality: string, resolution: number, revisionVersion = 1) => ({ quality, resolution, revisionVersion })

  test('quality sorts by resolution, then revision', () => {
    const list = [q('WEBDL-1080p', 1080, 2), q('HDTV-720p', 720), q('WEBDL-2160p', 2160), q('WEBDL-1080p', 1080)]
    expect([...list].sort(qualityComparator).map((r) => `${r.quality}v${r.revisionVersion}`)).toEqual(['HDTV-720pv1', 'WEBDL-1080pv1', 'WEBDL-1080pv2', 'WEBDL-2160pv1'])
  })

  test('languages sort by name, releases without any last', () => {
    const list = [{ languages: [] }, { languages: ['French'] }, { languages: ['English', 'French'] }]
    expect([...list].sort(languagesComparator).map((r) => r.languages.join('+'))).toEqual(['English+French', 'French', ''])
  })
})
