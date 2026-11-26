import { describe, expect, test } from 'bun:test'
import { compareAgreement } from '../src/features/activity/compareModel'

describe('compare agreement', () => {
  test('is the share of the grabs Bigarr would have made too', () => {
    expect(compareAgreement({ same: 6, different: 1 }, 1)).toEqual({ grabs: 8, percent: 75 })
  })

  test('rounds to a whole percentage', () => {
    expect(compareAgreement({ same: 2, different: 1 }, 0)).toEqual({ grabs: 3, percent: 67 })
  })

  test('has no percentage when the application grabbed nothing', () => {
    expect(compareAgreement({ same: 0, different: 0 }, 0)).toEqual({ grabs: 0, percent: null })
  })
})
