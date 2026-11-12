import { useEffect, useState } from 'react'
import type { PlaybackSession } from '@/api/client'

/** A gap bigger than this between the local clock and the server is a seek: the position jumps to it. */
export const snapGapMs = 15_000

/** The shortest a small gap takes to close: the position never runs at less than half speed or more than double. */
const minEaseMs = 3_000

/**
 * Where a stream was at `at` (a `performance.now()` time) and how it moves from there: on at real speed while playing,
 * plus a correction that fades out over `easeMs` when the server's value was close to the local one.
 */
export type Anchor = { offsetMs: number; at: number; moving: boolean; durationMs: number | null; correctionMs: number; easeMs: number; snapped: boolean }

export function positionAt(anchor: Anchor, now: number): number {
  const elapsed = anchor.moving ? Math.max(0, now - anchor.at) : 0
  const fade = anchor.correctionMs === 0 ? 0 : anchor.correctionMs * Math.max(0, 1 - Math.max(0, now - anchor.at) / anchor.easeMs)
  const position = Math.max(0, anchor.offsetMs + elapsed + fade)
  return anchor.durationMs ? Math.min(anchor.durationMs, position) : position
}

/**
 * The anchor for a fresh server value, received at `now`. Playing, the server's value is brought up to now with its age.
 * Against the position shown so far: a big gap (a seek, a restart) or a pause snaps; a small one eases in, so the
 * position neither jumps nor runs backwards.
 */
export function nextAnchor(previous: Anchor | undefined, session: Pick<PlaybackSession, 'state' | 'viewOffsetMs' | 'positionAgeMs' | 'item'>, now: number): Anchor {
  const moving = session.state === 'playing'
  const offsetMs = session.viewOffsetMs + (moving ? session.positionAgeMs : 0)
  const base = { offsetMs, at: now, moving, durationMs: session.item.durationMs }
  if (!previous || !moving) return { ...base, correctionMs: 0, easeMs: minEaseMs, snapped: !previous || positionAt(previous, now) !== offsetMs }
  const gap = positionAt(previous, now) - offsetMs
  if (Math.abs(gap) > snapGapMs) return { ...base, correctionMs: 0, easeMs: minEaseMs, snapped: true }
  return { ...base, correctionMs: gap, easeMs: Math.max(minEaseMs, 2 * Math.abs(gap)), snapped: false }
}

/**
 * The streams' positions moved on locally every second between the page's refreshes, and corrected at each refresh:
 * no request of its own. `positionOf` falls back to the server's value for a stream not anchored yet.
 */
export function useLivePositions(sessions: PlaybackSession[] | undefined, receivedAt: number) {
  const [anchors, setAnchors] = useState<Map<number, Anchor>>(() => new Map())
  const [now, setNow] = useState(() => performance.now())

  // biome-ignore lint/correctness/useExhaustiveDependencies: receivedAt is the trigger: it tells a new server answer from a re-render
  useEffect(() => {
    if (!sessions) return
    const at = performance.now()
    setAnchors((previous) => new Map(sessions.map((s) => [s.id, nextAnchor(previous.get(s.id), s, at)])))
    setNow(at)
    // A new answer from the server is what moves the anchors; its time stamp tells one apart from a re-render.
  }, [sessions, receivedAt])

  const anyMoving = sessions?.some((s) => s.state === 'playing') ?? false
  useEffect(() => {
    if (!anyMoving) return
    const timer = window.setInterval(() => setNow(performance.now()), 1000)
    return () => window.clearInterval(timer)
  }, [anyMoving])

  return {
    positionOf: (session: PlaybackSession) => {
      const anchor = anchors.get(session.id)
      return anchor ? positionAt(anchor, now) : session.viewOffsetMs
    },
    /** The bar moves on smoothly unless the position just jumped or the stream stands still. */
    glides: (session: PlaybackSession) => {
      const anchor = anchors.get(session.id)
      return !!anchor?.moving && !(anchor.snapped && now - anchor.at < 1_000)
    },
  }
}
