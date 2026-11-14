import type { components } from './generated/schema'

/** The API's contracts, generated from its OpenAPI document (bun run gen:api). */
type Schemas = components['schemas']

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

export type Me = Schemas['MeResponse']

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
export type ApiKey = Schemas['ApiKeyResponse']

/** The only response that carries the key itself. */
export type ApiKeyCreated = Schemas['ApiKeyCreatedResponse']

export const apiKeys = {
  list: () => api<ApiKey[]>('/api/v1/apikeys'),
  create: (name: string, compatView: string) => post<ApiKeyCreated>('/api/v1/apikeys', { name, compatView }),
  revoke: (id: number) => del(`/api/v1/apikeys/${id}`),
}

// ---- backups ---------------------------------------------------------------

export type Backup = { name: string; size: number; createdAt: string; type: 'manual' | 'scheduled' }

export type RestoreResult = Schemas['RestoreResponse']

export const backups = {
  list: () => api<Backup[]>('/api/v1/system/backup'),
  create: () => post<Backup>('/api/v1/system/backup'),
  remove: (name: string) => del(`/api/v1/system/backup/${encodeURIComponent(name)}`),
  /** Stages the database and restarts Bigarr; the page must expect the connection to drop. */
  restore: (name: string) => post<RestoreResult>(`/api/v1/system/backup/${encodeURIComponent(name)}/restore`),
  downloadUrl: (name: string) => `/api/v1/system/backup/${encodeURIComponent(name)}/download`,
}

// ---- movies ----------------------------------------------------------------

export type VersionMode = Schemas['VersionMode']
export type MinimumAvailability = Schemas['MinimumAvailability']
export type VersionStatus = Schemas['VersionStatus']
export type MovieStatus = Schemas['MovieStatus']

export type Constraints = Schemas['ConstraintsDto']

/** What ffprobe read from the file; null until the import or the backfill probed it. */
export type MediaInfo = Schemas['MediaInfo']

export type MediaFile = Schemas['MediaFileResponse']

export type ExtraFileKind = Schemas['ExtraFileKind']

export type ExtraFile = Schemas['ExtraFileResponse']

export type Version = Schemas['VersionResponse']

export type Movie = Schemas['MovieResponse']

export type VersionSummary = Schemas['VersionSummary']

export type MovieSummary = Schemas['MovieSummary']

export type MovieLookupResult = Schemas['MovieLookupResult']

export type AddMovieRequest = Schemas['AddMovieRequest']

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
export type BulkUpdateRequest = Schemas['BulkUpdateRequest']

export type BulkOutcome = Schemas['BulkOutcome']

export type BulkMovieResult = Schemas['BulkMovieResult']

export type BulkResponse = Schemas['BulkResponse']

export type BulkDeleteRequest = Schemas['BulkDeleteRequest']

export type BulkDeleteResponse = Schemas['BulkDeleteResponse']

// ---- tags ------------------------------------------------------------------

export type Tag = Schemas['TagResponse']

export const tags = {
  list: () => api<Tag[]>('/api/v1/tags'),
  /** Idempotent by label: an existing tag comes back as is. */
  add: (label: string) => post<Tag>('/api/v1/tags', { label }),
}

// ---- quality ---------------------------------------------------------------

/** Which versions may use a profile: film ones, series ones, or both. */
export type ProfileKind = Schemas['ProfileKind']

export type QualityProfile = Schemas['QualityProfileResponse']

/** The profiles a film (`movies`) or series (`series`) version can take: its own kind and Both. */
export const profilesFor = (kind: 'movies' | 'series') => (profiles: QualityProfile[]) => profiles.filter((p) => p.kind === kind || p.kind === 'both')

export type QualityDefinition = Schemas['QualityDefinitionResponse']

/** `kind` says whose sizes these are: the films' (default) or the series'. */
export type QualityDefinitionUpdate = Schemas['QualityDefinitionUpdateRequest']

/** One entry of the ordered items list, worst first: the position is the rank. */
export type QualityProfileItem = Schemas['QualityProfileItemDto']

export type FormatScore = Schemas['FormatScoreDto']

export type QualityProfileDetail = Schemas['QualityProfileResponse']

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
  kind: ProfileKind
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

export type CustomFormatSpecification = Schemas['CustomFormatSpecificationDto']

export type CustomFormat = Schemas['CustomFormatResponse']

/** `scores` is the full list: a profile left out or at zero loses its score. */
export type CustomFormatRequest = Schemas['CustomFormatRequest']

export type SpecificationFieldType = 'regex' | 'number' | 'select' | 'boolean'

export type SpecificationFieldSchema = Schemas['SpecificationFieldSchemaDto']

export type SpecificationSchema = Schemas['SpecificationSchemaDto']

export type CustomFormatSchema = Schemas['CustomFormatSchemaResponse']

export type CustomFormatImportResult = Schemas['CustomFormatImportResponse']

export type CustomFormatEvaluation = Schemas['CustomFormatEvaluationResponse']

export type CustomFormatTestRequest = Schemas['CustomFormatTestRequest']

export type CustomFormatTest = Schemas['CustomFormatTestResponse']

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
export type MediaKind = Schemas['MediaKind']

export type RootFolder = Schemas['RootFolderResponse']

/** For `useQuery({ select })`: the roots of one kind. */
export const rootsOfKind = (kind: MediaKind) => (roots: RootFolder[]) => roots.filter((r) => r.kind === kind)

export type UnmappedReason = Schemas['UnmappedReason']

export type UnmappedFolder = Schemas['UnmappedFolderResponse']

export type ScanState = Schemas['ScanState']

export type ScanStatus = Schemas['ScanStatusResponse']

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

export type FileSystemItem = Schemas['FileSystemItem']

/** One folder as the path picker reads it. `path` is empty and `parent` null when the mount points come back. */
export type FolderListing = Schemas['FolderListing']

export const filesystem = {
  /** A path that does not exist yet answers with its deepest existing ancestor, so a half-typed path still suggests. */
  browse: (path: string, includeFiles = false) => api<FolderListing>(`/api/v1/filesystem?path=${encodeURIComponent(path)}&includeFiles=${includeFiles}`),
}

// ---- indexers --------------------------------------------------------------

export type IndexerImplementation = Schemas['IndexerImplementation']

export type IndexerStatus = Schemas['IndexerStatusResponse']

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

export type IndexerTestResult = Schemas['IndexerTestResponse']

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

export type Rejection = Schemas['RejectionResponse']

export type VersionDecision = Schemas['VersionDecisionResponse']

export type IndexerFailureKind = Schemas['IndexerFailureKind']

export type Release = Schemas['ReleaseResponse']

export type SearchResult = Schemas['SearchResponse']

/** One request sent to an indexer during an interactive search (advanced view), its URL redacted by the server. */
export type IndexerQueryRequest = Schemas['IndexerRequestResponse']

/** What one indexer was asked during an interactive search, and how many of its releases parsed, matched and were accepted. */
export type IndexerQueryReport = Schemas['IndexerQueryReport']

/** `picked`: shadow mode recorded the best approved release instead of grabbing it. */
export type AutomaticSearchOutcome = Schemas['AutomaticSearchOutcome']

/** One version's outcome of an automatic search: the best approved release was grabbed, or here is why not. */
export type AutomaticSearchResult = Schemas['AutomaticSearchResult']

export const search = {
  /** Interactive search for the film, judged by every version (or one); `query` replaces its ids and titles with a text. */
  movie: (movieId: number, options: { versionId?: number; query?: string; includeQueries?: boolean } = {}) => {
    const params = new URLSearchParams({ movieId: String(movieId) })
    if (options.versionId) params.set('versionId', String(options.versionId))
    if (options.query?.trim()) params.set('query', options.query.trim())
    if (options.includeQueries) params.set('includeQueries', 'true')
    return api<SearchResult>(`/api/v1/search?${params}`)
  },
  /** Interactive search for a series: an episode, a season (packs and singles together) or the whole series. */
  series: (seriesId: number, scope: { episodeId?: number; season?: number; orderSeason?: boolean }, options: { versionId?: number; query?: string; includeQueries?: boolean } = {}) => {
    const params = new URLSearchParams()
    if (scope.episodeId) params.set('episodeId', String(scope.episodeId))
    if (scope.season !== undefined) params.set('season', String(scope.season))
    if (scope.orderSeason) params.set('orderSeason', 'true')
    if (options.versionId) params.set('versionId', String(options.versionId))
    if (options.query?.trim()) params.set('query', options.query.trim())
    if (options.includeQueries) params.set('includeQueries', 'true')
    return api<SeriesSearchResult>(`/api/v1/series/${seriesId}/search?${params}`)
  },
  /** Automatic search on demand: every version of the film (or one), the best approved release grabbed. */
  automatic: (movieId: number, versionId?: number) =>
    post<AutomaticSearchResult[]>(`/api/v1/movies/${movieId}/search${versionId ? `?versionId=${versionId}` : ''}`),
}

export type SeriesEpisodeRef = Schemas['SeriesEpisodeRef']

/** One series version's verdict over every episode the release covers. */
export type SeriesVersionDecision = Schemas['SeriesVersionDecisionResponse']

export type SeriesRelease = Schemas['SeriesReleaseResponse']

/** Which numbering a release matched its episodes on. */
export type EpisodeNumbering = Schemas['EpisodeNumbering']

export type SeriesSearchResult = Schemas['SeriesSearchResponse']

// ---- wanted ----------------------------------------------------------------

export type WantedFilter = Schemas['WantedFilter']

export type WantedItem = Schemas['WantedItemResponse']

export type WantedPage = Schemas['WantedPageResponse']

export const wanted = {
  list: (filter?: WantedFilter, page = 1, pageSize = 50) =>
    api<WantedPage>(`/api/v1/wanted?page=${page}&pageSize=${pageSize}${filter ? `&filter=${filter}` : ''}`),
  /** Queues a search for everything on the list, backoff ignored; runs in the background one version at a time. */
  searchAll: (filter?: WantedFilter) => post<{ queued: number }>(`/api/v1/wanted/search${filter ? `?filter=${filter}` : ''}`),
}

// ---- rss -------------------------------------------------------------------

export type RssMovieRef = Schemas['RssMovieRef']

export type RssSeriesRef = Schemas['RssSeriesRef']

export type RssRelease = Schemas['RssReleaseResponse']

export type RssIndexerStatus = Schemas['RssIndexerStatusResponse']

export type RssStatus = Schemas['RssStatusResponse']

export type RssSyncResult = Schemas['RssSyncResponse']

export const rss = {
  status: () => api<RssStatus>('/api/v1/rss'),
  releases: (matched?: boolean, limit = 200) =>
    api<RssRelease[]>(`/api/v1/rss/releases?limit=${limit}${matched === undefined ? '' : `&matched=${matched}`}`),
  sync: () => post<RssSyncResult>('/api/v1/rss/sync'),
}

// ---- download clients ------------------------------------------------------

export type DownloadClientImplementation = Schemas['DownloadClientImplementation']

export type DownloadClient = Schemas['DownloadClientResponse']

export type DownloadClientRequest = Omit<DownloadClient, 'id' | 'protocol' | 'createdAt'>

export type DownloadClientTestResult = Schemas['DownloadClientTestResponse']

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

export type GrabState = Schemas['GrabState']

export type DownloadItemStatus = Schemas['DownloadItemStatus']

export type StatusSeverity = Schemas['StatusSeverity']

export type StatusMessage = Schemas['StatusMessage']

/** The release as the search returned it: the search keeps no state, so the client carries the links. */
export type GrabRequest = Schemas['GrabRequest']

/** A grab sent to a client; in shadow mode only `shadowPickId` and `grabbedAt` are set (`shadow` is true). */
export type GrabResult = Schemas['GrabResponse']

export type QueueItem = Schemas['QueueItemResponse']

export type QueueEpisode = Schemas['QueueEpisodeResponse']

export type QueueClientStatus = Schemas['QueueClientStatusResponse']

export type Queue = Schemas['QueueResponse']

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

export type TransferMode = Schemas['TransferMode']

export type NamingToken = Schemas['NamingTokenResponse']

/** Radarr's and Sonarr's "Colon Replacement" (Custom is Sonarr's alone). */
export type ColonReplacement = Schemas['ColonReplacement']

export const filmColonReplacements: ColonReplacement[] = ['delete', 'dash', 'spaceDash', 'spaceDashSpace', 'smart']

export const seriesColonReplacements: ColonReplacement[] = [...filmColonReplacements, 'custom']

export type MediaManagement = Schemas['MediaManagementResponse']

export type SeasonPackUpgrade = Schemas['SeasonPackUpgrade']

export type MediaManagementRequest = Pick<
  MediaManagement,
  | 'renameFiles'
  | 'fileNameTemplate'
  | 'folderNameTemplate'
  | 'transferMode'
  | 'recycleBinPath'
  | 'recycleBinRetentionDays'
  | 'importExtraFiles'
  | 'extraFileExtensions'
  | 'seasonPackUpgrade'
  | 'seasonPackUpgradeThreshold'
  | 'replaceIllegalCharacters'
  | 'colonReplacement'
>

export type NamingPreview = Schemas['NamingPreviewResponse']

export type BackfillResult = Schemas['BackfillResponse']

export const mediaManagement = {
  get: () => api<MediaManagement>('/api/v1/mediamanagement'),
  update: (body: MediaManagementRequest) => put<MediaManagement>('/api/v1/mediamanagement', body),
  /** A draft template rendered on the sample film (or on `sampleTitle`), with the errors that would refuse it. */
  preview: (
    fileNameTemplate: string | null,
    folderNameTemplate: string | null,
    cleaning?: { replaceIllegalCharacters: boolean; colonReplacement: ColonReplacement; sampleTitle?: string },
  ) => post<NamingPreview>('/api/v1/mediamanagement/naming/preview', { fileNameTemplate, folderNameTemplate, ...cleaning }),
  /** Probes a batch of files that have no media info yet. */
  backfill: () => post<BackfillResult>('/api/v1/mediamanagement/backfill'),
  seriesNaming: () => api<SeriesNaming>('/api/v1/mediamanagement/series-naming'),
  updateSeriesNaming: (body: SeriesNamingRequest) => put<SeriesNaming>('/api/v1/mediamanagement/series-naming', body),
  /** Draft series templates rendered on Sonarr's sample series, with the errors that would refuse each. */
  previewSeriesNaming: (body: Partial<SeriesNamingRequest> & { sampleTitle?: string }) => post<SeriesNamingPreview>('/api/v1/mediamanagement/series-naming/preview', body),
}

// ---- series naming (Sonarr's) ------------------------------------------------

export type MultiEpisodeStyle = Schemas['MultiEpisodeStyle']

export const multiEpisodeStyles: MultiEpisodeStyle[] = ['extend', 'duplicate', 'repeat', 'scene', 'range', 'prefixedRange']

export type SeriesTokenScope = Schemas['SeriesTokenScope']

export type SeriesNamingToken = Schemas['SeriesNamingTokenResponse']

export type SeriesNamingPreview = Schemas['SeriesNamingPreviewResponse']

export type SeriesNamingRequest = {
  renameEpisodes: boolean
  seriesFolderTemplate: string
  seasonFolderTemplate: string
  specialsFolderName: string
  standardEpisodeTemplate: string
  dailyEpisodeTemplate: string
  animeEpisodeTemplate: string
  multiEpisodeStyle: MultiEpisodeStyle
  /** Sonarr's "Replace Illegal Characters" for series names: on replaces them, off removes them. */
  replaceIllegalCharacters: boolean
  /** Sonarr's "Colon Replacement". */
  colonReplacement: ColonReplacement
  /** What a colon becomes in custom mode. */
  customColonReplacement: string
}

export type SeriesNaming = Schemas['SeriesNamingResponse']

// ---- manual import ---------------------------------------------------------

export type ImportVersionOption = Schemas['ImportVersionOption']

export type ManualImportFile = Schemas['ManualImportFileResponse']

export type ManualImportExtra = Schemas['ManualImportExtraResponse']

export type ManualImport = Schemas['ManualImportResponse']

/** `extraPaths`: the side files to bring, as listed; omitted lets the import find them, empty brings none. */
export type ManualImportItem = Schemas['ManualImportItemRequest']

export type ManualImportResult = Schemas['ManualImportResultResponse']

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

// ---- series manual import, rename, episode files ----------------------------

export type SeriesManualImportEpisode = Schemas['SeriesManualImportEpisode']

export type SeriesManualImportFile = Schemas['SeriesManualImportFileResponse']

export type SeriesManualImport = Schemas['SeriesManualImportResponse']

export type SeriesManualImportItem = Schemas['SeriesManualImportItemRequest']

export type SeriesManualImportResult = Schemas['SeriesManualImportResultResponse']

export type SeriesRenamePreview = Schemas['SeriesRenamePreviewResponse']

export type SeriesRenameResult = Schemas['SeriesRenameResultResponse']

export type EpisodeExtra = Schemas['EpisodeExtraResponse']

export type EpisodeFileDetail = Schemas['EpisodeFileDetailResponse']

export const seriesFiles = {
  /** The video files of a path or of a queued download, with the mapper's proposal. */
  list: (options: { path?: string; grabId?: number; seriesId?: number }) => {
    const params = new URLSearchParams()
    if (options.path) params.set('path', options.path)
    if (options.grabId) params.set('grabId', String(options.grabId))
    if (options.seriesId) params.set('seriesId', String(options.seriesId))
    return api<SeriesManualImport>(`/api/v1/manualimport/series?${params.toString()}`)
  },
  /** Forced: no upgrade check. */
  import: (files: SeriesManualImportItem[], transferMode?: TransferMode | null) =>
    post<SeriesManualImportResult[]>('/api/v1/manualimport/series', { files, transferMode: transferMode ?? null }),
  renamePreview: (seriesId: number, options: { versionId?: number; seasonNumber?: number } = {}) => {
    const params = new URLSearchParams()
    if (options.versionId) params.set('versionId', String(options.versionId))
    if (options.seasonNumber != null) params.set('seasonNumber', String(options.seasonNumber))
    return api<SeriesRenamePreview[]>(`/api/v1/series/${seriesId}/rename?${params.toString()}`)
  },
  rename: (seriesId: number, body: { versionId?: number | null; seasonNumber?: number | null; episodeFileIds?: number[] | null }) =>
    post<SeriesRenameResult[]>(`/api/v1/series/${seriesId}/rename`, { seriesId, ...body }),
  file: (seriesId: number, fileId: number) => api<EpisodeFileDetail>(`/api/v1/series/${seriesId}/episodefiles/${fileId}`),
  deleteFile: (seriesId: number, fileId: number) => del(`/api/v1/series/${seriesId}/episodefiles/${fileId}`),
  /** The manual fix of a file the series' order could not place: the episodes it holds, optionally renamed. */
  reassign: (seriesId: number, fileId: number, body: { episodeIds: number[]; rename?: boolean }) =>
    post<OrderFileResult>(`/api/v1/series/${seriesId}/episodefiles/${fileId}/reassign`, body),
  /** Attach a file the scan could not place to episodes, where it lies. */
  assign: (seriesId: number, unmatchedId: number, body: { episodeIds: number[]; rename?: boolean; replace?: boolean; qualityId?: number | null }) =>
    post<EpisodeFileDetail>(`/api/v1/series/${seriesId}/unmatched/${unmatchedId}/assign`, body),
}

// ---- rename ----------------------------------------------------------------

export type ExtraRenamePreview = Schemas['ExtraRenamePreviewResponse']

/** `extras` follow the file: renamed with it when it changes. */
export type RenamePreview = Schemas['RenamePreviewResponse']

export type RenameResult = Schemas['RenameResultResponse']

export const rename = {
  preview: (movieId: number) => api<RenamePreview[]>(`/api/v1/rename?movieId=${movieId}`),
  apply: (movieId: number, mediaFileIds?: number[]) => post<RenameResult[]>('/api/v1/rename', { movieId, mediaFileIds: mediaFileIds ?? null }),
}

// ---- history ---------------------------------------------------------------

export type HistoryEventType = Schemas['HistoryEventType']

export type HistoryEvent = Schemas['HistoryEventResponse']

export const history = {
  /** `episodeId`: every line about the episode, whatever the version. */
  list: (options: { movieId?: number; episodeId?: number; versionId?: number; eventType?: HistoryEventType; limit?: number } = {}) => {
    const params = new URLSearchParams()
    if (options.movieId) params.set('movieId', String(options.movieId))
    if (options.episodeId) params.set('episodeId', String(options.episodeId))
    if (options.versionId) params.set('versionId', String(options.versionId))
    if (options.eventType) params.set('eventType', options.eventType)
    if (options.limit) params.set('limit', String(options.limit))
    const query = params.toString()
    return api<HistoryEvent[]>(`/api/v1/history${query ? `?${query}` : ''}`)
  },
}

// ---- blocklist -------------------------------------------------------------

export type BlocklistEntry = Schemas['BlocklistEntryResponse']

export type BlocklistPage = Schemas['BlocklistPageResponse']

export const blocklist = {
  list: (options: { movieId?: number; seriesId?: number; page?: number; pageSize?: number } = {}) => {
    const params = new URLSearchParams()
    if (options.movieId) params.set('movieId', String(options.movieId))
    if (options.seriesId) params.set('seriesId', String(options.seriesId))
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
 * `sonarr`: Sonarr v4's webhook payloads for the series events, for tools written for Sonarr.
 * `discord`: embeds for a Discord channel webhook, laid out like Radarr's (films) and Sonarr's (series) Discord connections.
 */
export type WebhookFormat = Schemas['WebhookFormat']

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

export type DiscordGrabField = Schemas['DiscordGrabField']
export type DiscordImportField = Schemas['DiscordImportField']
export type DiscordManualInteractionField = Schemas['DiscordManualInteractionField']

/** Radarr's Discord settings. A missing field list takes Radarr's defaults; an empty one sends no field. */
export type DiscordSettings = Schemas['DiscordSettings']

export type NotificationEndpointRequest = Schemas['NotificationEndpointRequest']

export type NotificationEndpoint = Schemas['NotificationEndpointResponse']

export type NotificationEventType = Schemas['NotificationEventTypeResponse']

export type WebhookTestResult = Schemas['WebhookTestResponse']

export type WebhookDeliveryState = Schemas['WebhookDeliveryState']

export type WebhookDelivery = Schemas['WebhookDeliveryResponse']

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

export type ScheduledTask = Schemas['ScheduledTaskResponse']

export type TaskRunResult = Schemas['TaskRunResponse']

export const tasks = {
  list: () => api<ScheduledTask[]>('/api/v1/system/tasks'),
  /** Runs the job now and waits for it. */
  run: (name: string) => post<TaskRunResult>(`/api/v1/system/tasks/${encodeURIComponent(name)}/run`),
}

export type HealthIssueType = Schemas['HealthIssueType']

export type HealthIssue = Schemas['HealthIssue']

export type Health = Schemas['HealthResponse']

export const health = {
  get: () => api<Health>('/api/v1/health'),
  check: () => post<Health>('/api/v1/health/check'),
}

// ---- events ----------------------------------------------------------------

/** text/event-stream of every dispatched event: the event name is the type, the data the JSON payload. */
export const eventsUrl = '/api/v1/events'

// ---- migration (import from Radarr) ----------------------------------------

export type RadarrImportPreviewRequest = Schemas['RadarrImportPreviewRequest']

export type RadarrProfilePreview = Schemas['RadarrProfilePreview']

/** `reachable`: the Radarr path exists on this host as is, no path translation needed. */
export type RadarrRootFolderPreview = Schemas['RadarrRootFolderPreview']

export type RadarrTagPreview = Schemas['RadarrTagPreview']

export type RadarrImportPreview = Schemas['RadarrImportPreviewResponse']

export type RadarrImportRequest = Schemas['RadarrImportRequest']

export type RadarrImportState = Schemas['RadarrImportState']

/** `tmdbId` is null for a line about a custom format or a profile; `movieId` is set when the film is in Bigarr. */
export type RadarrImportIssue = Schemas['RadarrImportIssue']

export type RadarrImportStatus = Schemas['RadarrImportStatusResponse']

export const migration = {
  preview: (body: RadarrImportPreviewRequest) => post<RadarrImportPreview>('/api/v1/migration/radarr/preview', body),
  /** Queues the import (202); 409 when one is already running. */
  start: (body: RadarrImportRequest) => post<RadarrImportStatus>('/api/v1/migration/radarr/import', body),
  status: () => api<RadarrImportStatus>('/api/v1/migration/radarr/status'),
}

// ---- migration (import from Tautulli) ---------------------------------------

/** `path`: the copy of tautulli.db on the Bigarr host, typed or where an upload put it. */
export type TautulliImportPreviewRequest = Schemas['TautulliImportPreviewRequest']

export type TautulliImportPreview = Schemas['TautulliImportPreviewResponse']

export type TautulliImportRequest = Schemas['TautulliImportRequest']

export type TautulliImportIssue = Schemas['TautulliImportIssue']

export type TautulliImportStatus = Schemas['TautulliImportStatusResponse']

export type TautulliUpload = Schemas['TautulliUploadResponse']

/** Sends the raw file as the body, reporting the share sent so far (0–1): a Tautulli database runs to hundreds of megabytes. */
function uploadTautulli(file: File, onProgress: (share: number) => void): Promise<TautulliUpload> {
  return new Promise((resolve, reject) => {
    const request = new XMLHttpRequest()
    request.open('PUT', '/api/v1/migration/tautulli/upload')
    request.withCredentials = true
    request.setRequestHeader('Content-Type', 'application/octet-stream')
    request.upload.onprogress = (e) => {
      if (e.lengthComputable) onProgress(e.loaded / e.total)
    }
    request.onload = () => {
      let body: unknown
      try {
        body = JSON.parse(request.responseText)
      } catch {
        body = undefined
      }
      if (request.status >= 200 && request.status < 300) {
        resolve(body as TautulliUpload)
        return
      }
      const problem = body as Problem | undefined
      reject(new ApiError(request.status, problem?.detail ?? problem?.title ?? request.statusText, problem))
    }
    request.onerror = () => reject(new ApiError(0, 'Upload failed'))
    request.send(file)
  })
}

export const tautulliImport = {
  upload: uploadTautulli,
  preview: (body: TautulliImportPreviewRequest) => post<TautulliImportPreview>('/api/v1/migration/tautulli/preview', body),
  /** Queues the import (202); 409 when one is already running. */
  start: (body: TautulliImportRequest) => post<TautulliImportStatus>('/api/v1/migration/tautulli/import', body),
  status: () => api<TautulliImportStatus>('/api/v1/migration/tautulli/status'),
}

// ---- migration (import from Sonarr) ----------------------------------------

export type SonarrRootFolderPreview = Schemas['SonarrRootFolderPreview']

export type SonarrImportPreview = {
  sonarrVersion: string
  series: number
  episodes: number
  episodeFiles: number
  monitored: number
  alreadyInLibrary: number
  exclusions: number
  historyRecords: number
  blocklist: number
  suggestedVersionName: string
  /** Series per type: standard, anime, daily. */
  seriesTypes: Record<string, number>
  /** `movies` holds the number of series of each profile (shape shared with Radarr's preview). */
  profiles: RadarrProfilePreview[]
  rootFolders: SonarrRootFolderPreview[]
  tags: RadarrTagPreview[]
  /** Sonarr v3 language profiles, for information only. */
  languageProfiles: string[]
  /** What the import would do with each Sonarr custom format. */
  customFormats: SonarrFormatPreview[] | null
}

/** `new`, `reused` (same name and definition), `renamed` (imported as "name (Sonarr)"), `updated`. */
export type SonarrFormatPreview = { sonarrId: number; sonarrName: string; outcome: 'new' | 'reused' | 'renamed' | 'updated'; bigarrName: string; notes: string[] }

export type SonarrImportRequest = {
  url: string
  apiKey: string
  versionName: string
  /** Sonarr quality profile id → Bigarr quality profile id. */
  profileMap: Record<number, number>
  /** Sonarr root folder path → Bigarr series root folder id. */
  rootFolderMap: Record<string, number>
  importTags: boolean
  importExclusions: boolean
  importHistory: boolean
  importBlocklist: boolean
  keepMonitored: boolean
  /** Shadow mode: keep following this Sonarr with the same mapping once the import is done. */
  follow?: boolean
  /** Give each mapped Series profile the qualities and cutoff of its Sonarr profile. */
  importProfileQualities?: boolean
  /** Bring Sonarr's custom formats and give the Series profiles Sonarr's scores and thresholds. */
  importCustomFormats?: boolean
  /** Copy Sonarr's naming into media management (import only; the follow never rewrites it). */
  importNaming?: boolean
}

/** `tvdbId` is null for a line about a profile; `seriesId` is set when the series is in Bigarr. */
export type SonarrImportIssue = Schemas['SonarrImportIssue']

export type SonarrImportStatus = Schemas['SonarrImportStatusResponse']

export const sonarrMigration = {
  preview: (body: RadarrImportPreviewRequest) => post<SonarrImportPreview>('/api/v1/migration/sonarr/preview', body),
  /** Queues the import (202); 409 when one is already running. */
  start: (body: SonarrImportRequest) => post<SonarrImportStatus>('/api/v1/migration/sonarr/import', body),
  status: () => api<SonarrImportStatus>('/api/v1/migration/sonarr/status'),
}

// ---- shadow mode: followed Radarrs -------------------------------------------

/** A Radarr Bigarr follows in shadow mode. The key comes back masked (`********`); sending the mask keeps the stored one. */
export type FollowedRadarr = Schemas['FollowedRadarrResponse']

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

export type FollowedRadarrTest = Schemas['FollowedRadarrTestResponse']

export type RadarrFollowPass = Schemas['RadarrFollowPass']

export type ShadowStatus = Schemas['ShadowStatusResponse']

/** A Sonarr Bigarr follows in shadow mode, the twin of a followed Radarr. */
export type FollowedSonarr = Schemas['FollowedSonarrResponse']

export type FollowedSonarrRequest = {
  name: string
  url: string
  apiKey: string | null
  versionName: string
  profileMap: Record<number, number>
  rootFolderMap: Record<string, number>
  importTags: boolean
  keepMonitored: boolean
  enabled: boolean
}

export type FollowedSonarrTest = Schemas['FollowedSonarrTestResponse']

export type SonarrFollowPass = Schemas['SonarrFollowPass']

export const KEY_MASK = '********'

// ---- shadow mode: the comparison ---------------------------------------------

export type CompareOutcome = Schemas['CompareOutcome']

/** Bigarr's parser and engine (RSS rules) on a release Radarr grabbed, for that version, when the grab was read. */
export type BigarrVerdict = Schemas['BigarrVerdict']

/** Radarr's parse of a release Bigarr picked, and the film's state in Radarr then. */
export type RadarrView = Schemas['RadarrView']

export type SightingVerdict = Schemas['SightingVerdict']

export type ComparePick = Schemas['ComparePickResponse']

export type RadarrGrabState = Schemas['RadarrGrabState']

export type CompareGrab = Schemas['CompareGrabResponse']

export type CompareSighting = Schemas['CompareSightingResponse']

export type CompareRow = Schemas['CompareRowResponse']

export type CompareCounts = Schemas['CompareCounts']

export type CompareResult = Schemas['CompareResponse']

/** `tmdbId`: one film only (the film page). `days: 0` means all time. */
export type CompareQuery = { version?: string; outcome?: CompareOutcome; days: number; windowHours: number; page: number; pageSize: number; tmdbId?: number }

// ---- shadow mode: the comparison with Sonarr ---------------------------------

export type SeriesCompareOutcome = Schemas['SeriesCompareOutcome']

/** Sonarr's parse of a release Bigarr picked, and those episodes' state in Sonarr then. */
export type SonarrView = Schemas['SonarrView']

export type SeriesComparePick = Schemas['SeriesComparePickResponse']

export type SeriesCompareGrab = Schemas['SeriesCompareGrabResponse']

export type SeriesCompareRow = Schemas['SeriesCompareRowResponse']

export type SeriesCompareCounts = Schemas['SeriesCompareCounts']

/** Rows newest first; `total` counts rows of the asked outcome. */
export type SeriesCompareResult = Schemas['SeriesCompareResponse']

export type SeriesCompareQuery = Omit<CompareQuery, 'outcome' | 'tmdbId'> & { outcome?: SeriesCompareOutcome; tvdbId?: number }

export const shadow = {
  status: () => api<ShadowStatus>('/api/v1/shadow'),
  sonarrs: {
    list: () => api<FollowedSonarr[]>('/api/v1/shadow/sonarrs'),
    add: (body: FollowedSonarrRequest) => post<FollowedSonarr>('/api/v1/shadow/sonarrs', body),
    update: (id: number, body: FollowedSonarrRequest) => put<FollowedSonarr>(`/api/v1/shadow/sonarrs/${id}`, body),
    remove: (id: number) => del(`/api/v1/shadow/sonarrs/${id}`),
    test: (body: FollowedSonarrRequest) => post<FollowedSonarrTest>('/api/v1/shadow/sonarrs/test', body),
    testSaved: (id: number, body?: FollowedSonarrRequest) => post<FollowedSonarrTest>(`/api/v1/shadow/sonarrs/${id}/test`, body ?? null),
    preview: (id: number) => post<SonarrImportPreview>(`/api/v1/shadow/sonarrs/${id}/preview`),
    sync: (id: number) => post<SonarrFollowPass>(`/api/v1/shadow/sonarrs/${id}/sync`),
    /** The path Sonarr's webhook calls (Sonarr → Settings → Connect → Webhook), with a Bigarr API key as `?apikey=`. */
    webhookPath: (id: number) => `/api/v1/shadow/sonarrs/${id}/webhook`,
  },
  /** Sonarr's grabs next to Bigarr's series picks, per episode set. */
  compareSeries: (query: SeriesCompareQuery) => {
    const params = new URLSearchParams({ days: String(query.days), windowHours: String(query.windowHours), page: String(query.page), pageSize: String(query.pageSize) })
    if (query.version) params.set('version', query.version)
    if (query.outcome) params.set('outcome', query.outcome)
    if (query.tvdbId) params.set('tvdbId', String(query.tvdbId))
    return api<SeriesCompareResult>(`/api/v1/shadow/compare/series?${params}`)
  },
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

export type MediaServerKind = Schemas['MediaServerKind']

export type PathMapping = Schemas['PathMappingDto']

export type MediaServer = Schemas['MediaServerResponse']

export type MediaServerRequest = { name: string; kind: MediaServerKind; url: string; token: string; enabled: boolean; pathMappings: PathMapping[] }

export type MediaServerTestResult = Schemas['MediaServerTestResponse']

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

export type MediaItemKind = Schemas['MediaItemKind']
export type PlaybackState = Schemas['PlaybackState']
export type StreamDecision = 'directPlay' | 'directStream' | 'transcode'
export type MinimumPlayUnit = Schemas['MinimumPlayUnit']

export type PlaybackUserRef = Schemas['PlaybackUserRef']

export type PlaybackItemRef = Schemas['PlaybackItemRef']

export type SuggestionReason = Schemas['SuggestionReason']

/** A library film the played item may be: why, the surest reason first, and how many versions it has. */
export type LinkSuggestion = Schemas['LinkSuggestion']

/** `tmdbSearched` is false when TMDB could not be reached: the library alone was looked at. */
export type LinkSuggestions = Schemas['LinkSuggestionsResponse']

export type ItemLink = Schemas['ItemLinkResponse']

/** A film not linked whose film is sure, and the plays it carries. */
export type SureMatch = Schemas['SureMatch']

/** `withoutSureMatch`: the films not linked left for the picker. */
export type SureMatches = Schemas['SureMatchesResponse']

export type ItemFilmPair = Schemas['ItemFilmPair']

/** `skipped`: items linked meanwhile, gone or not films. */
export type LinkMany = Schemas['LinkManyResponse']

/** What the media server says about a stream: every field may be missing. */
export type StreamDetails = Schemas['StreamDetails']

/** How a live transcode keeps up: speed 1 = real time; throttled = far enough ahead that Plex slows down (good). */
export type PlexTranscode = Schemas['PlexTranscode']

/** Where a stream came from, looked up when shown: the local network, or a place for a public address. */
export type PlaybackLocation = Schemas['PlaybackLocation']

export type PlaybackSession = Schemas['PlaybackSessionResponse']

export type PlaybackServerStatus = Schemas['PlaybackServerStatus']

export type NowPlaying = Schemas['NowPlayingResponse']

export type Play = Schemas['PlayResponse']

export type PlaysPage = Schemas['PlaysPageResponse']

/** `name` is the one Bigarr shows (the display name when set), `serverName` the account's on the server. */
export type PlaybackUser = Schemas['PlaybackUserResponse']

export type VersionPlays = Schemas['VersionPlaysResponse']

export type PlaybackSettings = Schemas['PlaybackSettingsDto']

export type PlaySortKey = 'startedAt' | 'user' | 'title' | 'version' | 'played'

export type PlaysQuery = {
  page?: number
  pageSize?: number
  userId?: number
  movieId?: number
  /** A library episode: its plays found by the episode's TVDB id, or the show's ids and its numbers. */
  episodeId?: number
  versionId?: number
  /** A version name (`4k`), across films. */
  version?: string
  kind?: MediaItemKind
  /** Film plays linked to no Bigarr film. */
  unmatched?: boolean
  /** ISO instants bounding the start of the play, `to` excluded. */
  from?: string
  to?: string
  /** The play's furthest decision: a transcode in any stream makes it a transcode. */
  decision?: StreamDecision
  /** In the title or the show's title. */
  q?: string
  sort?: PlaySortKey
  direction?: 'asc' | 'desc'
}

/** Plays started in the last `days` days, or ever when null. */
export type WatchTime = Schemas['WatchTimeResponse']

export type DecisionCounts = Schemas['DecisionCountsResponse']

export type PlaybackUserStats = Schemas['PlaybackUserStatsResponse']

export type UpdatePlaybackUser = Schemas['UpdatePlaybackUserRequest']

export type TopEntry = Schemas['TopEntryResponse']

/** The plays of an hour or a weekday, split by kind and by decision (plays without a stream are in `plays` only). */
export type PlaysSplit = Schemas['PlaysSplitResponse']

export type PlaybackStats = {
  days: number | null
  userId: number | null
  bucket: 'day' | 'week' | 'month'
  totals: { plays: number; seconds: number; users: number; movies: number; episodes: number }
  /** The same plays split two ways: films and episodes, or by their furthest decision. */
  overTime: { date: string; movies: number; episodes: number; seconds: number; directPlay: number; directStream: number; transcode: number }[]
  /** 24 hours, then 7 days Monday first: each split like the plays over time. */
  byHour: PlaysSplit[]
  byWeekday: PlaysSplit[]
  topMovies: TopEntry[]
  topShows: TopEntry[]
  topUsers: TopEntry[]
  /** The same people ranked by watch time. */
  topUsersByTime: TopEntry[]
  /** Each with its plays split by decision (directPlay, directStream, transcode). */
  topPlatforms: TopEntry[]
  /** The same platforms ranked by watch time. */
  topPlatformsByTime: TopEntry[]
  decisions: DecisionCounts
  concurrency: { streams: number; at: string | null }
  transcodedUhd: { movieId: number; title: string; year: number | null; versionName: string | null; plays: number; transcodes: number; lastPlayedAt: string }[]
}

/** One session of a play, with everything the server said about it. */
export type PlayStream = Schemas['PlayStreamResponse']

/** An address a user played from, newest first. */
export type PlaybackAddress = Schemas['PlaybackAddressResponse']

export const playback = {
  nowPlaying: () => api<NowPlaying>('/api/v1/playback/sessions'),
  plays: (query: PlaysQuery = {}) => {
    const params = new URLSearchParams()
    for (const [key, value] of Object.entries(query)) if (value !== undefined) params.set(key, String(value))
    return api<PlaysPage>(`/api/v1/playback/plays?${params}`)
  },
  streams: (playId: number) => api<PlayStream[]>(`/api/v1/playback/plays/${playId}/streams`),
  /** Stops the stream on its player (writes to Plex): only ever from the confirmation dialog. */
  stop: (sessionId: number, message: string | null) => post<void>(`/api/v1/playback/sessions/${sessionId}/stop`, { message }),
  addresses: (userId: number) => api<PlaybackAddress[]>(`/api/v1/playback/users/${userId}/addresses`),
  users: () => api<PlaybackUser[]>('/api/v1/playback/users'),
  user: (id: number) => api<PlaybackUserStats>(`/api/v1/playback/users/${id}`),
  updateUser: (id: number, body: UpdatePlaybackUser) => put<PlaybackUserStats>(`/api/v1/playback/users/${id}`, body),
  playVersions: () => api<string[]>('/api/v1/playback/plays/versions'),
  /** All the plays of the item follow; a null film unlinks it. */
  /** Asked for by hand from the link picker; never run on its own. */
  linkSuggestions: (itemId: number) => api<LinkSuggestions>(`/api/v1/playback/items/${itemId}/suggestions`),
  linkItem: (itemId: number, movieId: number | null, versionId: number | null = null) => put<ItemLink>(`/api/v1/playback/items/${itemId}/link`, { movieId, versionId }),
  sureMatches: () => api<SureMatches>('/api/v1/playback/items/sure-matches'),
  linkMany: (links: ItemFilmPair[]) => post<LinkMany>('/api/v1/playback/items/links', { links }),
  stats: (days: number | null, userId: number | null = null) =>
    api<PlaybackStats>(
      `/api/v1/playback/stats?${new URLSearchParams({ ...(days === null ? {} : { days: String(days) }), ...(userId === null ? {} : { userId: String(userId) }), tz: Intl.DateTimeFormat().resolvedOptions().timeZone })}`,
    ),
  moviePlays: (movieId: number) => api<VersionPlays[]>(`/api/v1/playback/movies/${movieId}`),
  settings: () => api<PlaybackSettings>('/api/v1/playback/settings'),
  updateSettings: (body: PlaybackSettings) => put<PlaybackSettings>('/api/v1/playback/settings', body),
}

// ---- helpers ---------------------------------------------------------------

/** A series profile's name without its leading "Series" word, as the server names a version: `Series 1080p` → `1080p`. */
export function seriesVersionName(profileName: string): string {
  const slug = slugify(profileName)
  return slug.startsWith('series-') && slug.length > 'series-'.length ? slug.slice('series-'.length) : slug
}

export function slugify(value: string): string {
  return value
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
}

// ---- series ----------------------------------------------------------------

export type SeriesType = Schemas['SeriesType']
export type SeriesStatus = Schemas['SeriesStatus']
export type MonitoringOption = Schemas['MonitoringOption']
export type NewItemMonitoring = Schemas['NewItemMonitoring']

/** Per version: monitored and aired (or with a file), files, monitored, every episode outside the specials, bytes. */
export type EpisodeStatistics = Schemas['EpisodeStatistics']

export type SeriesLookupResult = Schemas['SeriesLookupResult']

export type SeriesVersionSummary = Schemas['SeriesVersionSummary']

export type SeriesSummary = Schemas['SeriesSummary']

export type SeriesVersion = Schemas['SeriesVersionResponse']

export type SeasonVersionState = Schemas['SeasonVersionState']

export type Season = Schemas['SeasonResponse']

export type Series = Schemas['SeriesResponse']

export type EpisodeVersionState = Schemas['EpisodeVersionState']

export type Episode = Schemas['EpisodeResponse']

// ---- series order -----------------------------------------------------

export type EpisodeOrder = Schemas['EpisodeOrder']

export type EpisodeGroupType = 'originalAirDate' | 'absolute' | 'dvd' | 'digital' | 'storyArc' | 'production' | 'tv'

export type OrderOnlyEpisode = Schemas['OrderOnlyEpisode']

export type OrderChoice = Schemas['OrderChoiceResponse']

export type EpisodeGroup = { id: string; name: string; type: EpisodeGroupType; groupCount: number; episodeCount: number }

export type OrderChange = Schemas['OrderChangeResponse']

export type SeriesOrders = { seriesId: number; current: OrderChoice; groups: EpisodeGroup[]; lastChange: OrderChange | null; unavailable: string | null }

export type OrderFileStatus = Schemas['OrderFileStatus']

export type OrderPreview = Schemas['OrderPreviewResponse']

export type OrderFileResult = Schemas['OrderFileResult']

export type OrderChoiceRequest = Schemas['OrderChoiceRequest']

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

export type UnmatchedFileReason = Schemas['UnmatchedFileReason']

export type EpisodeFile = Schemas['EpisodeFileResponse']

export type UnmatchedEpisodeFile = Schemas['UnmatchedEpisodeFileResponse']

export type SeriesFiles = Schemas['SeriesFilesResponse']

export type MapSeriesFolderRequest = Schemas['MapSeriesFolderRequest']

/** Null fields are left untouched; `seriesType` applies to the series whatever `versions` picks. */
export type SeriesBulkUpdateRequest = Schemas['SeriesBulkUpdateRequest']

export type BulkSeriesResult = Schemas['BulkSeriesResult']

export type SeriesBulkResponse = Schemas['SeriesBulkResponse']

export type SeriesBulkDeleteRequest = Schemas['SeriesBulkDeleteRequest']

export type SeriesBulkDeleteResponse = Schemas['SeriesBulkDeleteResponse']

/** One series version's outcome of an automatic search. */
export type SeriesAutomaticSearchResult = Schemas['SeriesAutomaticSearchResult']

export const series = {
  list: () => api<SeriesSummary[]>('/api/v1/series'),
  get: (id: number) => api<Series>(`/api/v1/series/${id}`),
  episodes: (id: number) => api<Episode[]>(`/api/v1/series/${id}/episodes`),
  /** Automatic search for one episode, now, by every version of the series: the best approved release is grabbed (picked in shadow mode). */
  searchEpisode: (id: number, episodeId: number) => post<SeriesAutomaticSearchResult[]>(`/api/v1/series/${id}/episodes/${episodeId}/search`),
  lookup: (term: string) => api<SeriesLookupResult[]>(`/api/v1/series/lookup?term=${encodeURIComponent(term)}`),
  add: (body: AddSeriesRequest) => post<Series>('/api/v1/series', body),
  update: (id: number, body: { seriesType?: SeriesType }) => put<Series>(`/api/v1/series/${id}`, body),
  remove: (id: number, deleteFiles = false) => del(`/api/v1/series/${id}?deleteFiles=${deleteFiles}`),
  refresh: (id: number) => post<Series>(`/api/v1/series/${id}/refresh`),
  files: (id: number) => api<SeriesFiles>(`/api/v1/series/${id}/files`),
  rescan: (id: number) => post<ScanStatus>(`/api/v1/series/${id}/rescan`),
  rescanStatus: (id: number) => api<ScanStatus>(`/api/v1/series/${id}/rescan`),
  rescanAll: () => post<ScanStatus[]>('/api/v1/series/rescan'),
  mapFolder: (unmappedId: number, body: MapSeriesFolderRequest) =>
    post<{ seriesId: number; versionId: number; scan: ScanStatus }>(`/api/v1/series/unmapped/${unmappedId}/map`, body),
  addVersion: (id: number, body: AddSeriesVersionRequest) => post<SeriesVersion>(`/api/v1/series/${id}/versions`, body),
  updateVersion: (id: number, versionId: number, body: UpdateSeriesVersionRequest) => put<SeriesVersion>(`/api/v1/series/${id}/versions/${versionId}`, body),
  deleteVersion: (id: number, versionId: number, promote?: number, deleteFiles = false) =>
    del(`/api/v1/series/${id}/versions/${versionId}?deleteFiles=${deleteFiles}${promote ? `&promote=${promote}` : ''}`),
  promote: (id: number, versionId: number) => post<SeriesVersion>(`/api/v1/series/${id}/versions/${versionId}/promote`),
  applyMonitoring: (id: number, versionId: number, monitor: MonitoringOption) =>
    post<Series>(`/api/v1/series/${id}/versions/${versionId}/monitoring`, { monitor }),
  setSeason: (id: number, versionId: number, seasonNumber: number, monitored: boolean) =>
    put<void>(`/api/v1/series/${id}/versions/${versionId}/seasons/${seasonNumber}`, { monitored }),
  setEpisodes: (id: number, versionId: number, episodeIds: number[], monitored: boolean) =>
    put<void>(`/api/v1/series/${id}/versions/${versionId}/episodes`, { episodeIds, monitored }),
  orders: (id: number) => api<SeriesOrders>(`/api/v1/series/${id}/orders`),
  previewOrder: (id: number, body: OrderChoiceRequest) => post<OrderPreview>(`/api/v1/series/${id}/orders/preview`, body),
  applyOrder: (id: number, body: OrderChoiceRequest) => post<{ change: OrderChange; files: OrderFileResult[] }>(`/api/v1/series/${id}/orders/apply`, body),
  /** The library's bulk editor, like the films': one action on one version of many series. */
  bulkUpdate: (body: SeriesBulkUpdateRequest) => put<SeriesBulkResponse>('/api/v1/series/bulk', body),
  /** With `dryRun`, answers the exact plan and changes nothing. */
  bulkDelete: (body: SeriesBulkDeleteRequest) => post<SeriesBulkDeleteResponse>('/api/v1/series/bulk/delete', body),
  undoOrder: (id: number) => post<{ change: OrderChange; filesRestored: number; files: OrderFileResult[] }>(`/api/v1/series/${id}/orders/undo`),
}

// ---- specials that are films (S8) ------------------------------------------

export type FilmConfidence = NonNullable<Schemas['FilmConfidence']>
export type FilmPlacementSource = Schemas['FilmPlacementSource']
export type SeriesFilmVersionLink = Schemas['SeriesFilmVersionLinkResponse']
export type SeriesFilmLink = Schemas['SeriesFilmLinkResponse']
export type SeriesFilm = Schemas['SeriesFilmResponse']
export type SeriesFilms = Schemas['SeriesFilmsResponse']
export type AddSeriesFilmRequest = Partial<Schemas['AddSeriesFilmRequest']> & { episodeId: number }
export type MovieSeriesLink = Schemas['MovieSeriesLinkResponse']
export type EpisodeFilmOwner = Schemas['EpisodeFilmOwner']
export type SeriesFilmState = Schemas['SeriesFilmState']
export type FilmFileStepKind = Schemas['FilmFileStepKind']
export type FilmFileStep = Schemas['FilmFileStep']
export type SeriesFilmReviewRow = Schemas['SeriesFilmReviewRow']
export type SeriesFilmReview = Schemas['SeriesFilmReviewResponse']
export type SeriesFilmSummary = Schemas['SeriesFilmSummaryResponse']
export type LinkSeriesFilmResult = Schemas['LinkSeriesFilmResult']

export const seriesFilms = {
  list: (seriesId: number) => api<SeriesFilms>(`/api/v1/series/${seriesId}/films`),
  add: (seriesId: number, body: AddSeriesFilmRequest) => post<SeriesFilms>(`/api/v1/series/${seriesId}/films`, body),
  detect: (seriesId: number) => post<SeriesFilms>(`/api/v1/series/${seriesId}/films/detect`),
  remove: (seriesId: number, episodeId: number) => del(`/api/v1/series/${seriesId}/films/${episodeId}`),
  setAutoAdd: (autoAdd: boolean) => put<void>('/api/v1/settings/series-films', { autoAdd }),
  forMovie: (movieId: number) => api<MovieSeriesLink[]>(`/api/v1/movies/${movieId}/series-links`),
  /** Every candidate of every show, with what linking it would do on disk (a dry run: nothing is written). */
  review: () => api<SeriesFilmReview>('/api/v1/series-films/review'),
  summary: () => api<SeriesFilmSummary>('/api/v1/series-films/summary'),
  linkMany: (episodeIds: number[]) => post<LinkSeriesFilmResult[]>('/api/v1/series-films/link', { episodeIds }),
  dismiss: (seriesId: number, episodeId: number) => post<void>(`/api/v1/series/${seriesId}/films/${episodeId}/dismiss`),
  restore: (seriesId: number, episodeId: number) => del(`/api/v1/series/${seriesId}/films/${episodeId}/dismiss`),
}

// ---- series automation ------------------------------------------------

export type SeriesWantedItem = Schemas['SeriesWantedItemResponse']

export type SeriesWantedPage = Schemas['SeriesWantedPageResponse']

export const seriesWanted = {
  list: (filter?: WantedFilter, page = 1, pageSize = 50, seriesId?: number) =>
    api<SeriesWantedPage>(`/api/v1/series/wanted?page=${page}&pageSize=${pageSize}${filter ? `&filter=${filter}` : ''}${seriesId ? `&seriesId=${seriesId}` : ''}`),
  /** These rows, or every row of the filter when none is given; queued, searched one version at a time. */
  search: (targetIds?: number[], filter?: WantedFilter) => post<{ queued: number }>('/api/v1/series/wanted/search', { targetIds: targetIds ?? null, filter: filter ?? null }),
}

export type HeldRelease = Schemas['HeldReleaseResponse']

export const heldReleases = {
  list: (seriesId?: number, includeDismissed = false) =>
    api<HeldRelease[]>(`/api/v1/series/held?includeDismissed=${includeDismissed}${seriesId ? `&seriesId=${seriesId}` : ''}`),
  grab: (id: number) => post<GrabResult>(`/api/v1/series/held/${id}/grab`),
  dismiss: (id: number) => post<void>(`/api/v1/series/held/${id}/dismiss`),
}

/** Sonarr's calendar legend. */
export type CalendarStatus = Schemas['CalendarStatus']

export type CalendarEpisode = Schemas['CalendarEpisodeResponse']

export const calendar = {
  /** `start` and `end` are UTC instants, the end excluded; at most 62 days. */
  list: (start: Date, end: Date, unmonitored = false) =>
    api<CalendarEpisode[]>(`/api/v1/calendar?start=${encodeURIComponent(start.toISOString())}&end=${encodeURIComponent(end.toISOString())}&unmonitored=${unmonitored}`),
  icsUrl: '/api/v1/calendar/bigarr.ics',
}
