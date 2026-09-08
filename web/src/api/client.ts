export class ApiError extends Error {
  constructor(
    public readonly status: number,
    message: string,
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
    throw new ApiError(response.status, (await response.text().catch(() => '')) || response.statusText)
  }

  const isJson = response.headers.get('content-type')?.includes('application/json') ?? false
  if (response.status === 204 || !isJson) {
    return undefined as T
  }

  return (await response.json()) as T
}

export type Me = {
  id: number
  username: string
  role: string
  authKind: 'Session' | 'ApiKey'
  compatView: string | null
}

export type SystemStatus = {
  appName: string
  version: string
  environmentName: string
  startedAt: string
  runtimeVersion: string
  os: string
  isDocker: boolean
  configDirectory: string
}

export const auth = {
  me: () => api<Me>('/api/v1/auth/me'),
  login: (username: string, password: string) =>
    api<void>('/api/v1/auth/login', { method: 'POST', body: JSON.stringify({ username, password, rememberMe: true }) }),
  logout: () => api<void>('/api/v1/auth/logout', { method: 'POST' }),
}

export const system = {
  status: () => api<SystemStatus>('/api/v1/system/status'),
}
