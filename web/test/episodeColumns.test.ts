import { describe, expect, test } from 'bun:test'
import type { Episode, EpisodeFile } from '../src/api/client'
import { audioLabel, channelLayout, defaultEpisodeColumns, episodeStatus, fileFlags, fileLanguages, knownColumns } from '../src/features/series/episodeColumns'
import { sortEpisodes } from '../src/features/series/episodeSort'

const episode = (id: number, hasAired = true) => ({ id, hasAired, title: `E${id}` }) as unknown as Episode
const file = (patch: Partial<EpisodeFile>) => ({ size: 1, qualityResolution: 1080, revisionVersion: 1, customFormatScore: 0, languages: [], indexerFlags: 'none', mediaInfo: null, ...patch }) as EpisodeFile

describe('knownColumns', () => {
  test('keeps the known columns in the declared order', () => {
    expect(knownColumns(['size', 'gone', 'airDate'])).toEqual(['airDate', 'size'])
  })

  test('falls back to the defaults on anything but a list', () => {
    expect(knownColumns(undefined)).toEqual(defaultEpisodeColumns)
    expect(knownColumns('size')).toEqual(defaultEpisodeColumns)
  })
})

describe('episodeStatus', () => {
  test('like Sonarr: downloaded, missing, unmonitored, unaired', () => {
    expect(episodeStatus(episode(1), true, file({}))).toBe('downloaded')
    expect(episodeStatus(episode(1), true, undefined)).toBe('missing')
    expect(episodeStatus(episode(1), false, undefined)).toBe('unmonitored')
    expect(episodeStatus(episode(1, false), true, undefined)).toBe('unaired')
  })
})

describe('media labels', () => {
  test('channel layouts', () => {
    expect(channelLayout(5.1)).toBe('5.1')
    expect(channelLayout(7.1)).toBe('7.1')
    expect(channelLayout(2)).toBe('2.0')
    expect(channelLayout(null)).toBeNull()
  })

  test('audio is codec and layout, nothing without a probe', () => {
    expect(audioLabel(file({ mediaInfo: { audioCodec: 'EAC3', audioChannels: 5.1 } as EpisodeFile['mediaInfo'] }))).toBe('EAC3 5.1')
    expect(audioLabel(file({}))).toBeNull()
  })

  test('unknown language and no flags say nothing', () => {
    expect(fileLanguages(file({ languages: ['Unknown'] }))).toEqual([])
    expect(fileLanguages(file({ languages: ['French', 'English'] }))).toEqual(['French', 'English'])
    expect(fileFlags(file({}))).toEqual([])
    expect(fileFlags(file({ indexerFlags: 'freeleech, internal' } as Partial<EpisodeFile>))).toEqual(['freeleech', 'internal'])
  })
})

describe('sorting on the new columns', () => {
  const episodes = [episode(1), episode(2), episode(3), episode(4, false)]
  const files: Record<number, EpisodeFile> = {
    1: file({ qualityResolution: 720, customFormatScore: 10, customFormats: ['A'] }),
    2: file({ qualityResolution: 2160, customFormatScore: -5, customFormats: ['B'] }),
    3: file({ qualityResolution: 1080, revisionVersion: 2, customFormatScore: 30, customFormats: ['C'] }),
  }
  const ctx = (e: Episode) => ({ file: files[e.id], monitored: true })
  const ids = (list: Episode[]) => list.map((e) => e.id)

  test('quality by resolution, best first, no file last', () => {
    expect(ids(sortEpisodes(episodes, { key: 'quality', direction: 'desc' }, ctx))).toEqual([2, 3, 1, 4])
  })

  test('score, best first', () => {
    expect(ids(sortEpisodes(episodes, { key: 'score', direction: 'desc' }, ctx))).toEqual([3, 1, 2, 4])
  })

  test('status puts what is missing first', () => {
    const list = [episode(1), episode(5), episode(4, false)]
    expect(ids(sortEpisodes(list, { key: 'status', direction: 'asc' }, ctx))).toEqual([5, 4, 1])
  })
})
