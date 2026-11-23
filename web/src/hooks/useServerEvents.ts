import { useEffect } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { eventsUrl } from '@/api/client'
import { type EventPayload, eventTypes, keysFor } from './serverEvents'

/**
 * One EventSource on the server's event stream for the whole app: whatever happens in the background (a grab from the
 * RSS loop, an import, a failed download, a request or an issue, for films and series alike) refreshes the pages that
 * show it. The browser reconnects on its own.
 */
export function useServerEvents() {
  const queryClient = useQueryClient()

  useEffect(() => {
    // StrictMode mounts effects twice in development; the cleanup below closes the first source before the second opens.
    const source = new EventSource(eventsUrl)
    const handlers = eventTypes.map((type) => {
      const handler = (event: MessageEvent<string>) => {
        let payload: EventPayload = {}
        try {
          payload = (JSON.parse(event.data) as EventPayload | null) ?? {}
        } catch {
          /* not JSON: nothing to target */
        }

        for (const key of keysFor(type, payload)) {
          void queryClient.invalidateQueries({ queryKey: key })
        }
      }
      source.addEventListener(type, handler)
      return [type, handler] as const
    })

    return () => {
      for (const [type, handler] of handlers) source.removeEventListener(type, handler)
      source.close()
    }
  }, [queryClient])
}
