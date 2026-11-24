import { useEffect } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { eventsUrl } from '@/api/client'
import { openEventStream } from './eventStream'
import { type EventPayload, eventTypes, keysFor, type ServerEventType } from './serverEvents'

/**
 * One EventSource on the server's event stream for the whole app: whatever happens in the background (a grab from the
 * RSS loop, an import, a failed download, a request or an issue, for films and series alike) refreshes the pages that
 * show it. The browser reconnects on its own after a network drop; after an answer that is not a 200 it gives up, and
 * the stream is reopened here. Either way the queries on screen are refreshed once it is back.
 */
export function useServerEvents() {
  const queryClient = useQueryClient()

  useEffect(() => {
    // StrictMode mounts effects twice in development; the cleanup closes the first stream before the second opens.
    return openEventStream({
      create: () => new EventSource(eventsUrl),
      types: eventTypes,
      onEvent: (type, data) => {
        let payload: EventPayload = {}
        try {
          payload = (JSON.parse(data) as EventPayload | null) ?? {}
        } catch {
          /* not JSON: nothing to target */
        }

        for (const key of keysFor(type as ServerEventType, payload)) {
          void queryClient.invalidateQueries({ queryKey: key })
        }
      },
      // Events published while the stream was down are lost: refresh what is on screen.
      onReconnect: () => void queryClient.invalidateQueries(),
    })
  }, [queryClient])
}
