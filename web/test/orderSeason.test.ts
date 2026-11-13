import { describe, expect, test } from 'bun:test'
import type { Episode, EpisodeFile, Series } from '../src/api/client'
import { orderSeason } from '../src/features/series/orderSeason'

const series = { versions: [{ id: 1 }, { id: 2 }] } as unknown as Series
const episode = (id: number, file1: number | null, file2: number | null = null) =>
  ({
    id,
    hasAired: true,
    versions: [
      { versionId: 1, targetId: id, monitored: true, hasFile: file1 !== null, episodeFileId: file1 },
      { versionId: 2, targetId: 100 + id, monitored: false, hasFile: file2 !== null, episodeFileId: file2 },
    ],
  }) as unknown as Episode
const file = (id: number, size: number) => ({ id, size }) as EpisodeFile

describe('orderSeason', () => {
  test('sums the distinct files of its episodes per version', () => {
    // Episodes 1 and 2 share a double-episode file; episode 3 has none on version 1.
    const season = orderSeason(series, 2, [episode(1, 10, 20), episode(2, 10), episode(3, null)], [file(10, 3_000), file(20, 500), file(30, 7_000)])
    const [v1, v2] = season.versions
    expect(v1?.statistics).toEqual({ episodeCount: 3, episodeFileCount: 2, monitoredEpisodeCount: 3, totalEpisodeCount: 3, sizeOnDisk: 3_000 })
    expect(v2?.statistics.sizeOnDisk).toBe(500)
  })

  test('a version whose files are not loaded counts 0', () => {
    const season = orderSeason(series, 1, [episode(1, 10, 20)], [file(10, 3_000)])
    expect(season.versions.map((v) => v.statistics.sizeOnDisk)).toEqual([3_000, 0])
  })
})
