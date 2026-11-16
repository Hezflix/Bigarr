import type { IssueStatusFilter, IssueType } from '@/api/client'

export const STATUSES: IssueStatusFilter[] = ['open', 'resolved', 'all']

/** The Issues list's filters, in the address: `?status=open&type=audio&q=matrix`; `movieId` / `seriesId` from the library pages (admin). */
export type IssuesSearch = { status?: IssueStatusFilter; type?: IssueType; q?: string; movieId?: number; seriesId?: number; page?: number }

const positive = (value: unknown) => (typeof value === 'number' && Number.isInteger(value) && value > 0 ? value : undefined)

export const validateIssuesSearch = (search: Record<string, unknown>): IssuesSearch => ({
  status: STATUSES.includes(search.status as IssueStatusFilter) ? (search.status as IssueStatusFilter) : undefined,
  type: ['video', 'audio', 'subtitles', 'other'].includes(search.type as string) ? (search.type as IssueType) : undefined,
  q: typeof search.q === 'string' && search.q.trim() ? search.q : undefined,
  movieId: positive(search.movieId),
  seriesId: positive(search.seriesId),
  page: typeof search.page === 'number' && search.page > 1 ? search.page : undefined,
})
