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
  /** Bigarr__Scheduler__Enabled: when false no task runs on its own; each runs only when triggered. */
  schedulerEnabled: boolean
  /** Bigarr__Shadow__Enabled: grabs are recorded as Bigarr's picks, nothing is sent to a download client. */
  shadowEnabled: boolean
}

export const auth = {
  me: () => api<Me>('/api/v1/auth/me'),
  login: (username: string, password: string) => post<void>('/api/v1/auth/login', { username, password, rememberMe: true }),
  logout: () => post<void>('/api/v1/auth/logout'),
}

export const system = {
  status: () => api<SystemStatus>('/api/v1/system/status'),
}

// ---- API keys (Connect) ----------------------------------------------------

/** What a key sees through the Radarr-compatible API: `default`, `all` or `version:<slug>`. */
export type ApiKey = {
  id: number
  name: string
  keyPrefix: string
  compatView: string
  createdAt: string
  lastUsedAt: string | null
  revokedAt: string | null
}

/** The only response that carries the key itself. */
export type ApiKeyCreated = ApiKey & { key: string }

export const apiKeys = {
  list: () => api<ApiKey[]>('/api/v1/apikeys'),
  create: (name: string, compatView: string) => post<ApiKeyCreated>('/api/v1/apikeys', { name, compatView }),
  revoke: (id: number) => del(`/api/v1/apikeys/${id}`),
}

// ---- backups ---------------------------------------------------------------

export type Backup = { name: string; size: number; createdAt: string; type: 'manual' | 'scheduled' }

export type RestoreResult = { name: string; restartRequested: boolean; message: string }

export const backups = {
  list: () => api<Backup[]>('/api/v1/system/backup'),
  create: () => post<Backup>('/api/v1/system/backup'),
  remove: (name: string) => del(`/api/v1/system/backup/${encodeURIComponent(name)}`),
  /** Stages the database and restarts Bigarr; the page must expect the connection to drop. */
  restore: (name: string) => post<RestoreResult>(`/api/v1/system/backup/${encodeURIComponent(name)}/restore`),
  downloadUrl: (name: string) => `/api/v1/system/backup/${encodeURIComponent(name)}/download`,
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
  /** Side files imported with it. Files written by others (Bazarr) are not listed. */
  extras: ExtraFile[]
}

export type ExtraFileKind = 'subtitle' | 'other'

export type ExtraFile = {
  id: number
  relativePath: string
  path: string
  extension: string
  kind: ExtraFileKind
  /** ISO 639-1 code, null when the name did not say. */
  language: string | null
  forced: boolean
  hearingImpaired: boolean
  dateAdded: string
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
  minimumAvailability: MinimumAvailability
  rootFolderId: number
  /** Tag ids. */
  tags: number[]
  /** File size in bytes; null without a file. */
  size: number | null
  /** Null without a file; false when the file is below the profile cutoff (quality and custom format score). */
  cutoffMet: boolean | null
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
  addedAt: string
  tmdbRating: number | null
  imdbRating: number | null
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
  /** Search the indexers right away, in the background. */
  searchOnAdd?: boolean
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
  searchOnAdd?: boolean
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
  bulkUpdate: (body: BulkUpdateRequest) => put<BulkResponse>('/api/v1/movies/bulk', body),
  /** Queues an automatic search per targeted version; they run in the background, one at a time. */
  bulkSearch: (movieIds: number[], versions: VersionSelector) => post<BulkResponse>('/api/v1/movies/bulk/search', { movieIds, versions }),
  /** With `dryRun`, answers the exact plan and changes nothing. */
  bulkDelete: (body: BulkDeleteRequest) => post<BulkDeleteResponse>('/api/v1/movies/bulk/delete', body),
}

// ---- bulk edit -------------------------------------------------------------

/** Which version of each film a bulk action touches; same syntax as an API key's view. */
export type VersionSelector = 'default' | 'all' | `version:${string}`

export type TagsMode = 'add' | 'remove' | 'replace'

/** Null fields are left untouched. */
export type BulkUpdateRequest = {
  movieIds: number[]
  versions: VersionSelector
  monitored?: boolean | null
  qualityProfileId?: number | null
  minimumAvailability?: MinimumAvailability | null
  tagIds?: number[] | null
  applyTags?: TagsMode | null
}

export type BulkOutcome = 'updated' | 'unchanged' | 'queued' | 'alreadyQueued' | 'movieDeleted' | 'versionsDeleted' | 'skipped' | 'failed'

export type BulkMovieResult = {
  movieId: number
  title: string
  outcome: BulkOutcome
  /** Names of the versions acted on. */
  versions: string[]
  /** Why the film was skipped or failed. */
  detail: string | null
  /** Delete: files removed. */
  files: number
  /** Delete: the version that became the film's default. */
  promoted: string | null
}

export type BulkResponse = { results: BulkMovieResult[] }

export type BulkDeleteRequest = {
  movieIds: number[]
  versions: VersionSelector
  deleteFiles: boolean
  addImportExclusion: boolean
  dryRun: boolean
}

export type BulkDeleteResponse = {
  dryRun: boolean
  /** Where deleted files go; null means they are deleted for good. */
  recycleBin: string | null
  results: BulkMovieResult[]
}

// ---- tags ------------------------------------------------------------------

export type Tag = { id: number; label: string }

export const tags = {
  list: () => api<Tag[]>('/api/v1/tags'),
  /** Idempotent by label: an existing tag comes back as is. */
  add: (label: string) => post<Tag>('/api/v1/tags', { label }),
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

/** What a root folder holds: the film scan and the add pages only see roots of their own kind. */
export type MediaKind = 'movies' | 'series'

export type RootFolder = {
  id: number
  path: string
  accessible: boolean
  freeSpace: number | null
  totalSpace: number | null
  versionCount: number
  unmappedFolderCount: number
  kind: MediaKind
}

/** For `useQuery({ select })`: the roots of one kind. */
export const rootsOfKind = (kind: MediaKind) => (roots: RootFolder[]) => roots.filter((r) => r.kind === kind)

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
  kind: MediaKind
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
  seriesAdded: number
  unmatchedFiles: number
  seriesId: number | null
}

export const rootFolders = {
  list: () => api<RootFolder[]>('/api/v1/rootfolders'),
  add: (path: string, kind: MediaKind = 'movies') => post<RootFolder>('/api/v1/rootfolders', { path, kind }),
  setKind: (id: number, kind: MediaKind) => put<RootFolder>(`/api/v1/rootfolders/${id}`, { kind }),
  remove: (id: number) => del(`/api/v1/rootfolders/${id}`),
  scan: (id: number) => post<ScanStatus>(`/api/v1/rootfolders/${id}/scan`),
  scanStatus: (id: number) => api<ScanStatus>(`/api/v1/rootfolders/${id}/scan`),
  unmapped: () => api<UnmappedFolder[]>('/api/v1/rootfolders/unmapped'),
}

// ---- filesystem ------------------------------------------------------------

export type FileSystemItem = { name: string; path: string; size: number; lastModified: string | null }

/** One folder as the path picker reads it. `path` is empty and `parent` null when the mount points come back. */
export type FolderListing = {
  path: string
  parent: string | null
  exists: boolean
  directories: FileSystemItem[]
  files: FileSystemItem[]
}

export const filesystem = {
  /** A path that does not exist yet answers with its deepest existing ancestor, so a half-typed path still suggests. */
  browse: (path: string, includeFiles = false) => api<FolderListing>(`/api/v1/filesystem?path=${encodeURIComponent(path)}&includeFiles=${includeFiles}`),
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
  /** Set when Prowlarr manages the row: each of its syncs rewrites everything but the rate limit. */
  prowlarrId: number | null
  status: IndexerStatus
  createdAt: string
  /** Series categories; empty until filled from the indexer's capabilities (first series search or test). */
  tvCategories: number[]
  /** Anime categories, added to the TV ones for anime; empty until filled from the capabilities. */
  animeCategories: number[]
}

export type IndexerRequest = Omit<Indexer, 'id' | 'protocol' | 'prowlarrId' | 'status' | 'createdAt'>

export type IndexerTestResult = {
  supportsMovieSearch: boolean
  supportsImdbSearch: boolean
  supportsTmdbSearch: boolean
  categoryCount: number
  recentReleases: number
  supportsTvSearch: boolean
  tvSearchParams: string[] | null
  /** What the capabilities list: an empty row field falls back to these. */
  tvCategories: number[] | null
  animeCategories: number[] | null
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
  /** False when the release does not look like this film; it is still judged by every version and can be grabbed by hand. */
  matchesMovie: boolean
  rejections: Rejection[]
  /** One verdict per version; empty only when the title does not parse. */
  versions: VersionDecision[]
  /** Where a grab goes unless the user picks another version: the one that accepts it, else the closest, else the default. */
  suggestedVersionId: number | null
}

export type SearchResult = {
  movieId: number
  searchedAt: string
  indexersQueried: number
  releases: Release[]
  indexerErrors: { indexerId: number; indexer: string; kind: IndexerFailureKind; message: string }[]
  /** The text the indexers were asked, when the user typed one; null for the film's own search (ids and titles). */
  query: string | null
}

/** `picked`: shadow mode recorded the best approved release instead of grabbing it. */
export type AutomaticSearchOutcome = 'grabbed' | 'nothingFound' | 'noIndexer' | 'skipped' | 'failed' | 'picked'

/** One version's outcome of an automatic search: the best approved release was grabbed, or here is why not. */
export type AutomaticSearchResult = {
  movieId: number
  movieTitle: string
  versionId: number
  versionName: string
  outcome: AutomaticSearchOutcome
  releaseTitle: string | null
  grabId: number | null
  detail: string | null
  releases: number
  approved: number
  indexerErrors: { indexerId: number; indexer: string; kind: IndexerFailureKind; message: string }[]
}

export const search = {
  /** Interactive search for the film, judged by every version (or one); `query` replaces its ids and titles with a text. */
  movie: (movieId: number, options: { versionId?: number; query?: string } = {}) => {
    const params = new URLSearchParams({ movieId: String(movieId) })
    if (options.versionId) params.set('versionId', String(options.versionId))
    if (options.query?.trim()) params.set('query', options.query.trim())
    return api<SearchResult>(`/api/v1/search?${params}`)
  },
  /** Interactive search for a series: an episode, a season (packs and singles together) or the whole series. */
  series: (seriesId: number, scope: { episodeId?: number; season?: number }, options: { versionId?: number; query?: string } = {}) => {
    const params = new URLSearchParams()
    if (scope.episodeId) params.set('episodeId', String(scope.episodeId))
    if (scope.season !== undefined) params.set('season', String(scope.season))
    if (options.versionId) params.set('versionId', String(options.versionId))
    if (options.query?.trim()) params.set('query', options.query.trim())
    return api<SeriesSearchResult>(`/api/v1/series/${seriesId}/search?${params}`)
  },
  /** Automatic search on demand: every version of the film (or one), the best approved release grabbed. */
  automatic: (movieId: number, versionId?: number) =>
    post<AutomaticSearchResult[]>(`/api/v1/movies/${movieId}/search${versionId ? `?versionId=${versionId}` : ''}`),
}

export type SeriesEpisodeRef = { id: number; seasonNumber: number; episodeNumber: number; absoluteEpisodeNumber: number | null; title: string }

/** One series version's verdict over every episode the release covers. */
export type SeriesVersionDecision = VersionDecision & { targetIds: number[] }

export type SeriesRelease = Omit<Release, 'edition' | 'parsedTitle' | 'parsedYear' | 'matchesMovie' | 'versions'> & {
  parsedSeriesTitle: string | null
  /** False when the release names another show; it is still judged and can be grabbed by hand. */
  matchesSeries: boolean
  /** The episodes it covers in TVDB numbering; empty when it maps to none (the reason is in `rejections`). */
  episodes: SeriesEpisodeRef[]
  episodesLabel: string | null
  fullSeason: boolean
  versions: SeriesVersionDecision[]
}

export type SeriesSearchResult = {
  seriesId: number
  scope: 'episode' | 'season' | 'series'
  seasonNumber: number | null
  episodeId: number | null
  label: string
  searchedAt: string
  indexersQueried: number
  releases: SeriesRelease[]
  indexerErrors: { indexerId: number; indexer: string; kind: IndexerFailureKind; message: string }[]
  query: string | null
}

// ---- wanted ----------------------------------------------------------------

export type WantedFilter = 'missing' | 'cutoffUnmet'

export type WantedItem = {
  movieId: number
  movieTitle: string
  year: number | null
  posterUrl: string | null
  versionId: number
  versionName: string
  qualityProfile: string
  kind: WantedFilter
  currentQuality: string | null
  currentScore: number | null
  cutoffQuality: string
  lastSearchAt: string | null
  searchAttempts: number
  /** When the cutoff-unmet job may search it again; null means at its next pass. */
  nextSearchAt: string | null
}

export type WantedPage = { items: WantedItem[]; total: number; page: number; pageSize: number }

export const wanted = {
  list: (filter?: WantedFilter, page = 1, pageSize = 50) =>
    api<WantedPage>(`/api/v1/wanted?page=${page}&pageSize=${pageSize}${filter ? `&filter=${filter}` : ''}`),
  /** Queues a search for everything on the list, backoff ignored; runs in the background one version at a time. */
  searchAll: (filter?: WantedFilter) => post<{ queued: number }>(`/api/v1/wanted/search${filter ? `?filter=${filter}` : ''}`),
}

// ---- rss -------------------------------------------------------------------

export type RssMovieRef = { id: number; title: string; year: number | null; posterUrl: string | null }

export type RssSeriesRef = { id: number; title: string; year: number | null; posterUrl: string | null }

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
  /** Set when the pass sent the release to a download client. */
  grabId: number | null
  /** Why the attributed release was not grabbed after all. */
  grabError: string | null
  /** Shadow mode: the pick recorded in place of a grab. */
  shadowPickId: number | null
  /** A series release: the series of the library (then `movie` is null); verdicts and attribution are the series versions'. */
  series: RssSeriesRef | null
  /** The episodes it covers: "S01E05", "S01E01–S01E10 (10 episodes)". */
  episodes: string | null
  episodeIds: number[] | null
}

export type RssIndexerStatus = { indexerId: number; indexer: string; lastRunAt: string | null; lastError: string | null; lastFetched: number; lastNew: number }

export type RssStatus = {
  /** Bigarr__Rss__Enabled: the pass is one of the scheduled tasks. */
  enabled: boolean
  /** Bigarr__Scheduler__Enabled: when false the pass only runs when triggered. */
  schedulerEnabled: boolean
  intervalMinutes: number
  running: boolean
  lastRunAt: string | null
  /** Set only when the pass runs on its own. */
  nextRunAt: string | null
  stored: number
  matched: number
  wouldGrab: number
  indexers: RssIndexerStatus[]
  grabbed: number
  /** Shadow mode: releases recorded as picks instead of grabs. */
  picked: number
}

export type RssSyncResult = {
  indexers: number
  fetched: number
  new: number
  matched: number
  wouldGrab: number
  indexerErrors: { indexerId: number; indexer: string; kind: IndexerFailureKind; message: string }[]
  grabbed: number
  picked: number
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
    stall: (downloadId: string) => post<void>(`/api/v1/downloadclients/mock/${encodeURIComponent(downloadId)}/stall`),
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
  /** Kept with the pick in shadow mode. */
  publishDate?: string | null
  /** A series grab: the shared ids of the episodes covered; `versionId` is then the series version. */
  episodeIds?: number[] | null
}

/** A grab sent to a client; in shadow mode only `shadowPickId` and `grabbedAt` are set (`shadow` is true). */
export type GrabResult = {
  grabId: number | null
  downloadId: string | null
  downloadClientId: number | null
  downloadClient: string | null
  state: GrabState | null
  grabbedAt: string
  shadowPickId: number | null
  shadow: boolean
}

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
  importExtraFiles: boolean
  /** Comma list without dots: `srt,ass`. */
  extraFileExtensions: string
  /** ffprobe was found: imported files get media info, the backfill fills the older ones. */
  mediaInfoAvailable: boolean
  ffprobePath: string | null
  fileNamePreview: string
  folderNamePreview: string
  tokens: NamingToken[]
}

export type MediaManagementRequest = Pick<
  MediaManagement,
  'renameFiles' | 'fileNameTemplate' | 'folderNameTemplate' | 'transferMode' | 'recycleBinPath' | 'recycleBinRetentionDays' | 'importExtraFiles' | 'extraFileExtensions'
>

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
  /** Side files that would come along (extra files setting); empty when the setting is off. */
  extras: ManualImportExtra[]
}

export type ManualImportExtra = Pick<ExtraFile, 'path' | 'relativePath' | 'extension' | 'kind' | 'language' | 'forced' | 'hearingImpaired'>

export type ManualImport = {
  path: string
  grabId: number | null
  releaseTitle: string | null
  defaultTransferMode: TransferMode
  files: ManualImportFile[]
}

/** `extraPaths`: the side files to bring, as listed; omitted lets the import find them, empty brings none. */
export type ManualImportItem = { path: string; versionId: number; qualityId?: number | null; grabId?: number | null; extraPaths?: string[] | null }

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

export type ExtraRenamePreview = { extraFileId: number; currentRelativePath: string; newRelativePath: string }

/** `extras` follow the file: renamed with it when it changes. */
export type RenamePreview = {
  versionId: number
  versionName: string
  mediaFileId: number
  currentRelativePath: string
  newRelativePath: string
  changed: boolean
  extras: ExtraRenamePreview[]
}

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

// ---- blocklist -------------------------------------------------------------

export type BlocklistEntry = {
  id: number
  versionId: number
  versionName: string | null
  movieId: number | null
  movieTitle: string | null
  movieYear: number | null
  posterUrl: string | null
  sourceTitle: string
  quality: string | null
  releaseGuid: string | null
  infoHash: string | null
  indexerId: number | null
  indexerName: string | null
  reason: string | null
  createdAt: string
}

export type BlocklistPage = { items: BlocklistEntry[]; total: number; page: number; pageSize: number }

export const blocklist = {
  list: (options: { movieId?: number; page?: number; pageSize?: number } = {}) => {
    const params = new URLSearchParams()
    if (options.movieId) params.set('movieId', String(options.movieId))
    params.set('page', String(options.page ?? 1))
    params.set('pageSize', String(options.pageSize ?? 50))
    return api<BlocklistPage>(`/api/v1/blocklist?${params.toString()}`)
  },
  remove: (id: number) => del(`/api/v1/blocklist/${id}`),
  bulkDelete: (ids: number[]) => post<number>('/api/v1/blocklist/bulk-delete', { ids }),
}

// ---- notifications ---------------------------------------------------------

/**
 * `bigarr`: Bigarr's own body for every event. `radarr`: Radarr's webhook payloads, for tools written for Radarr.
 * `discord`: embeds for a Discord channel webhook, laid out like Radarr's Discord connection.
 */
export type WebhookFormat = 'bigarr' | 'radarr' | 'discord'

export const discordGrabFields = [
  'overview',
  'rating',
  'genres',
  'version',
  'quality',
  'group',
  'size',
  'links',
  'release',
  'poster',
  'fanart',
  'indexer',
  'customFormats',
  'customFormatScore',
  'tags',
] as const
export const discordImportFields = [
  'overview',
  'rating',
  'genres',
  'version',
  'quality',
  'codecs',
  'group',
  'size',
  'languages',
  'subtitles',
  'links',
  'release',
  'poster',
  'fanart',
  'tags',
  'customFormats',
  'customFormatScore',
] as const
export const discordManualInteractionFields = ['overview', 'rating', 'genres', 'version', 'quality', 'group', 'size', 'links', 'downloadTitle', 'poster', 'fanart', 'tags'] as const

export type DiscordGrabField = (typeof discordGrabFields)[number]
export type DiscordImportField = (typeof discordImportFields)[number]
export type DiscordManualInteractionField = (typeof discordManualInteractionFields)[number]

/** Radarr's Discord settings. A missing field list takes Radarr's defaults; an empty one sends no field. */
export type DiscordSettings = {
  username?: string | null
  avatar?: string | null
  author?: string | null
  /** Grabs and failed downloads. */
  grabFields?: DiscordGrabField[] | null
  /** Imports and upgrades; poster and fanart also apply to added and deleted films. */
  importFields?: DiscordImportField[] | null
  /** Import failures (Radarr's "manual interaction required"). */
  manualInteractionFields?: DiscordManualInteractionField[] | null
}

export type NotificationEndpointRequest = {
  name: string
  url: string
  secret?: string | null
  enabled?: boolean
  /** Event types to send; empty means every event. */
  events?: string[]
  /** Tag ids: only films whose versions carry one of them. Empty means every film. */
  tags?: number[]
  format?: WebhookFormat
  /** Radarr format only: `default`, `all` or `version:<name>`, like an API key's view. */
  view?: string
  /** Discord format only. */
  discord?: DiscordSettings | null
}

export type NotificationEndpoint = {
  id: number
  name: string
  url: string
  hasSecret: boolean
  enabled: boolean
  events: string[]
  tags: number[]
  format: WebhookFormat
  view: string
  discord: DiscordSettings | null
  pendingDeliveries: number
  failedDeliveries: number
  lastDeliveredAt: string | null
  lastError: string | null
  createdAt: string
}

export type NotificationEventType = {
  type: string
  description: string
  /** What the Radarr format sends it as; null when that format does not send it. */
  radarrEventType: string | null
  /** About no film (health, test): sent whatever the tags. */
  ignoresTags: boolean
  /** Whether the Discord format posts a message for it. */
  sentToDiscord: boolean
}

export type WebhookTestResult = { statusCode: number | null; elapsedMs: number }

export type WebhookDeliveryState = 'pending' | 'delivered' | 'givenUp'

export type WebhookDelivery = {
  id: number
  eventId: number
  eventType: string
  state: WebhookDeliveryState
  attempts: number
  nextAttemptAt: string | null
  lastAttemptAt: string | null
  deliveredAt: string | null
  lastStatusCode: number | null
  lastError: string | null
  createdAt: string
}

export const notifications = {
  list: () => api<NotificationEndpoint[]>('/api/v1/notifications'),
  get: (id: number) => api<NotificationEndpoint>(`/api/v1/notifications/${id}`),
  add: (body: NotificationEndpointRequest) => post<NotificationEndpoint>('/api/v1/notifications', body),
  update: (id: number, body: NotificationEndpointRequest) => put<NotificationEndpoint>(`/api/v1/notifications/${id}`, body),
  remove: (id: number) => del(`/api/v1/notifications/${id}`),
  /** Sends a test event to an unsaved configuration: what the form calls. */
  test: (body: NotificationEndpointRequest) => post<WebhookTestResult>('/api/v1/notifications/test', body),
  testSaved: (id: number) => post<WebhookTestResult>(`/api/v1/notifications/${id}/test`),
  deliveries: (id: number, limit = 50) => api<WebhookDelivery[]>(`/api/v1/notifications/${id}/deliveries?limit=${limit}`),
  /** Makes a delivery due now, given up or not; the attempt counter starts over. */
  retryDelivery: (deliveryId: number) => post<WebhookDelivery>(`/api/v1/notifications/deliveries/${deliveryId}/retry`),
  eventTypes: () => api<NotificationEventType[]>('/api/v1/notifications/events'),
}

// ---- tasks & health --------------------------------------------------------

export type ScheduledTask = {
  name: string
  displayName: string
  intervalMinutes: number
  enabled: boolean
  running: boolean
  lastRunAt: string | null
  lastDurationMs: number | null
  lastSucceeded: boolean | null
  lastSummary: string | null
  lastError: string | null
  nextRunAt: string | null
}

export type TaskRunResult = { name: string; success: boolean; durationMs: number; summary: string | null; error: string | null; ranAt: string }

export const tasks = {
  list: () => api<ScheduledTask[]>('/api/v1/system/tasks'),
  /** Runs the job now and waits for it. */
  run: (name: string) => post<TaskRunResult>(`/api/v1/system/tasks/${encodeURIComponent(name)}/run`),
}

export type HealthIssueType = 'notice' | 'warning' | 'error'

export type HealthIssue = { source: string; type: HealthIssueType; message: string }

export type Health = { checkedAt: string | null; issues: HealthIssue[] }

export const health = {
  get: () => api<Health>('/api/v1/health'),
  check: () => post<Health>('/api/v1/health/check'),
}

// ---- events ----------------------------------------------------------------

/** text/event-stream of every dispatched event: the event name is the type, the data the JSON payload. */
export const eventsUrl = '/api/v1/events'

// ---- migration (import from Radarr) ----------------------------------------

export type RadarrImportPreviewRequest = { url: string; apiKey: string }

export type RadarrProfilePreview = { id: number; name: string; movies: number; suggestedProfileId: number | null }

/** `reachable`: the Radarr path exists on this host as is, no path translation needed. */
export type RadarrRootFolderPreview = { path: string; movies: number; suggestedRootFolderId: number | null; reachable: boolean }

export type RadarrTagPreview = { id: number; label: string; movies: number }

export type RadarrImportPreview = {
  radarrVersion: string
  movies: number
  withFile: number
  monitored: number
  alreadyInLibrary: number
  exclusions: number
  historyRecords: number
  blocklist: number
  customFormats: number
  suggestedVersionName: string
  profiles: RadarrProfilePreview[]
  rootFolders: RadarrRootFolderPreview[]
  tags: RadarrTagPreview[]
}

export type RadarrImportRequest = {
  url: string
  apiKey: string
  versionName: string
  /** Radarr quality profile id → Bigarr quality profile id. */
  profileMap: Record<number, number>
  /** Radarr root folder path → Bigarr root folder id. */
  rootFolderMap: Record<string, number>
  importTags: boolean
  importExclusions: boolean
  importHistory: boolean
  importBlocklist: boolean
  importCustomFormats: boolean
  keepMonitored: boolean
  includeMoviesWithoutFile: boolean
  /** Shadow mode: keep following this Radarr with the same mapping once the import is done. */
  follow?: boolean
  /** Give each mapped profile the qualities, cutoff and upgrade setting of its Radarr profile. */
  importProfileQualities?: boolean
}

export type RadarrImportState = 'idle' | 'queued' | 'running' | 'completed' | 'failed'

/** `tmdbId` is null for a line about a custom format or a profile; `movieId` is set when the film is in Bigarr. */
export type RadarrImportIssue = { title: string; tmdbId: number | null; reason: string; movieId: number | null }

export type RadarrImportStatus = {
  state: RadarrImportState
  startedAt: string | null
  finishedAt: string | null
  total: number
  processed: number
  moviesAdded: number
  versionsAdded: number
  versionsUpdated: number
  filesAdded: number
  filesMissing: number
  skipped: number
  failed: number
  tagsAdded: number
  exclusionsAdded: number
  historyAdded: number
  blocklistAdded: number
  customFormatsAdded: number
  customFormatsUpdated: number
  profilesScored: number
  profileQualitiesCopied: number
  current: string | null
  error: string | null
  issues: RadarrImportIssue[]
}

export const migration = {
  preview: (body: RadarrImportPreviewRequest) => post<RadarrImportPreview>('/api/v1/migration/radarr/preview', body),
  /** Queues the import (202); 409 when one is already running. */
  start: (body: RadarrImportRequest) => post<RadarrImportStatus>('/api/v1/migration/radarr/import', body),
  status: () => api<RadarrImportStatus>('/api/v1/migration/radarr/status'),
}

// ---- shadow mode: followed Radarrs -------------------------------------------

/** A Radarr Bigarr follows in shadow mode. The key comes back masked (`********`); sending the mask keeps the stored one. */
export type FollowedRadarr = {
  id: number
  name: string
  url: string
  apiKey: string
  versionName: string
  profileMap: Record<number, number>
  rootFolderMap: Record<string, number>
  importTags: boolean
  keepMonitored: boolean
  includeMoviesWithoutFile: boolean
  enabled: boolean
  /** The next pass reads Radarr's history from here (a few minutes earlier, to be safe). */
  historyCheckpoint: string
  lastSyncAt: string | null
  lastSummary: string | null
  lastError: string | null
  lastErrorAt: string | null
  /** The last call of Radarr's webhook and its eventType (MovieAdded, Grab, Download, Test…). */
  lastWebhookAt: string | null
  lastWebhookEvent: string | null
  grabs: number
  inFlight: number
  createdAt: string
}

export type FollowedRadarrRequest = {
  name: string
  url: string
  /** Blank or `********` keeps the stored key on update. */
  apiKey: string | null
  versionName: string
  profileMap: Record<number, number>
  rootFolderMap: Record<string, number>
  importTags: boolean
  keepMonitored: boolean
  includeMoviesWithoutFile: boolean
  enabled: boolean
}

export type FollowedRadarrTest = { appName: string | null; version: string | null; movies: number }

export type RadarrFollowPass = { followedRadarrId: number; name: string; success: boolean; summary: string; grabs: number; resolved: number; picksShown: number; searchedOnAdd: number }

export type ShadowStatus = { enabled: boolean; picks: number; lastPickAt: string | null; radarrGrabs: number; sightings: number; radarrs: FollowedRadarr[] }

export const KEY_MASK = '********'

// ---- shadow mode: the comparison ---------------------------------------------

export type CompareOutcome = 'same' | 'different' | 'bigarrOnly' | 'radarrOnly'

/** Bigarr's parser and engine (RSS rules) on a release Radarr grabbed, for that version, when the grab was read. */
export type BigarrVerdict = {
  /** Set when the release would not even reach the engine: not this film, unparsable, film not in Bigarr yet. */
  unmatched: string | null
  approved: boolean
  rejections: string[]
  customFormatScore: number
  customFormats: string[]
  quality: string | null
  attributedVersion: string | null
  currentQuality: string | null
  currentScore: number | null
  /** The version's "file" then was Bigarr's own latest pick: the file Bigarr would have had. */
  currentIsPick: boolean
}

/** Radarr's parse of a release Bigarr picked, and the film's state in Radarr then. */
export type RadarrView = {
  /** TMDB id of the film Radarr maps the title to; null when it maps it to none. */
  tmdbId: number | null
  quality: string | null
  customFormats: string[]
  customFormatScore: number
  monitored: boolean | null
  fileQuality: string | null
  fileScore: number | null
  error: string | null
}

export type SightingVerdict = { versionName: string; approved: boolean; rejections: string[]; customFormatScore: number }

export type ComparePick = {
  id: number
  pickedAt: string
  lastPickedAt: string
  picks: number
  trigger: string
  title: string
  indexer: string | null
  size: number
  quality: string | null
  releaseGroup: string | null
  languages: string[]
  customFormats: string[]
  customFormatScore: number
  publishDate: string | null
  radarr: RadarrView | null
}

export type RadarrGrabState = 'grabbed' | 'imported' | 'failed' | 'ignored'

export type CompareGrab = {
  id: number
  radarrName: string
  grabbedAt: string
  title: string
  indexer: string | null
  size: number
  quality: string | null
  releaseGroup: string | null
  languages: string[]
  customFormats: string[]
  customFormatScore: number | null
  publishedAt: string | null
  /** Radarr's releaseSource: Rss, Search, UserInvokedSearch, InteractiveSearch, ReleasePush. */
  releaseSource: string | null
  state: RadarrGrabState
  resolvedAt: string | null
  bigarr: BigarrVerdict | null
}

export type CompareSighting = {
  seenAt: string
  indexer: string
  title: string
  matched: boolean
  unmatchedReason: string | null
  attributedVersion: string | null
  verdict: SightingVerdict | null
}

export type CompareRow = {
  key: string
  outcome: CompareOutcome
  tmdbId: number
  movieId: number | null
  movieTitle: string
  year: number | null
  posterUrl: string | null
  versionName: string
  at: string
  radarr: CompareGrab | null
  bigarr: ComparePick | null
  /** Other picks made while Radarr's download was in flight. */
  alsoPicked: ComparePick[]
  /** Positive when Bigarr picked first, in seconds. */
  bigarrLeadSeconds: number | null
  /** Radarr-only rows: what Bigarr's feeds saw of Radarr's release; null means never seen. */
  sighting: CompareSighting | null
}

export type CompareCounts = { same: number; different: number; bigarrOnly: number; radarrOnly: number; total: number }

export type CompareResult = {
  from: string | null
  to: string
  windowHours: number
  shadowEnabled: boolean
  versions: string[]
  summary: CompareCounts
  byVersion: { versionName: string; counts: CompareCounts }[]
  rows: CompareRow[]
  total: number
  page: number
  pageSize: number
}

/** `tmdbId`: one film only (the film page). `days: 0` means all time. */
export type CompareQuery = { version?: string; outcome?: CompareOutcome; days: number; windowHours: number; page: number; pageSize: number; tmdbId?: number }

export const shadow = {
  status: () => api<ShadowStatus>('/api/v1/shadow'),
  radarrs: {
    list: () => api<FollowedRadarr[]>('/api/v1/shadow/radarrs'),
    add: (body: FollowedRadarrRequest) => post<FollowedRadarr>('/api/v1/shadow/radarrs', body),
    update: (id: number, body: FollowedRadarrRequest) => put<FollowedRadarr>(`/api/v1/shadow/radarrs/${id}`, body),
    remove: (id: number) => del(`/api/v1/shadow/radarrs/${id}`),
    /** An unsaved form. */
    test: (body: FollowedRadarrRequest) => post<FollowedRadarrTest>('/api/v1/shadow/radarrs/test', body),
    /** A saved Radarr; the form's URL and key win when given, a masked key means the stored one. */
    testSaved: (id: number, body?: FollowedRadarrRequest) => post<FollowedRadarrTest>(`/api/v1/shadow/radarrs/${id}/test`, body ?? null),
    /** The import's mapping data for a saved Radarr, read with its stored key. */
    preview: (id: number) => post<RadarrImportPreview>(`/api/v1/shadow/radarrs/${id}/preview`),
    sync: (id: number) => post<RadarrFollowPass>(`/api/v1/shadow/radarrs/${id}/sync`),
    /** The path Radarr's webhook calls (Radarr → Settings → Connect → Webhook), with a Bigarr API key as `?apikey=`. */
    webhookPath: (id: number) => `/api/v1/shadow/radarrs/${id}/webhook`,
  },
  /** Radarr's grabs next to Bigarr's picks; the summary ignores the outcome filter. */
  compare: (query: CompareQuery) => {
    const params = new URLSearchParams({ days: String(query.days), windowHours: String(query.windowHours), page: String(query.page), pageSize: String(query.pageSize) })
    if (query.version) params.set('version', query.version)
    if (query.outcome) params.set('outcome', query.outcome)
    if (query.tmdbId) params.set('tmdbId', String(query.tmdbId))
    return api<CompareResult>(`/api/v1/shadow/compare?${params}`)
  },
}

// ---- media servers ----------------------------------------------------------

export type MediaServerKind = 'plex' | 'jellyfin' | 'emby'

export type PathMapping = { from: string; to: string }

export type MediaServer = {
  id: number
  name: string
  kind: MediaServerKind
  url: string
  token: string
  enabled: boolean
  pathMappings: PathMapping[]
  lastRefreshAt: string | null
  lastError: string | null
  lastErrorAt: string | null
  isFailing: boolean
  createdAt: string
}

export type MediaServerRequest = { name: string; kind: MediaServerKind; url: string; token: string; enabled: boolean; pathMappings: PathMapping[] }

export type MediaServerTestResult = { name: string | null; version: string | null; movieLibraries: number }

export const mediaServers = {
  list: () => api<MediaServer[]>('/api/v1/mediaservers'),
  get: (id: number) => api<MediaServer>(`/api/v1/mediaservers/${id}`),
  add: (body: MediaServerRequest) => post<MediaServer>('/api/v1/mediaservers', body),
  update: (id: number, body: MediaServerRequest) => put<MediaServer>(`/api/v1/mediaservers/${id}`, body),
  remove: (id: number) => del(`/api/v1/mediaservers/${id}`),
  /** Tests an unsaved configuration: what the form calls. */
  test: (body: MediaServerRequest) => post<MediaServerTestResult>('/api/v1/mediaservers/test', body),
  testSaved: (id: number) => post<MediaServerTestResult>(`/api/v1/mediaservers/${id}/test`),
  /** A full scan of the server's movie libraries, now. */
  refresh: (id: number) => post<MediaServer>(`/api/v1/mediaservers/${id}/refresh`),
}

// ---- playback ---------------------------------------------------------------

export type MediaItemKind = 'movie' | 'episode'
export type PlaybackState = 'playing' | 'paused' | 'buffering'
export type StreamDecision = 'directPlay' | 'directStream' | 'transcode'
export type MinimumPlayUnit = 'seconds' | 'percent'

export type PlaybackUserRef = { id: number; name: string; thumbUrl: string | null }

export type PlaybackItemRef = {
  id: number
  kind: MediaItemKind
  title: string
  year: number | null
  showTitle: string | null
  seasonNumber: number | null
  episodeNumber: number | null
  durationMs: number | null
  libraryTitle: string | null
  movieId: number | null
}

/** What the media server says about a stream: every field may be missing. */
export type StreamDetails = {
  playerTitle: string | null
  playerProduct: string | null
  playerPlatform: string | null
  playerPlatformVersion: string | null
  playerDevice: string | null
  playerId: string | null
  ipAddress: string | null
  local: boolean | null
  secure: boolean | null
  relayed: boolean | null
  bandwidth: number | null
  decision: StreamDecision | null
  videoDecision: string | null
  audioDecision: string | null
  subtitleDecision: string | null
  hardwareTranscode: boolean | null
  sourceContainer: string | null
  sourceVideoResolution: string | null
  sourceVideoCodec: string | null
  sourceDynamicRange: string | null
  sourceAudioCodec: string | null
  sourceAudioChannels: number | null
  sourceBitrate: number | null
  streamContainer: string | null
  streamVideoResolution: string | null
  streamVideoCodec: string | null
  streamAudioCodec: string | null
  streamAudioChannels: number | null
  streamBitrate: number | null
  serverFilePath: string | null
}

/** How a live transcode keeps up: speed 1 = real time; throttled = far enough ahead that Plex slows down (good). */
export type PlexTranscode = {
  speed: number | null
  throttled: boolean | null
  bufferedMs: number | null
  hardwareDecoding: string | null
  hardwareEncoding: string | null
  protocol: string | null
  complete: boolean | null
}

export type PlaybackSession = {
  id: number
  serverId: number
  sessionKey: string
  user: PlaybackUserRef
  item: PlaybackItemRef
  state: PlaybackState
  viewOffsetMs: number
  startedAt: string
  pausedSeconds: number
  versionId: number | null
  versionName: string | null
  details: StreamDetails
  transcode: PlexTranscode | null
  /** The film's images when Bigarr manages it. */
  posterUrl: string | null
  fanartUrl: string | null
}

export type PlaybackServerStatus = { id: number; name: string; connected: boolean; error: string | null; since: string | null }

export type NowPlaying = { servers: PlaybackServerStatus[]; sessions: PlaybackSession[] }

export type Play = {
  id: number
  user: PlaybackUserRef
  item: PlaybackItemRef
  startedAt: string
  stoppedAt: string
  playedSeconds: number
  pausedSeconds: number
  maxOffsetMs: number
  durationMs: number | null
  watched: boolean
  source: 'live' | 'serverHistory' | 'tautulli'
  movieId: number | null
  versionId: number | null
  versionName: string | null
  streams: number
  decision: StreamDecision | null
  player: string | null
  platform: string | null
  local: boolean | null
}

export type PlaysPage = { page: number; pageSize: number; totalRecords: number; records: Play[] }

export type PlaybackUser = { id: number; name: string; thumbUrl: string | null; isOwner: boolean; plays: number; lastSeenAt: string }

export type VersionPlays = { versionId: number | null; versionName: string | null; plays: number; users: number; transcodes: number; lastPlayedAt: string | null }

export type PlaybackSettings = { minimumPlay: number; minimumPlayUnit: MinimumPlayUnit; watchedPercent: number }

export type PlaySortKey = 'startedAt' | 'user' | 'title' | 'version' | 'played'

export type PlaysQuery = {
  page?: number
  pageSize?: number
  userId?: number
  movieId?: number
  versionId?: number
  kind?: MediaItemKind
  sort?: PlaySortKey
  direction?: 'asc' | 'desc'
}

/** One session of a play, with everything the server said about it. */
export type PlayStream = {
  id: number
  startedAt: string
  stoppedAt: string
  startOffsetMs: number
  endOffsetMs: number
  pausedSeconds: number
  filePath: string | null
  versionId: number | null
  versionName: string | null
  details: StreamDetails
}

export const playback = {
  nowPlaying: () => api<NowPlaying>('/api/v1/playback/sessions'),
  plays: (query: PlaysQuery = {}) => {
    const params = new URLSearchParams()
    for (const [key, value] of Object.entries(query)) if (value !== undefined) params.set(key, String(value))
    return api<PlaysPage>(`/api/v1/playback/plays?${params}`)
  },
  streams: (playId: number) => api<PlayStream[]>(`/api/v1/playback/plays/${playId}/streams`),
  users: () => api<PlaybackUser[]>('/api/v1/playback/users'),
  moviePlays: (movieId: number) => api<VersionPlays[]>(`/api/v1/playback/movies/${movieId}`),
  settings: () => api<PlaybackSettings>('/api/v1/playback/settings'),
  updateSettings: (body: PlaybackSettings) => put<PlaybackSettings>('/api/v1/playback/settings', body),
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

// ---- series ----------------------------------------------------------------

export type SeriesType = 'standard' | 'anime' | 'daily'
export type SeriesStatus = 'upcoming' | 'continuing' | 'ended' | 'deleted'
export type MonitoringOption = 'all' | 'future' | 'missing' | 'existing' | 'pilot' | 'firstSeason' | 'latestSeason' | 'none'
export type NewItemMonitoring = 'all' | 'none'

/** Per version: monitored and aired (or with a file), files, monitored, every episode outside the specials, bytes. */
export type EpisodeStatistics = {
  episodeCount: number
  episodeFileCount: number
  monitoredEpisodeCount: number
  totalEpisodeCount: number
  sizeOnDisk: number
}

export type SeriesLookupResult = {
  tvdbId: number | null
  tmdbId: number | null
  imdbId: string | null
  title: string
  originalTitle: string | null
  year: number | null
  overview: string | null
  posterUrl: string | null
  status: SeriesStatus
  network: string | null
  suggestedType: SeriesType
  existingSeriesId: number | null
}

export type SeriesVersionSummary = {
  id: number
  name: string
  isDefault: boolean
  monitored: boolean
  qualityProfileId: number
  qualityProfile: string
  statistics: EpisodeStatistics
}

export type SeriesSummary = {
  id: number
  tvdbId: number | null
  tmdbId: number | null
  title: string
  sortTitle: string
  year: number | null
  status: SeriesStatus
  network: string | null
  seriesType: SeriesType
  posterUrl: string | null
  addedAt: string
  monitored: boolean
  seasonCount: number
  nextAiring: string | null
  versions: SeriesVersionSummary[]
}

export type SeriesVersion = {
  id: number
  name: string
  mode: VersionMode
  qualityProfileId: number
  qualityProfile: string
  isDefault: boolean
  monitored: boolean
  seasonFolder: boolean
  monitorNewItems: NewItemMonitoring
  rootFolderId: number
  rootFolderPath: string
  folderName: string
  path: string
  createdAt: string
  tags: number[]
  statistics: EpisodeStatistics
  constraints: Constraints | null
}

export type SeasonVersionState = { versionId: number; monitored: boolean; statistics: EpisodeStatistics }

export type Season = {
  seasonNumber: number
  title: string | null
  overview: string | null
  posterUrl: string | null
  episodeCount: number
  airedEpisodeCount: number
  versions: SeasonVersionState[]
}

export type Series = {
  id: number
  tvdbId: number | null
  tmdbId: number | null
  imdbId: string | null
  title: string
  originalTitle: string | null
  sortTitle: string
  year: number | null
  overview: string | null
  status: SeriesStatus
  network: string | null
  runtime: number | null
  airTime: string | null
  originalCountry: string | null
  originalLanguage: string | null
  seriesType: SeriesType
  ordering: 'tvdb'
  genres: string[]
  alternativeTitles: { title: string; language: string | null }[]
  posterUrl: string | null
  fanartUrl: string | null
  firstAired: string | null
  lastAired: string | null
  defaultRootFolderId: number
  addedAt: string
  lastMetadataRefreshAt: string | null
  monitored: boolean
  seasons: Season[]
  versions: SeriesVersion[]
}

export type EpisodeVersionState = { versionId: number; targetId: number; monitored: boolean; hasFile: boolean; episodeFileId: number | null }

export type Episode = {
  /** Shared by every version of the series. */
  id: number
  tvdbId: number | null
  tmdbId: number | null
  seasonNumber: number
  episodeNumber: number
  absoluteEpisodeNumber: number | null
  airDate: string | null
  airDateUtc: string | null
  title: string
  overview: string | null
  runtime: number | null
  imageUrl: string | null
  isSpecial: boolean
  hasAired: boolean
  versions: EpisodeVersionState[]
}

export type AddSeriesRequest = {
  tvdbId: number | null
  tmdbId: number | null
  qualityProfileId: number
  rootFolderId: number
  seriesType?: SeriesType
  monitor: MonitoringOption
  seasonFolder: boolean
  monitorNewItems?: NewItemMonitoring
  versionName?: string | null
  monitored?: boolean
  tagIds?: number[]
}

export type AddSeriesVersionRequest = {
  name: string
  qualityProfileId: number
  rootFolderId?: number | null
  folderName?: string | null
  monitor: MonitoringOption
  seasonFolder?: boolean
  monitorNewItems?: NewItemMonitoring
  monitored?: boolean
}

export type UpdateSeriesVersionRequest = {
  name?: string | null
  qualityProfileId?: number | null
  monitored?: boolean | null
  seasonFolder?: boolean | null
  monitorNewItems?: NewItemMonitoring | null
  tagIds?: number[] | null
  constraints?: Constraints | null
  clearConstraints?: boolean
}

export const monitoringOptions: MonitoringOption[] = ['all', 'future', 'missing', 'existing', 'pilot', 'firstSeason', 'latestSeason', 'none']
export const seriesTypes: SeriesType[] = ['standard', 'anime', 'daily']

export type UnmatchedFileReason = 'notParsable' | 'episodeNotFound' | 'ambiguous' | 'wrongSeriesType' | 'duplicate' | 'notAnEpisode'

export type EpisodeFile = {
  id: number
  versionId: number
  relativePath: string
  path: string
  seasonNumber: number
  size: number
  dateAdded: string
  qualityId: number
  quality: string
  revisionVersion: number
  isRepack: boolean
  releaseGroup: string | null
  languages: string[]
  originalReleaseTitle: string | null
  mediaInfo: MediaInfo | null
  episodeIds: number[]
  /** "S01E01", "S01E02-E03". */
  episodes: string
}

export type UnmatchedEpisodeFile = {
  id: number
  versionId: number
  relativePath: string
  path: string
  size: number
  reason: UnmatchedFileReason
  detail: string | null
  discoveredAt: string
  lastSeenAt: string
}

export type SeriesFiles = { files: EpisodeFile[]; unmatched: UnmatchedEpisodeFile[] }

export type MapSeriesFolderRequest = { seriesId?: number | null; tvdbId?: number | null; tmdbId?: number | null; qualityProfileId?: number | null }

export const series = {
  list: () => api<SeriesSummary[]>('/api/v1/series'),
  get: (id: number) => api<Series>(`/api/v1/series/${id}`),
  episodes: (id: number) => api<Episode[]>(`/api/v1/series/${id}/episodes`),
  lookup: (term: string) => api<SeriesLookupResult[]>(`/api/v1/series/lookup?term=${encodeURIComponent(term)}`),
  add: (body: AddSeriesRequest) => post<Series>('/api/v1/series', body),
  update: (id: number, body: { seriesType?: SeriesType }) => put<Series>(`/api/v1/series/${id}`, body),
  remove: (id: number) => del(`/api/v1/series/${id}`),
  refresh: (id: number) => post<Series>(`/api/v1/series/${id}/refresh`),
  files: (id: number) => api<SeriesFiles>(`/api/v1/series/${id}/files`),
  rescan: (id: number) => post<ScanStatus>(`/api/v1/series/${id}/rescan`),
  rescanStatus: (id: number) => api<ScanStatus>(`/api/v1/series/${id}/rescan`),
  rescanAll: () => post<ScanStatus[]>('/api/v1/series/rescan'),
  mapFolder: (unmappedId: number, body: MapSeriesFolderRequest) =>
    post<{ seriesId: number; versionId: number; scan: ScanStatus }>(`/api/v1/series/unmapped/${unmappedId}/map`, body),
  addVersion: (id: number, body: AddSeriesVersionRequest) => post<SeriesVersion>(`/api/v1/series/${id}/versions`, body),
  updateVersion: (id: number, versionId: number, body: UpdateSeriesVersionRequest) => put<SeriesVersion>(`/api/v1/series/${id}/versions/${versionId}`, body),
  deleteVersion: (id: number, versionId: number, promote?: number) =>
    del(`/api/v1/series/${id}/versions/${versionId}${promote ? `?promote=${promote}` : ''}`),
  promote: (id: number, versionId: number) => post<SeriesVersion>(`/api/v1/series/${id}/versions/${versionId}/promote`),
  applyMonitoring: (id: number, versionId: number, monitor: MonitoringOption) =>
    post<Series>(`/api/v1/series/${id}/versions/${versionId}/monitoring`, { monitor }),
  setSeason: (id: number, versionId: number, seasonNumber: number, monitored: boolean) =>
    put<void>(`/api/v1/series/${id}/versions/${versionId}/seasons/${seasonNumber}`, { monitored }),
  setEpisodes: (id: number, versionId: number, episodeIds: number[], monitored: boolean) =>
    put<void>(`/api/v1/series/${id}/versions/${versionId}/episodes`, { episodeIds, monitored }),
}
