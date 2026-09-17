import { useEffect } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { eventsUrl } from '@/api/client'

/** The event types the server publishes (blueprint §3.9), listened to by name. */
const eventTypes = [
  'movieAdded',
  'versionAdded',
  'movieDeleted',
  'grabbed',
  'imported',
  'upgraded',
  'downloadFailed',
  'importFailed',
  'fileDeleted',
  'defaultVersionChanged',
  'healthIssue',
  'healthRestored',
] as const

type ServerEventType = (typeof eventTypes)[number]

type Payload = { movie?: { id: number } | null }

/** Query keys to refresh per event, besides the film the event is about. */
const invalidations: Record<ServerEventType, string[][]> = {
  movieAdded: [['movies'], ['wanted']],
  versionAdded: [['movies'], ['wanted']],
  movieDeleted: [['movies'], ['wanted']],
  grabbed: [['queue'], ['history'], ['movies'], ['wanted']],
  downloadFailed: [['queue'], ['history'], ['movies'], ['wanted'], ['blocklist']],
  importFailed: [['queue'], ['history'], ['movies'], ['wanted']],
  imported: [['movies'], ['history'], ['wanted'], ['queue']],
  upgraded: [['movies'], ['history'], ['wanted'], ['queue']],
  fileDeleted: [['movies'], ['history'], ['wanted']],
  defaultVersionChanged: [['movies'], ['history']],
  healthIssue: [['health']],
  healthRestored: [['health']],
}

/**
 * One EventSource on the server's event stream for the whole app: whatever happens in the background (a grab from the
 * RSS loop, an import, a failed download) refreshes the pages that show it. The browser reconnects on its own.
 */
export function useServerEvents() {
  const queryClient = useQueryClient()

  useEffect(() => {
    // StrictMode mounts effects twice in development; the cleanup below closes the first source before the second opens.
    const source = new EventSource(eventsUrl)
    const handlers = eventTypes.map((type) => {
      const handler = (event: MessageEvent<string>) => {
        let movieId: number | undefined
        try {
          const payload = JSON.parse(event.data) as Payload
          movieId = payload.movie?.id
        } catch {
          /* not JSON: nothing to target */
        }

        for (const key of invalidations[type]) {
          void queryClient.invalidateQueries({ queryKey: key })
        }

        if (movieId !== undefined) {
          void queryClient.invalidateQueries({ queryKey: ['movie', movieId] })
          void queryClient.invalidateQueries({ queryKey: ['search', movieId] })
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
