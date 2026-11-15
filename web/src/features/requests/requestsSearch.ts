import type { RequestFilter, RequestMediaType } from '@/api/client'

export const FILTERS: RequestFilter[] = ['all', 'pending', 'approved', 'processing', 'available', 'declined', 'failed']

export type RequestsSearch = { filter?: RequestFilter; type?: RequestMediaType; user?: number; page?: number }

export const validateRequestsSearch = (search: Record<string, unknown>): RequestsSearch => ({
  filter: FILTERS.includes(search.filter as RequestFilter) ? (search.filter as RequestFilter) : undefined,
  type: search.type === 'movie' || search.type === 'series' ? search.type : undefined,
  user: typeof search.user === 'number' ? search.user : undefined,
  page: typeof search.page === 'number' && search.page > 1 ? search.page : undefined,
})

