import { useQuery } from '@tanstack/react-query'
import { system } from '@/api/client'

/**
 * Bigarr__Scheduler__Enabled. When false no task runs on its own (RSS sync, searches, health check, maintenance,
 * backups), so a page must not promise a cadence. Undefined until the status is loaded; the shell already holds it.
 */
export function useSchedulerEnabled(): boolean | undefined {
  const status = useQuery({ queryKey: ['system', 'status'], queryFn: system.status, staleTime: 5 * 60_000 })
  return status.data?.schedulerEnabled
}
