import { describe, expect, test } from 'bun:test'
import { formatBitrate, formatDuration, probeFormat, probeStreams } from '../src/features/library/probeStreams'

const probe = {
  streams: [
    {
      index: 0,
      codec_name: 'hevc',
      codec_long_name: 'H.265 / HEVC (High Efficiency Video Coding)',
      codec_type: 'video',
      profile: 'Main 10',
      width: 3840,
      height: 2160,
      pix_fmt: 'yuv420p10le',
      color_transfer: 'smpte2084',
      color_primaries: 'bt2020',
      avg_frame_rate: '24000/1001',
      side_data_list: [{ side_data_type: 'DOVI configuration record', dv_profile: 8 }, { side_data_type: 'Mastering display metadata' }],
      disposition: { default: 1, forced: 0 },
      tags: { 'BPS-eng': '58000000', language: 'und' },
    },
    { index: 1, codec_name: 'truehd', codec_type: 'audio', channels: 8, channel_layout: '7.1', sample_rate: '48000', tags: { LANGUAGE: 'eng', title: 'TrueHD Atmos 7.1' }, disposition: { default: 1 } },
    { index: 2, codec_name: 'subrip', codec_type: 'subtitle', tags: { language: 'fre' }, disposition: { default: 0, forced: 1, hearing_impaired: 0 } },
    { index: 3, codec_name: 'ttf', codec_type: 'attachment', tags: { filename: 'font.ttf' } },
  ],
  format: { format_name: 'matroska,webm', format_long_name: 'Matroska / WebM', duration: '7265.4', size: '53687091200', bit_rate: '59100000', tags: { title: 'Demo Film' } },
}

describe('probeStreams', () => {
  test('a video stream gives its resolution, depth, frame rate and HDR', () => {
    const [video] = probeStreams(probe)
    expect(video).toMatchObject({
      kind: 'video',
      codec: 'hevc',
      profile: 'Main 10',
      width: 3840,
      height: 2160,
      bitDepth: 10,
      frameRate: 23.976,
      transfer: 'PQ',
      doviProfile: 8,
      sideData: ['DOVI configuration record', 'Mastering display metadata'],
      bitrate: 58000000,
      isDefault: true,
      isForced: false,
    })
  })

  test('audio and subtitle streams keep language, title and flags whatever the tag case', () => {
    const [, audio, subtitle, other] = probeStreams(probe)
    expect(audio).toMatchObject({ kind: 'audio', language: 'eng', title: 'TrueHD Atmos 7.1', channels: 8, channelLayout: '7.1', sampleRate: 48000, bitDepth: null, frameRate: null })
    expect(subtitle).toMatchObject({ kind: 'subtitle', language: 'fre', isForced: true, isDefault: false })
    expect(other).toMatchObject({ kind: 'other', type: 'attachment' })
  })

  test('8-bit video without bits_per_raw_sample reads as 8, and anything not an ffprobe answer gives nothing', () => {
    expect(probeStreams({ streams: [{ codec_type: 'video', pix_fmt: 'yuv420p' }] })[0].bitDepth).toBe(8)
    expect(probeStreams(null)).toEqual([])
    expect(probeStreams({ streams: 'nope' })).toEqual([])
    expect(probeFormat({})).toBeNull()
  })
})

describe('probeFormat', () => {
  test('numbers written as strings are read', () => {
    expect(probeFormat(probe)).toEqual({ name: 'matroska,webm', longName: 'Matroska / WebM', durationSeconds: 7265.4, size: 53687091200, bitrate: 59100000, title: 'Demo Film' })
  })
})

test('bitrates and durations read as people say them', () => {
  expect(formatBitrate(59_100_000)).toBe('59 Mb/s')
  expect(formatBitrate(4_500_000)).toBe('4.5 Mb/s')
  expect(formatBitrate(640_000)).toBe('640 kb/s')
  expect(formatDuration(7265.4)).toBe('2:01:05')
  expect(formatDuration(1325)).toBe('22:05')
})
