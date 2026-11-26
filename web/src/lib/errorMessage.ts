import { ApiError } from '@/api/client'

/** What to tell the user about a failed call: the API's own message when it sent one, the fallback for anything else. */
export const errorMessage = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback)
