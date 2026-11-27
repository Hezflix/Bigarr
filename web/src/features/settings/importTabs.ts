import type { RadarrImportState } from '../../api/client'

/** The sources of Settings → Import, in tab order; also the `?tab=` values. */
export const importSources = ['radarr', 'sonarr', 'tautulli', 'seerr'] as const

export type ImportSource = (typeof importSources)[number]

export const isImportSource = (value: unknown): value is ImportSource => importSources.includes(value as ImportSource)

/** What a tab's icon says by its colour: never run, running (or queued), done, failed. */
export type ImportTone = 'idle' | 'running' | 'done' | 'failed'

/** The tone of an import's last run; unknown (still loading, or unreadable) reads as never run. */
export function importTone(state: RadarrImportState | undefined): ImportTone {
  switch (state) {
    case 'queued':
    case 'running':
      return 'running'
    case 'completed':
      return 'done'
    case 'failed':
      return 'failed'
    default:
      return 'idle'
  }
}
