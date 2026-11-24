/**
 * Reads ffprobe's JSON (`-show_format -show_streams`) into what the probe dialog shows per stream. Anything ffprobe left
 * out stays null; the raw JSON tab shows the rest.
 */

export type ProbeStreamKind = 'video' | 'audio' | 'subtitle' | 'other'

export interface ProbeStream {
  index: number
  kind: ProbeStreamKind
  /** ffprobe's codec_type as it came ("attachment", "data"…), for the streams of kind other. */
  type: string | null
  codec: string | null
  codecLong: string | null
  profile: string | null
  language: string | null
  title: string | null
  isDefault: boolean
  isForced: boolean
  isHearingImpaired: boolean
  /** Bits per second: the stream's bit_rate, else the BPS tag Matroska muxers write. */
  bitrate: number | null
  width: number | null
  height: number | null
  bitDepth: number | null
  pixelFormat: string | null
  frameRate: number | null
  /** "PQ", "HLG": what the transfer characteristics say. */
  transfer: string | null
  colorPrimaries: string | null
  /** The side data ffprobe found: "DOVI configuration record", "Mastering display metadata"… */
  sideData: string[]
  /** Dolby Vision profile when a DOVI record is there. */
  doviProfile: number | null
  channels: number | null
  channelLayout: string | null
  sampleRate: number | null
}

export interface ProbeFormat {
  name: string | null
  longName: string | null
  durationSeconds: number | null
  size: number | null
  bitrate: number | null
  title: string | null
}

type Json = Record<string, unknown>

const isObject = (value: unknown): value is Json => typeof value === 'object' && value !== null && !Array.isArray(value)

const str = (value: unknown): string | null => (typeof value === 'string' && value.trim() !== '' ? value : null)

/** ffprobe writes many numbers as strings ("48000", "7200.000"). */
function num(value: unknown): number | null {
  if (typeof value === 'number') return Number.isFinite(value) ? value : null
  if (typeof value === 'string' && value.trim() !== '') {
    const parsed = Number(value)
    return Number.isFinite(parsed) ? parsed : null
  }
  return null
}

/** Tags are case-insensitive in practice (Matroska "language", MP4 "LANGUAGE", "BPS-eng"). */
function tag(tags: Json, ...names: string[]): unknown {
  for (const name of names) {
    const key = Object.keys(tags).find((k) => k.toLowerCase() === name.toLowerCase())
    if (key) return tags[key]
  }
  return undefined
}

/** "24000/1001" → 23.976; "0/0" → null. */
function rate(value: unknown): number | null {
  const text = str(value)
  if (!text) return null
  const [a, b] = text.split('/').map(Number)
  if (!b || !Number.isFinite(a) || !Number.isFinite(b)) return null
  return Math.round((a / b) * 1000) / 1000
}

/** bits_per_raw_sample when given, else read from the pixel format ("yuv420p10le" → 10). */
function bitDepth(stream: Json): number | null {
  const raw = num(stream.bits_per_raw_sample)
  if (raw) return raw
  const format = str(stream.pix_fmt)
  if (!format) return null
  const match = /p(\d{2})(?:le|be)?$/.exec(format)
  return match ? Number(match[1]) : 8
}

const transfers: Record<string, string> = { smpte2084: 'PQ', 'arib-std-b67': 'HLG' }

function kindOf(type: string | null): ProbeStreamKind {
  return type === 'video' || type === 'audio' || type === 'subtitle' ? type : 'other'
}

export function probeStreams(probe: unknown): ProbeStream[] {
  if (!isObject(probe) || !Array.isArray(probe.streams)) return []
  return probe.streams.filter(isObject).map((stream, position) => {
    const tags = isObject(stream.tags) ? stream.tags : {}
    const disposition = isObject(stream.disposition) ? stream.disposition : {}
    const sideData = Array.isArray(stream.side_data_list) ? stream.side_data_list.filter(isObject) : []
    const dovi = sideData.find((s) => str(s.side_data_type)?.startsWith('DOVI'))
    const type = str(stream.codec_type)
    const kind = kindOf(type)
    const transfer = str(stream.color_transfer)
    return {
      index: num(stream.index) ?? position,
      kind,
      type,
      codec: str(stream.codec_name),
      codecLong: str(stream.codec_long_name),
      profile: str(stream.profile),
      language: str(tag(tags, 'language')),
      title: str(tag(tags, 'title')),
      isDefault: disposition.default === 1,
      isForced: disposition.forced === 1,
      isHearingImpaired: disposition.hearing_impaired === 1,
      bitrate: num(stream.bit_rate) ?? num(tag(tags, 'BPS', 'BPS-eng')),
      width: num(stream.width),
      height: num(stream.height),
      bitDepth: kind === 'video' ? bitDepth(stream) : null,
      pixelFormat: str(stream.pix_fmt),
      frameRate: kind === 'video' ? (rate(stream.avg_frame_rate) ?? rate(stream.r_frame_rate)) : null,
      transfer: transfer ? (transfers[transfer] ?? transfer) : null,
      colorPrimaries: str(stream.color_primaries),
      sideData: sideData.map((s) => str(s.side_data_type)).filter((s): s is string => s !== null),
      doviProfile: dovi ? num(dovi.dv_profile) : null,
      channels: num(stream.channels),
      channelLayout: str(stream.channel_layout),
      sampleRate: num(stream.sample_rate),
    }
  })
}

export function probeFormat(probe: unknown): ProbeFormat | null {
  if (!isObject(probe) || !isObject(probe.format)) return null
  const format = probe.format
  const tags = isObject(format.tags) ? format.tags : {}
  return {
    name: str(format.format_name),
    longName: str(format.format_long_name),
    durationSeconds: num(format.duration),
    size: num(format.size),
    bitrate: num(format.bit_rate),
    title: str(tag(tags, 'title')),
  }
}

/** 60000000 → "60 Mb/s", 640000 → "640 kb/s". */
export function formatBitrate(bitsPerSecond: number): string {
  if (bitsPerSecond >= 1_000_000) return `${(bitsPerSecond / 1_000_000).toFixed(bitsPerSecond >= 10_000_000 ? 0 : 1)} Mb/s`
  return `${Math.round(bitsPerSecond / 1000)} kb/s`
}

/** 7265.4 → "2:01:05". */
export function formatDuration(seconds: number): string {
  const total = Math.round(seconds)
  const h = Math.floor(total / 3600)
  const m = Math.floor((total % 3600) / 60)
  const s = total % 60
  const pad = (n: number) => String(n).padStart(2, '0')
  return h > 0 ? `${h}:${pad(m)}:${pad(s)}` : `${m}:${pad(s)}`
}
