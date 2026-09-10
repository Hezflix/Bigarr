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
  remove: (id: number, addImportExclusion: boolean) => del(`/api/v1/movies/${id}?addImportExclusion=${addImportExclusion}`),
  refresh: (id: number) => post<Movie>(`/api/v1/movies/${id}/refresh`),
  addVersion: (movieId: number, body: AddVersionRequest) => post<Version>(`/api/v1/movies/${movieId}/versions`, body),
  updateVersion: (movieId: number, versionId: number, body: UpdateVersionRequest) =>
    put<Version>(`/api/v1/movies/${movieId}/versions/${versionId}`, body),
  deleteVersion: (movieId: number, versionId: number, promote?: number) =>
    del(`/api/v1/movies/${movieId}/versions/${versionId}${promote ? `?promote=${promote}` : ''}`),
  promote: (movieId: number, versionId: number) => post<Version>(`/api/v1/movies/${movieId}/versions/${versionId}/promote`),
}

// ---- quality ---------------------------------------------------------------

export type QualityProfile = { id: number; name: string; cutoff: number; upgradeAllowed: boolean; isSystem: boolean; preferredLanguage: string }

export const quality = {
  profiles: () => api<QualityProfile[]>('/api/v1/qualityprofiles'),
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

export type VersionDecision = { versionId: number; versionName: string; approved: boolean; rejections: Rejection[] }

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

// ---- helpers ---------------------------------------------------------------

export function slugify(value: string): string {
  return value
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
}
