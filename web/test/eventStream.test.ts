import { describe, expect, test } from 'bun:test'
import { openEventStream, type StreamSource } from '../src/hooks/eventStream'

class FakeSource implements StreamSource {
  readyState = 0
  onopen: ((event: Event) => void) | null = null
  onerror: ((event: Event) => void) | null = null
  closed = false
  listeners = new Map<string, (event: MessageEvent<string>) => void>()

  addEventListener(type: string, listener: (event: MessageEvent<string>) => void) {
    this.listeners.set(type, listener)
  }

  close() {
    this.closed = true
    this.readyState = 2
  }

  open() {
    this.readyState = 1
    this.onopen?.(new Event('open'))
  }

  fail(readyState: number) {
    this.readyState = readyState
    this.onerror?.(new Event('error'))
  }
}

function setup() {
  const sources: FakeSource[] = []
  const timers: { run: () => void; ms: number; cleared: boolean }[] = []
  const events: [string, string][] = []
  let reconnects = 0
  const stop = openEventStream({
    create: () => {
      const source = new FakeSource()
      sources.push(source)
      return source
    },
    types: ['grabbed', 'imported'],
    onEvent: (type, data) => events.push([type, data]),
    onReconnect: () => {
      reconnects++
    },
    setTimer: (run, ms) => {
      const timer = { run, ms, cleared: false }
      timers.push(timer)
      return timer
    },
    clearTimer: (timer) => {
      ;(timer as { cleared: boolean }).cleared = true
    },
  })
  const runLastTimer = () => {
    const timer = timers[timers.length - 1]
    if (timer && !timer.cleared) timer.run()
  }
  return { sources, timers, events, reconnects: () => reconnects, stop, runLastTimer }
}

describe('openEventStream', () => {
  test('the first open does not ask for a refresh', () => {
    const { sources, reconnects } = setup()
    sources[0]?.open()
    expect(reconnects()).toBe(0)
  })

  test('an event of a listed type reaches onEvent with its type and data', () => {
    const { sources, events } = setup()
    sources[0]?.listeners.get('imported')?.({ data: '{"movie":{"id":3}}' } as MessageEvent<string>)
    expect(events).toEqual([['imported', '{"movie":{"id":3}}']])
  })

  test('a hard failure reopens the stream after 5 s and the next open asks for a refresh', () => {
    const { sources, timers, reconnects, runLastTimer } = setup()
    sources[0]?.open()
    sources[0]?.fail(2)
    expect(sources[0]?.closed).toBe(true)
    expect(timers.map((t) => t.ms)).toEqual([5_000])
    expect(sources).toHaveLength(1)

    runLastTimer()
    expect(sources).toHaveLength(2)
    sources[1]?.open()
    expect(reconnects()).toBe(1)
    sources[1]?.open()
    expect(reconnects()).toBe(1)
  })

  test('hard failures wait 5, 10, 30 then 30 s again, and an open resets the wait', () => {
    const { sources, timers, runLastTimer } = setup()
    for (let i = 0; i < 4; i++) {
      sources[i]?.fail(2)
      runLastTimer()
    }
    expect(timers.map((t) => t.ms)).toEqual([5_000, 10_000, 30_000, 30_000])

    sources[4]?.open()
    sources[4]?.fail(2)
    expect(timers.map((t) => t.ms).at(-1)).toBe(5_000)
  })

  test('a soft failure leaves the browser to retry, and the next open asks for a refresh', () => {
    const { sources, timers, reconnects } = setup()
    sources[0]?.open()
    sources[0]?.fail(0)
    expect(sources).toHaveLength(1)
    expect(timers).toHaveLength(0)
    expect(sources[0]?.closed).toBe(false)

    sources[0]?.open()
    expect(reconnects()).toBe(1)
  })

  test('closing cancels a pending retry and closes the source', () => {
    const { sources, timers, stop } = setup()
    sources[0]?.fail(2)
    stop()
    expect(timers[0]?.cleared).toBe(true)

    timers[0]?.run()
    expect(sources).toHaveLength(1)
    expect(sources[0]?.closed).toBe(true)
  })

  test('closing an open stream closes its source', () => {
    const { sources, stop } = setup()
    sources[0]?.open()
    stop()
    expect(sources[0]?.closed).toBe(true)
  })
})
