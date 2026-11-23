/** The event types the server publishes, listened to by name. */
export const eventTypes = [
  'movieAdded',
  'versionAdded',
  'versionUpdated',
  'movieDeleted',
  'grabbed',
  'imported',
  'upgraded',
  'downloadFailed',
  'importFailed',
  'fileDeleted',
  'fileRenamed',
  'defaultVersionChanged',
  'healthIssue',
  'healthRestored',
  'versionDeleted',
  'seriesAdded',
  'seriesDeleted',
  'seriesVersionAdded',
  'seriesVersionDeleted',
  'seriesVersionUpdated',
  'seriesDefaultVersionChanged',
  'episodeGrabbed',
  'episodeImported',
  'episodeFileRenamed',
  'episodeFileDeleted',
  'episodeDownloadFailed',
  'episodeImportFailed',
  'requestCreated',
  'requestApproved',
  'requestDeclined',
  'requestAvailable',
  'requestFailed',
  'issueCreated',
  'issueCommented',
  'issueResolved',
  'issueReopened',
] as const

export type ServerEventType = (typeof eventTypes)[number]

/** Query keys to refresh per event, besides the film or series the event is about. */
export const invalidations: Record<ServerEventType, string[][]> = {
  movieAdded: [['movies'], ['wanted']],
  versionAdded: [['movies'], ['wanted']],
  versionUpdated: [['movies'], ['wanted']],
  movieDeleted: [['movies'], ['wanted']],
  grabbed: [['queue'], ['history'], ['movies'], ['wanted']],
  downloadFailed: [['queue'], ['history'], ['movies'], ['wanted'], ['blocklist']],
  importFailed: [['queue'], ['history'], ['movies'], ['wanted']],
  imported: [['movies'], ['history'], ['wanted'], ['queue']],
  upgraded: [['movies'], ['history'], ['wanted'], ['queue']],
  fileDeleted: [['movies'], ['history'], ['wanted']],
  fileRenamed: [['movies'], ['history']],
  defaultVersionChanged: [['movies'], ['history']],
  healthIssue: [['health']],
  healthRestored: [['health']],
  versionDeleted: [['movies'], ['wanted'], ['history']],
  seriesAdded: [['series'], ['seriesWanted'], ['calendar']],
  seriesDeleted: [['series'], ['seriesWanted'], ['calendar']],
  seriesVersionAdded: [['series'], ['seriesWanted'], ['calendar']],
  seriesVersionDeleted: [['series'], ['seriesWanted'], ['calendar']],
  seriesVersionUpdated: [['series'], ['seriesWanted'], ['calendar']],
  seriesDefaultVersionChanged: [['series'], ['seriesWanted'], ['calendar']],
  episodeGrabbed: [['queue'], ['history'], ['series'], ['seriesWanted']],
  episodeImported: [['queue'], ['history'], ['series'], ['seriesWanted'], ['calendar']],
  episodeFileRenamed: [['history']],
  episodeFileDeleted: [['history'], ['series'], ['seriesWanted']],
  episodeDownloadFailed: [['queue'], ['history'], ['seriesWanted'], ['blocklist']],
  episodeImportFailed: [['queue'], ['history'], ['seriesWanted']],
  requestCreated: [['requests']],
  requestApproved: [['requests'], ['discover']],
  requestDeclined: [['requests']],
  requestAvailable: [['requests'], ['discover']],
  requestFailed: [['requests']],
  issueCreated: [['issues']],
  issueCommented: [['issues']],
  issueResolved: [['issues']],
  issueReopened: [['issues']],
}

export type EventPayload = { movie?: { id: number } | null; series?: { id: number } | null }

/** The query keys an event refreshes: its lists, then the film or the series it is about. */
export function keysFor(type: ServerEventType, payload: EventPayload): (string | number)[][] {
  const keys: (string | number)[][] = [...invalidations[type]]
  const movieId = payload.movie?.id
  if (typeof movieId === 'number') keys.push(['movie', movieId], ['search', movieId])
  const seriesId = payload.series?.id
  if (typeof seriesId === 'number') keys.push(['seriesDetail', seriesId], ['seriesEpisodes', seriesId], ['seriesFiles', seriesId])
  return keys
}
