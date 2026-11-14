import { describe, expect, test } from 'bun:test'
import { versionFits } from '../src/features/search/rejectionReasons'

const decision = (versionId: number, approved: boolean, rejections: { reason: string; temporary: boolean }[] = []) => ({
  versionId,
  versionName: `v${versionId}`,
  customFormatScore: 0,
  approved,
  rejections,
})

describe('versionFits', () => {
  test('an accepting version has no reason', () => {
    expect(versionFits([decision(1, true)], [], 'Release')).toEqual([{ versionId: 1, versionName: 'v1', score: 0, status: 'accepted', reasons: [] }])
  })

  test('only temporary reasons wait, any permanent one rejects', () => {
    const [waiting, rejected] = versionFits(
      [decision(1, false, [{ reason: 'Already downloading', temporary: true }]), decision(2, false, [{ reason: 'Already downloading', temporary: true }, { reason: 'Not wanted in profile', temporary: false }])],
      [],
    )
    expect(waiting?.status).toBe('temporary')
    expect(rejected?.status).toBe('rejected')
  })

  test("the release's own reasons come first for every version, even one that accepts it", () => {
    const fits = versionFits([decision(1, true), decision(2, false, [{ reason: 'Size too large', temporary: false }])], ['Not this film: title'], 'Release')
    expect(fits.map((f) => f.reasons.map((r) => r.reason))).toEqual([['Not this film: title'], ['Not this film: title', 'Size too large']])
    expect(fits.every((f) => f.status === 'rejected')).toBe(true)
  })
})

test('a release no version judged keeps its own reasons on one line', () => {
  expect(versionFits([], ['Release title could not be parsed'], 'Release')).toEqual([
    { versionId: -1, versionName: 'Release', score: 0, status: 'rejected', reasons: [{ reason: 'Release title could not be parsed', temporary: false }] },
  ])
  expect(versionFits([], [], 'Release')).toEqual([])
})
