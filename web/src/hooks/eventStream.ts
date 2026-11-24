/** What the stream needs from an EventSource: enough to be faked in a test. */
export interface StreamSource {
  readyState: number
  onopen: ((event: Event) => void) | null
  onerror: ((event: Event) => void) | null
  addEventListener(type: string, listener: (event: MessageEvent<string>) => void): void
  close(): void
}

export interface EventStreamOptions {
  create: () => StreamSource
  types: readonly string[]
  onEvent: (type: string, data: string) => void
  /** Called when the stream is open again after an interruption: events may have been missed. */
  onReconnect: () => void
  /** Waits before reopening after the browser gave up; the last one repeats. */
  retryDelaysMs?: readonly number[]
  setTimer?: (run: () => void, ms: number) => unknown
  clearTimer?: (timer: unknown) => void
}

export const defaultRetryDelaysMs = [5_000, 10_000, 30_000] as const

/** `EventSource.readyState` once the browser has given up on the stream. */
const closedState = 2

/** Opens the stream and keeps it open. Returns the function that closes it for good. */
export function openEventStream(options: EventStreamOptions): () => void {
  const {
    create,
    types,
    onEvent,
    onReconnect,
    retryDelaysMs = defaultRetryDelaysMs,
    setTimer = (run, ms) => setTimeout(run, ms),
    clearTimer = (timer) => clearTimeout(timer as ReturnType<typeof setTimeout>),
  } = options

  let source: StreamSource | null = null
  let timer: unknown = null
  let closed = false
  let interrupted = false
  let attempt = 0

  const connect = () => {
    timer = null
    if (closed) return
    const current = create()
    source = current
    for (const type of types) current.addEventListener(type, (event) => onEvent(type, event.data))

    current.onopen = () => {
      if (interrupted) onReconnect()
      interrupted = false
      attempt = 0
    }

    current.onerror = () => {
      interrupted = true
      // readyState 0: the browser is reconnecting by itself. 2: it gave up, so reopen here.
      if (current.readyState !== closedState || closed) return
      current.close()
      const wait = retryDelaysMs[Math.min(attempt, retryDelaysMs.length - 1)] ?? defaultRetryDelaysMs[0]
      attempt++
      timer = setTimer(connect, wait)
    }
  }

  connect()

  return () => {
    closed = true
    if (timer !== null) clearTimer(timer)
    timer = null
    source?.close()
  }
}
