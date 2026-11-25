import { describe, expect, test } from 'bun:test'
import type { QualityDefinition } from '../src/api/client'
import { qualityOptions } from '../src/components/qualityOptions'

const definition = (id: number, name: string): QualityDefinition => ({
  id,
  name,
  title: null,
  source: 'unknown',
  resolution: 0,
  modifier: 'none',
  minSize: null,
  preferredSize: null,
  maxSize: null,
  seriesMinSize: null,
  seriesPreferredSize: null,
  seriesMaxSize: null,
})

// As the server sends them: worst first.
const definitions = [definition(0, 'Unknown'), definition(1, 'SDTV'), definition(4, 'HDTV-720p'), definition(7, 'Bluray-1080p')]

describe('quality options', () => {
  test('list the best quality first', () => {
    expect(qualityOptions(definitions, true).map((q) => q.name)).toEqual(['Bluray-1080p', 'HDTV-720p', 'SDTV', 'Unknown'])
  })

  test('leave Unknown out unless it may be kept', () => {
    expect(qualityOptions(definitions, false).map((q) => q.name)).toEqual(['Bluray-1080p', 'HDTV-720p', 'SDTV'])
  })

  test('do not reorder the list they are given', () => {
    qualityOptions(definitions, true)
    expect(definitions[0].name).toBe('Unknown')
  })
})
