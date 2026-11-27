import { describe, expect, test } from 'bun:test'
import type { HistoryEvent } from '../src/api/client'
import { foldGrabs } from '../src/features/series/seasonHistory'

let id = 0
const line = (eventType: HistoryEvent['eventType'], episode: string, data: Record<string, string> = {}, extra: Partial<HistoryEvent> = {}): HistoryEvent =>
  ({
    id: ++id,
    eventType,
    versionName: 'Standard',
    sourceTitle: 'Show.S01.1080p.WEB-DL-GRP',
    createdAt: '2026-11-20T10:00:00Z',
    data: { episode, downloadId: 'abc', ...data },
    ...extra,
  }) as HistoryEvent

describe('season history', () => {
  test('folds the lines of one grab into one, listing its episodes', () => {
    const folded = foldGrabs([line('grabbed', 'S01E02'), line('grabbed', 'S01E01'), line('grabbed', 'S01E03')])
    expect(folded).toHaveLength(1)
    expect(folded[0].data.episode).toBe('S01E01, S01E02, S01E03')
  })

  test('keeps two grabs of the same release apart when they happened at different moments or for different versions', () => {
    const folded = foldGrabs([line('grabbed', 'S01E01'), line('grabbed', 'S01E01', {}, { createdAt: '2026-11-21T10:00:00Z' }), line('grabbed', 'S01E01', {}, { versionName: '4K' })])
    expect(folded).toHaveLength(3)
  })

  test('keeps every other event one row per episode, in order', () => {
    const events = [line('imported', 'S01E01', { path: '/tv/a' }), line('grabbed', 'S01E01'), line('imported', 'S01E02', { path: '/tv/b' }), line('grabbed', 'S01E02')]
    const folded = foldGrabs(events)
    expect(folded.map((e) => [e.eventType, e.data.episode])).toEqual([
      ['imported', 'S01E01'],
      ['grabbed', 'S01E01, S01E02'],
      ['imported', 'S01E02'],
    ])
  })

  test('leaves the events it was given untouched', () => {
    const events = [line('grabbed', 'S01E01'), line('grabbed', 'S01E02')]
    foldGrabs(events)
    expect(events[0].data.episode).toBe('S01E01')
  })
})
