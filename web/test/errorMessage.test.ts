import { describe, expect, test } from 'bun:test'
import { ApiError } from '../src/api/client'
import { errorMessage } from '../src/lib/errorMessage'

describe('error message', () => {
  test('is the message the API sent', () => {
    expect(errorMessage(new ApiError(409, 'The episode already has a file.'), 'Something went wrong.')).toBe('The episode already has a file.')
  })

  test('is the fallback for an error that did not come from the API', () => {
    expect(errorMessage(new TypeError('Failed to fetch'), 'Something went wrong.')).toBe('Something went wrong.')
  })

  test('is the fallback for a value that is not an error', () => {
    expect(errorMessage(undefined, 'Something went wrong.')).toBe('Something went wrong.')
  })
})
