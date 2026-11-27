import { useQuery } from '@tanstack/react-query'
import { shadow } from '@/api/client'

/**
 * Which kinds are still in shadow mode, and whether an enabled follow is left behind for a kind that grabs for real.
 * The comparison pages only make sense in shadow mode; a followed app stays reachable while it is enabled, since the
 * health check asks to disable it there. Everything reads false until the status has loaded, or when it cannot.
 */
export function useShadowMode() {
  const status = useQuery({ queryKey: ['shadow'], queryFn: shadow.status, staleTime: 60_000 })
  const data = status.data
  return {
    films: data?.films ?? false,
    series: data?.series ?? false,
    radarrFollowed: (data?.radarrs ?? []).some((r) => r.enabled),
    sonarrFollowed: (data?.sonarrs ?? []).some((s) => s.enabled),
  }
}
