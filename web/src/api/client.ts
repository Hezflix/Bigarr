export type Problem = { title?: string; detail?: string; status?: number; errors?: Record<string, string[]> }

export class ApiError extends Error {
  constructor(
    public readonly status: number,
    message: string,
    public readonly problem?: Problem,
  ) {
    super(message)
  }
}

/** Thin fetch wrapper for the v1 API. The session cookie rides along automatically. */
export async function api<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, {
    ...init,
    credentials: 'same-origin',
    headers: { 'Content-Type': 'application/json', ...init?.headers },
  })

  if (!response.ok) {
    let problem: Problem | undefined
    let message = response.statusText
    try {
      problem = (await response.json()) as Problem
      message = problem.detail ?? problem.title ?? message
      if (problem.errors) message = Object.values(problem.errors).flat().join(' ')
    } catch {
      /* not a problem document */
    }
    throw new ApiError(response.status, message || `HTTP ${response.status}`, problem)
  }

  const isJson = response.headers.get('content-type')?.includes('application/json') ?? false
  if (response.status === 204 || !isJson) {
    return undefined as T
  }

  return (await response.json()) as T
}

const post = <T>(path: string, body?: unknown) => api<T>(path, { method: 'POST', body: body === undefined ? undefined : JSON.stringify(body) })
const put = <T>(path: string, body: unknown) => api<T>(path, { method: 'PUT', body: JSON.stringify(body) })
const del = (path: string) => api<void>(path, { method: 'DELETE' })

// ---- auth / system ---------------------------------------------------------

export type Me = { id: number; username: string; role: string; authKind: 'Session' | 'ApiKey'; compatView: string | null }

export type SystemStatus = {
  appName: string
  version: string
  environmentName: string
  startedAt: string
  runtimeVersion: string
  os: string
  isDocker: boolean
  configDirectory: string
  metadataProvider: 'tmdb' | 'fixtures'
}

export const auth = {
  me: () => api<Me>('/api/v1/auth/me'),
  login: (username: string, password: string) => post<void>('/api/v1/auth/login', { username, password, rememberMe: true }),
  logout: () => post<void>('/api/v1/auth/logout'),
}

export const system = {
  status: () => api<SystemStatus>('/api/v1/system/status'),
}

// ---- movies ----------------------------------------------------------------

export type VersionMode = 'managed' | 'oneShot'
export type MinimumAvailability = 'announced' | 'inCinemas' | 'released'
export type VersionStatus = 'pending' | 'missing' | 'downloading' | 'available'
export type MovieStatus = 'tba' | 'announced' | 'inCinemas' | 'released' | 'deleted'

export type Constraints = {
  edition: string | null
  requiredCustomFormatIds: number[] | null
  forbiddenCustomFormatIds: number[] | null
  releaseGroup: string | null
}

/** What ffprobe read from the file; null until the import or the backfill probed it. */
export type MediaInfo = {
  containerFormat: string | null
  videoCodec: string | null
  videoBitrate: number | null
  videoBitDepth: number | null
  videoDynamicRange: string | null
  videoDynamicRangeType: string | null
  width: number | null
  height: number | null
  scanType: string | null
  audioCodec: string | null
  audioBitrate: number | null
  audioChannels: number | null
  audioLanguages: string[]
  subtitles: string[]
  runTime: string | null
}

export type MediaFile = {
  id: number
  relativePath: string
  path: string
  size: number
  dateAdded: string
  qualityId: number
  quality: string
  revisionVersion: number
  isRepack: boolean
  edition: string | null
  releaseGroup: string | null
  languages: string[]
  originalReleaseTitle: string | null
  hasMediaInfo: boolean
  mediaInfo: MediaInfo | null
}

export type Version = {
  id: number
  name: string
  mode: VersionMode
  qualityProfileId: number
  qualityProfile: string
  isDefault: boolean
  monitored: boolean
  rootFolderId: number
  rootFolderPath: string
  folderName: string
  path: string
  minimumAvailability: MinimumAvailability
  constraints: Constraints | null
  status: VersionStatus
  isAvailable: boolean
  file: MediaFile | null
  createdAt: string
  /** Custom formats the file matches, judged at read time; null without a file. */
  customFormats: string[] | null
  customFormatScore: number | null
  /** Quality at the profile cutoff and score at the cutoff format score: nothing more is wanted. */
  cutoffMet: boolean | null
}

export type Movie = {
  id: number
  tmdbId: number
  imdbId: string | null
  title: string
  originalTitle: string | null
  sortTitle: string
  year: number | null
  runtime: number | null
  overview: string | null
  certification: string | null
  originalLanguage: string | null
  genres: string[]
  ratings: { tmdb?: { value: number; votes: number } | null; imdb?: { value: number; votes: number } | null } | null
  alternativeTitles: { title: string; language: string | null }[]
  tmdbStatus: MovieStatus
  effectiveStatus: MovieStatus
  inCinemasDate: string | null
  digitalReleaseDate: string | null
  physicalReleaseDate: string | null
  collectionTmdbId: number | null
  posterUrl: string | null
  fanartUrl: string | null
  defaultRootFolderId: number
  addedAt: string
  lastMetadataRefreshAt: string | null
  monitored: boolean
  versions: Version[]
}

export type VersionSummary = {
  id: number
  name: string
  mode: VersionMode
  isDefault: boolean
  monitored: boolean
  qualityProfileId: number
  status: VersionStatus
  quality: string | null
}

export type MovieSummary = {
  id: number
  tmdbId: number
  title: string
  sortTitle: string
  year: number | null
  posterUrl: string | null
  effectiveStatus: MovieStatus
  monitored: boolean
  versions: VersionSummary[]
}

export type MovieLookupResult = {
  tmdbId: number
  title: string
  originalTitle: string | null
  year: number | null
  overview: string | null
  posterUrl: string | null
  status: MovieStatus
  existingMovieId: number | null
  excluded: boolean
}

export type AddMovieRequest = {
  tmdbId: number
  qualityProfileId: number
  rootFolderId: number
  versionName?: string | null
  monitored?: boolean
  minimumAvailability?: MinimumAvailability
}

export type AddVersionRequest = {
  name: string
  mode: VersionMode
  qualityProfileId: number
  rootFolderId?: number | null
  folderName?: string | null
  monitored?: boolean
  minimumAvailability?: MinimumAvailability
  constraints?: Partial<Constraints> | null
}

export type UpdateVersionRequest = {
  name?: string | null
  mode?: VersionMode | null
  qualityProfileId?: number | null
  monitored?: boolean | null
  minimumAvailability?: MinimumAvailability | null
  constraints?: Partial<Constraints> | null
  clearConstraints?: boolean
}

export const movies = {
  list: () => api<MovieSummary[]>('/api/v1/movies'),
  get: (id: number) => api<Movie>(`/api/v1/movies/${id}`),
  lookup: (term: string) => api<MovieLookupResult[]>(`/api/v1/movies/lookup?term=${encodeURIComponent(term)}`),
  add: (body: AddMovieRequest) => post<Movie>('/api/v1/movies', body),
  remove: (id: number, addImportExclusion: boolean, deleteFiles = false) =>
    del(`/api/v1/movies/${id}?addImportExclusion=${addImportExclusion}&deleteFiles=${deleteFiles}`),
  refresh: (id: number) => post<Movie>(`/api/v1/movies/${id}/refresh`),
  addVersion: (movieId: number, body: AddVersionRequest) => post<Version>(`/api/v1/movies/${movieId}/versions`, body),
  updateVersion: (movieId: number, versionId: number, body: UpdateVersionRequest) =>
    put<Version>(`/api/v1/movies/${movieId}/versions/${versionId}`, body),
  deleteVersion: (movieId: number, versionId: number, promote?: number) =>
    del(`/api/v1/movies/${movieId}/versions/${versionId}${promote ? `?promote=${promote}` : ''}`),
  promote: (movieId: number, versionId: number) => post<Version>(`/api/v1/movies/${movieId}/versions/${versionId}/promote`),
  /** Drops the version's file (to the recycle bin when configured); the version stays and is missing again. */
  deleteFile: (movieId: number, versionId: number) => api<Version>(`/api/v1/movies/${movieId}/versions/${versionId}/file`, { method: 'DELETE' }),
}

// ---- quality ---------------------------------------------------------------

export type QualityProfile = { id: number; name: string; cutoff: number; upgradeAllowed: boolean; isSystem: boolean; preferredLanguage: string }

export type QualityDefinition = {
  id: number
  name: string
  title: string | null
  source: string
  resolution: number
  modifier: string
  /** MB per minute of runtime; null max means unlimited. */
  minSize: number | null
  preferredSize: number | null
  maxSize: number | null
}

export type QualityDefinitionUpdate = { title: string | null; minSize: number | null; preferredSize: number | null; maxSize: number | null }

/** One entry of the ordered items list, worst first: the position is the rank. */
export type QualityProfileItem = { groupId: number | null; groupName: string | null; qualityIds: number[]; allowed: boolean }

export type FormatScore = { customFormatId: number; customFormat: string; score: number }

export type QualityProfileDetail = {
  id: number
  name: string
  items: QualityProfileItem[]
  cutoff: number
  upgradeAllowed: boolean
  minFormatScore: number
  cutoffFormatScore: number
  minUpgradeFormatScore: number
  preferredLanguage: string
  isSystem: boolean
  formatScores: FormatScore[]
  versionCount: number
}

export type QualityProfileRequest = {
  name: string
  items: QualityProfileItem[]
  cutoff: number
  upgradeAllowed: boolean
  minFormatScore: number
  cutoffFormatScore: number
  minUpgradeFormatScore: number
  preferredLanguage: string
  formatScores: { customFormatId: number; score: number }[]
}

export const quality = {
  profiles: () => api<QualityProfile[]>('/api/v1/qualityprofiles'),
  profile: (id: number) => api<QualityProfileDetail>(`/api/v1/qualityprofiles/${id}`),
  /** A blank profile with every quality in order, nothing allowed. */
  schema: () => api<QualityProfileDetail>('/api/v1/qualityprofiles/schema'),
  addProfile: (body: QualityProfileRequest) => post<QualityProfileDetail>('/api/v1/qualityprofiles', body),
  updateProfile: (id: number, body: QualityProfileRequest) => put<QualityProfileDetail>(`/api/v1/qualityprofiles/${id}`, body),
  removeProfile: (id: number) => del(`/api/v1/qualityprofiles/${id}`),
  definitions: () => api<QualityDefinition[]>('/api/v1/qualitydefinitions'),
  updateDefinition: (id: number, body: QualityDefinitionUpdate) => put<QualityDefinition>(`/api/v1/qualitydefinitions/${id}`, body),
}

// ---- custom formats --------------------------------------------------------

export type CustomFormatSpecification = { name: string; implementation: string; negate: boolean; required: boolean; fields: Record<string, unknown> }

export type CustomFormat = {
  id: number
  name: string
  includeWhenRenaming: boolean
  specifications: CustomFormatSpecification[]
  /** Set when the format came from a guide file; a re-import updates the same row. */
  trashId: string | null
  /** Scores the guide file suggested, by profile family (default…); shown as a hint, never applied on its own. */
  trashScores: Record<string, number> | null
  scores: { qualityProfileId: number; qualityProfile: string; score: number }[]
}

/** `scores` is the full list: a profile left out or at zero loses its score. */
export type CustomFormatRequest = {
  name: string
  includeWhenRenaming: boolean
  specifications: CustomFormatSpecification[]
  scores: { qualityProfileId: number; score: number }[]
}

export type SpecificationFieldType = 'regex' | 'number' | 'select' | 'boolean'

export type SpecificationFieldSchema = { name: string; type: SpecificationFieldType; required: boolean; options: { value: number; label: string }[] | null }

export type SpecificationSchema = { implementation: string; fields: SpecificationFieldSchema[] }

export type CustomFormatSchema = { specifications: SpecificationSchema[] }

export type CustomFormatImportResult = { added: CustomFormat[]; updated: CustomFormat[]; scoresApplied: string[] }

export type CustomFormatEvaluation = {
  title: string
  parsedTitle: string | null
  year: number | null
  quality: string
  edition: string | null
  releaseGroup: string | null
  languages: string[]
  formats: { id: number; name: string }[]
  scores: { qualityProfileId: number; qualityProfile: string; score: number }[]
}

export type CustomFormatTestRequest = { title: string; specifications: CustomFormatSpecification[]; originalLanguage?: string; size?: number }

export type CustomFormatTest = {
  matches: boolean
  parsedTitle: string | null
  year: number | null
  quality: string
  edition: string | null
  releaseGroup: string | null
  languages: string[]
  specifications: { name: string; implementation: string; satisfied: boolean }[]
}

export const customFormats = {
  list: () => api<CustomFormat[]>('/api/v1/customformats'),
  /** The condition types the engine knows and the fields each one takes. */
  schema: () => api<CustomFormatSchema>('/api/v1/customformats/schema'),
  add: (body: CustomFormatRequest) => post<CustomFormat>('/api/v1/customformats', body),
  update: (id: number, body: CustomFormatRequest) => put<CustomFormat>(`/api/v1/customformats/${id}`, body),
  remove: (id: number) => del(`/api/v1/customformats/${id}`),
  /** The body is a Radarr export or guide JSON itself, one format or an array. Scores are left untouched. */
  import: (json: string) => api<CustomFormatImportResult>('/api/v1/customformats/import', { method: 'POST', body: json }),
  /** A draft judged before it is saved, condition by condition. */
  test: (body: CustomFormatTestRequest) => post<CustomFormatTest>('/api/v1/customformats/test', body),
  /** Without a film, a release naming no language has an unknown language; pass the original language to judge as Radarr would. */
  evaluate: (title: string, options: { movieId?: number; originalLanguage?: string } = {}) =>
    api<CustomFormatEvaluation>(
      `/api/v1/customformats/evaluate?title=${encodeURIComponent(title)}${options.movieId ? `&movieId=${options.movieId}` : ''}${
        options.originalLanguage ? `&originalLanguage=${encodeURIComponent(options.originalLanguage)}` : ''
      }`,
    ),
}

// ---- library ---------------------------------------------------------------

export type RootFolder = {
  id: number
  path: string
  accessible: boolean
  freeSpace: number | null
  totalSpace: number | null
  versionCount: number
  unmappedFolderCount: number
}

export type UnmappedReason = 'noVideoFiles' | 'notParsable' | 'noMatch' | 'ambiguousMatch' | 'metadataError'

export type UnmappedFolder = {
  id: number
  rootFolderId: number
  relativePath: string
  path: string
  fileCount: number
  reason: UnmappedReason
  detail: string | null
  discoveredAt: string
  lastSeenAt: string
}

export type ScanState = 'idle' | 'queued' | 'running' | 'completed' | 'failed'

export type ScanStatus = {
  rootFolderId: number
  state: ScanState
  startedAt: string | null
  finishedAt: string | null
  foldersTotal: number
  foldersProcessed: number
  moviesAdded: number
  versionsAdded: number
  filesAdded: number
  unmapped: number
  currentFolder: string | null
  error: string | null
}

export const rootFolders = {
  list: () => api<RootFolder[]>('/api/v1/rootfolders'),
  add: (path: string) => post<RootFolder>('/api/v1/rootfolders', { path }),
  remove: (id: number) => del(`/api/v1/rootfolders/${id}`),
  scan: (id: number) => post<ScanStatus>(`/api/v1/rootfolders/${id}/scan`),
  scanStatus: (id: number) => api<ScanStatus>(`/api/v1/rootfolders/${id}/scan`),
  unmapped: () => api<UnmappedFolder[]>('/api/v1/rootfolders/unmapped'),
}

// ---- indexers --------------------------------------------------------------

export type IndexerImplementation = 'torznab' | 'mock'

export type IndexerStatus = {
  healthy: boolean
  failures: number
  disabledUntil: string | null
  lastError: string | null
  lastFailureAt: string | null
  lastSuccessAt: string | null
}

export type Indexer = {
  id: number
  name: string
  implementation: IndexerImplementation
  baseUrl: string | null
  apiPath: string
  apiKey: string | null
  categories: number[]
  priority: number
  enableRss: boolean
  enableAutomaticSearch: boolean
  enableInteractiveSearch: boolean
  minimumSeeders: number
  seedRatio: number | null
  seedTimeMinutes: number | null
  rateLimitSeconds: number
  protocol: 'torrent' | 'usenet'
  /** Set when the row comes from Prowlarr: name, URL, key, priority and enabled state follow it. */
  prowlarrId: number | null
  status: IndexerStatus
  createdAt: string
}

export type IndexerRequest = Omit<Indexer, 'id' | 'protocol' | 'prowlarrId' | 'status' | 'createdAt'>

export type IndexerTestResult = {
  supportsMovieSearch: boolean
  supportsImdbSearch: boolean
  supportsTmdbSearch: boolean
  categoryCount: number
  recentReleases: number
}

export type ProwlarrSyncResult = { total: number; added: number; updated: number; removed: number; skipped: number; skippedNames: string[] }

export type ProwlarrStatus = {
  configured: boolean
  host: string | null
  syncOnStartup: boolean
  lastSyncAt: string | null
  lastError: string | null
  lastResult: ProwlarrSyncResult | null
}

export const prowlarr = {
  status: () => api<ProwlarrStatus>('/api/v1/indexers/prowlarr'),
  sync: () => post<ProwlarrSyncResult>('/api/v1/indexers/prowlarr/sync'),
}

export const indexers = {
  list: () => api<Indexer[]>('/api/v1/indexers'),
  get: (id: number) => api<Indexer>(`/api/v1/indexers/${id}`),
  add: (body: IndexerRequest) => post<Indexer>('/api/v1/indexers', body),
  update: (id: number, body: IndexerRequest) => put<Indexer>(`/api/v1/indexers/${id}`, body),
  remove: (id: number) => del(`/api/v1/indexers/${id}`),
  /** Tests an unsaved configuration: what the form calls. */
  test: (body: IndexerRequest) => post<IndexerTestResult>('/api/v1/indexers/test', body),
  testSaved: (id: number) => post<IndexerTestResult>(`/api/v1/indexers/${id}/test`),
}

// ---- search ----------------------------------------------------------------

export type Rejection = { reason: string; temporary: boolean }

export type VersionDecision = { versionId: number; versionName: string; approved: boolean; rejections: Rejection[]; customFormatScore: number }

export type IndexerFailureKind = 'unreachable' | 'authentication' | 'rateLimited' | 'serverError' | 'invalidResponse' | 'request' | 'disabled'

export type Release = {
  guid: string
  title: string
  indexerId: number
  indexer: string
  indexerPriority: number
  protocol: 'torrent' | 'usenet'
  size: number
  seeders: number | null
  leechers: number | null
  publishDate: string
  downloadUrl: string | null
  magnetUrl: string | null
  infoUrl: string | null
  infoHash: string | null
  /** Comma-separated flag names, "none" when there are none. */
  flags: string
  qualityId: number
  quality: string
  resolution: number
  revisionVersion: number
  isRepack: boolean
  edition: string | null
  releaseGroup: string | null
  languages: string[]
  /** Custom formats matched; the score depends on each version's profile and sits on the verdict. */
  customFormats: string[]
  parsedTitle: string | null
  parsedYear: number | null
  matchesMovie: boolean
  rejections: Rejection[]
  versions: VersionDecision[]
}

export type SearchResult = {
  movieId: number
  searchedAt: string
  indexersQueried: number
  releases: Release[]
  indexerErrors: { indexerId: number; indexer: string; kind: IndexerFailureKind; message: string }[]
}

export const search = {
  movie: (movieId: number, versionId?: number) =>
    api<SearchResult>(`/api/v1/search?movieId=${movieId}${versionId ? `&versionId=${versionId}` : ''}`),
}

// ---- rss -------------------------------------------------------------------

export type RssMovieRef = { id: number; title: string; year: number | null; posterUrl: string | null }

export type RssRelease = {
  guid: string
  title: string
  indexerId: number
  indexer: string
  size: number
  seeders: number | null
  leechers: number | null
  publishDate: string
  seenAt: string
  infoUrl: string | null
  flags: string
  qualityId: number
  quality: string
  revisionVersion: number
  isRepack: boolean
  edition: string | null
  releaseGroup: string | null
  languages: string[]
  customFormats: string[]
  parsedTitle: string | null
  parsedYear: number | null
  movie: RssMovieRef | null
  unmatchedReason: string | null
  versions: VersionDecision[]
  attributedVersionId: number | null
  attributedVersionName: string | null
}

export type RssIndexerStatus = { indexerId: number; indexer: string; lastRunAt: string | null; lastError: string | null; lastFetched: number; lastNew: number }

export type RssStatus = {
  enabled: boolean
  intervalMinutes: number
  running: boolean
  lastRunAt: string | null
  nextRunAt: string | null
  stored: number
  matched: number
  wouldGrab: number
  indexers: RssIndexerStatus[]
}

export type RssSyncResult = {
  indexers: number
  fetched: number
  new: number
  matched: number
  wouldGrab: number
  indexerErrors: { indexerId: number; indexer: string; kind: IndexerFailureKind; message: string }[]
}

export const rss = {
  status: () => api<RssStatus>('/api/v1/rss'),
  releases: (matched?: boolean, limit = 200) =>
    api<RssRelease[]>(`/api/v1/rss/releases?limit=${limit}${matched === undefined ? '' : `&matched=${matched}`}`),
  sync: () => post<RssSyncResult>('/api/v1/rss/sync'),
}

// ---- download clients ------------------------------------------------------

export type DownloadClientImplementation = 'qBittorrent' | 'mock'

export type DownloadClient = {
  id: number
  name: string
  implementation: DownloadClientImplementation
  enabled: boolean
  host: string | null
  port: number
  useSsl: boolean
  urlBase: string | null
  username: string | null
  password: string | null
  category: string
  addPaused: boolean
  priority: number
  protocol: 'torrent' | 'usenet'
  createdAt: string
}

export type DownloadClientRequest = Omit<DownloadClient, 'id' | 'protocol' | 'createdAt'>

export type DownloadClientTestResult = { version: string; apiVersion: string | null; category: string; categoryCreated: boolean }

export const downloadClients = {
  list: () => api<DownloadClient[]>('/api/v1/downloadclients'),
  add: (body: DownloadClientRequest) => post<DownloadClient>('/api/v1/downloadclients', body),
  update: (id: number, body: DownloadClientRequest) => put<DownloadClient>(`/api/v1/downloadclients/${id}`, body),
  remove: (id: number) => del(`/api/v1/downloadclients/${id}`),
  /** Tests an unsaved configuration: what the form calls. */
  test: (body: DownloadClientRequest) => post<DownloadClientTestResult>('/api/v1/downloadclients/test', body),
  testSaved: (id: number) => post<DownloadClientTestResult>(`/api/v1/downloadclients/${id}/test`),
  /** The mock client is driven by hand, like a user would in qBittorrent. */
  mock: {
    complete: (downloadId: string) => post<void>(`/api/v1/downloadclients/mock/${encodeURIComponent(downloadId)}/complete`),
    fail: (downloadId: string) => post<void>(`/api/v1/downloadclients/mock/${encodeURIComponent(downloadId)}/fail`),
    remove: (downloadId: string) => del(`/api/v1/downloadclients/mock/${encodeURIComponent(downloadId)}`),
  },
}

// ---- grab & queue ----------------------------------------------------------

export type GrabState = 'grabbed' | 'downloading' | 'importPending' | 'importing' | 'imported' | 'importFailed' | 'downloadFailed' | 'removed' | 'ignored'

export type DownloadItemStatus = 'queued' | 'downloading' | 'paused' | 'stalled' | 'completed' | 'failed' | 'unknown'

export type StatusSeverity = 'info' | 'warning' | 'error'

export type StatusMessage = { title: string; messages: string[]; severity: StatusSeverity }

/** The release as the search returned it: the search keeps no state, so the client carries the links. */
export type GrabRequest = {
  versionId: number
  indexerId: number
  guid: string
  title: string
  downloadUrl: string | null
  magnetUrl: string | null
  infoHash: string | null
  size: number
  seeders: number | null
  flags: string
  protocol: 'torrent' | 'usenet'
  indexer: string
}

export type GrabResult = { grabId: number; downloadId: string; downloadClientId: number; downloadClient: string; state: GrabState; grabbedAt: string }

export type QueueItem = {
  grabId: number | null
  downloadId: string
  title: string
  quality: string | null
  movieId: number | null
  movieTitle: string | null
  movieYear: number | null
  versionId: number | null
  versionName: string | null
  state: GrabState | null
  status: DownloadItemStatus
  size: number
  sizeLeft: number
  /** 0 to 1. */
  progress: number
  downloadRate: number
  etaSeconds: number | null
  downloadClientId: number
  downloadClient: string
  indexerId: number | null
  indexer: string | null
  outputPath: string | null
  statusMessages: StatusMessage[]
  addedAt: string
  /** False for a torrent found in our category that Bigarr never grabbed. */
  tracked: boolean
}

export type QueueClientStatus = { downloadClientId: number; name: string; healthy: boolean; lastError: string | null; lastSuccessAt: string | null }

export type Queue = {
  lastPollAt: string | null
  nextPollAt: string | null
  pollSeconds: number
  revision: number
  items: QueueItem[]
  clients: QueueClientStatus[]
}

export const queue = {
  get: () => api<Queue>('/api/v1/queue'),
  /** Polls the clients now. */
  refresh: () => post<Queue>('/api/v1/queue/refresh'),
  /** text/event-stream: the current snapshot first, then one event per poll (event name "queue"). */
  eventsUrl: '/api/v1/queue/events',
  remove: (grabId: number, options: { removeFromClient: boolean; deleteFiles: boolean; blocklist: boolean }) =>
    del(`/api/v1/queue/${grabId}?removeFromClient=${options.removeFromClient}&deleteFiles=${options.deleteFiles}&blocklist=${options.blocklist}`),
  removeUntracked: (clientId: number, downloadId: string, deleteFiles: boolean) =>
    del(`/api/v1/queue/untracked/${clientId}/${encodeURIComponent(downloadId)}?deleteFiles=${deleteFiles}`),
  grab: (body: GrabRequest) => post<GrabResult>('/api/v1/grab', body),
  /** A failed import goes back to pending and the worker tries again. */
  retryImport: (grabId: number) => post<void>(`/api/v1/queue/${grabId}/import`),
}

// ---- media management ------------------------------------------------------

export type TransferMode = 'hardlinkOrCopy' | 'copy' | 'move'

export type NamingToken = { token: string; description: string; example: string; folder: boolean }

export type MediaManagement = {
  renameFiles: boolean
  fileNameTemplate: string
  folderNameTemplate: string
  transferMode: TransferMode
  recycleBinPath: string | null
  recycleBinRetentionDays: number
  /** ffprobe was found: imported files get media info, the backfill fills the older ones. */
  mediaInfoAvailable: boolean
  ffprobePath: string | null
  fileNamePreview: string
  folderNamePreview: string
  tokens: NamingToken[]
}

export type MediaManagementRequest = Pick<MediaManagement, 'renameFiles' | 'fileNameTemplate' | 'folderNameTemplate' | 'transferMode' | 'recycleBinPath' | 'recycleBinRetentionDays'>

export type NamingPreview = { fileName: string | null; fileErrors: string[]; folderName: string | null; folderErrors: string[] }

export type BackfillResult = { mediaInfoAvailable: boolean; probed: number; remaining: number }

export const mediaManagement = {
  get: () => api<MediaManagement>('/api/v1/mediamanagement'),
  update: (body: MediaManagementRequest) => put<MediaManagement>('/api/v1/mediamanagement', body),
  /** A draft template rendered on the sample film, with the errors that would refuse it. */
  preview: (fileNameTemplate: string | null, folderNameTemplate: string | null) =>
    post<NamingPreview>('/api/v1/mediamanagement/naming/preview', { fileNameTemplate, folderNameTemplate }),
  /** Probes a batch of files that have no media info yet. */
  backfill: () => post<BackfillResult>('/api/v1/mediamanagement/backfill'),
}

// ---- manual import ---------------------------------------------------------

export type ImportVersionOption = { versionId: number; versionName: string; isDefault: boolean; approved: boolean; rejections: string[] }

export type ManualImportFile = {
  path: string
  relativePath: string
  name: string
  size: number
  isSample: boolean
  parsedTitle: string | null
  parsedYear: number | null
  qualityId: number
  quality: string
  revisionVersion: number
  isRepack: boolean
  edition: string | null
  releaseGroup: string | null
  languages: string[]
  movieId: number | null
  movieTitle: string | null
  movieYear: number | null
  /** Why no movie was found, when none was. */
  matchReason: string | null
  /** The version the import would pick on its own, when one accepts the file. */
  versionId: number | null
  versions: ImportVersionOption[]
}

export type ManualImport = {
  path: string
  grabId: number | null
  releaseTitle: string | null
  defaultTransferMode: TransferMode
  files: ManualImportFile[]
}

export type ManualImportItem = { path: string; versionId: number; qualityId?: number | null; grabId?: number | null }

export type ManualImportResult = {
  path: string
  versionId: number
  imported: boolean
  reason: string | null
  mediaFileId: number | null
  destinationPath: string | null
  upgraded: boolean
}

export const manualImport = {
  /** The video files of a path or of a queued download, with what Bigarr makes of them. */
  list: (options: { path?: string; grabId?: number; movieId?: number }) => {
    const params = new URLSearchParams()
    if (options.path) params.set('path', options.path)
    if (options.grabId) params.set('grabId', String(options.grabId))
    if (options.movieId) params.set('movieId', String(options.movieId))
    return api<ManualImport>(`/api/v1/manualimport?${params.toString()}`)
  },
  /** Forced: no upgrade check, a one-shot with a file is replaced too. */
  import: (files: ManualImportItem[], transferMode?: TransferMode | null) => post<ManualImportResult[]>('/api/v1/manualimport', { files, transferMode: transferMode ?? null }),
}

// ---- rename ----------------------------------------------------------------

export type RenamePreview = { versionId: number; versionName: string; mediaFileId: number; currentRelativePath: string; newRelativePath: string; changed: boolean }

export type RenameResult = { mediaFileId: number; relativePath: string; renamed: boolean; error: string | null }

export const rename = {
  preview: (movieId: number) => api<RenamePreview[]>(`/api/v1/rename?movieId=${movieId}`),
  apply: (movieId: number, mediaFileIds?: number[]) => post<RenameResult[]>('/api/v1/rename', { movieId, mediaFileIds: mediaFileIds ?? null }),
}

// ---- history ---------------------------------------------------------------

export type HistoryEventType = 'grabbed' | 'downloadFailed' | 'imported' | 'upgraded' | 'importFailed' | 'fileDeleted' | 'renamed' | 'blocklisted' | 'defaultChanged'

export type HistoryEvent = {
  id: number
  eventType: HistoryEventType
  movieId: number | null
  tmdbId: number | null
  movieTitle: string | null
  versionId: number | null
  versionName: string | null
  sourceTitle: string | null
  indexerId: number | null
  indexerName: string | null
  mediaFileId: number | null
  data: Record<string, string>
  createdAt: string
}

export const history = {
  list: (options: { movieId?: number; versionId?: number; eventType?: HistoryEventType; limit?: number } = {}) => {
    const params = new URLSearchParams()
    if (options.movieId) params.set('movieId', String(options.movieId))
    if (options.versionId) params.set('versionId', String(options.versionId))
    if (options.eventType) params.set('eventType', options.eventType)
    if (options.limit) params.set('limit', String(options.limit))
    const query = params.toString()
    return api<HistoryEvent[]>(`/api/v1/history${query ? `?${query}` : ''}`)
  },
}

// ---- helpers ---------------------------------------------------------------

export function slugify(value: string): string {
  return value
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
}
