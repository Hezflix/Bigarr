import { describe, expect, test } from 'bun:test'
import type { QualityDefinition } from '../src/api/client'
import { qualityNamesBestFirst, qualityOptions } from '../src/components/qualityOptions'

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

describe('quality names of a filter', () => {
  test('list the best quality first, each name once', () => {
    expect(qualityNamesBestFirst(['SDTV', 'Bluray-1080p', 'HDTV-720p', 'SDTV', 'Bluray-1080p'], definitions)).toEqual(['Bluray-1080p', 'HDTV-720p', 'SDTV'])
  })

  test('follow the definitions, not the alphabet', () => {
    const web = [definition(9, 'HDTV-1080p'), definition(3, 'WEBDL-1080p'), definition(7, 'Bluray-1080p'), definition(30, 'Remux-1080p')]
    expect(qualityNamesBestFirst(['Bluray-1080p', 'HDTV-1080p', 'Remux-1080p', 'WEBDL-1080p'], web)).toEqual(['Remux-1080p', 'Bluray-1080p', 'WEBDL-1080p', 'HDTV-1080p'])
  })

  test('put the names the definitions do not know last, in alphabetical order', () => {
    expect(qualityNamesBestFirst(['Zeta', 'Unknown', 'Alpha', 'HDTV-720p'], definitions)).toEqual(['HDTV-720p', 'Unknown', 'Alpha', 'Zeta'])
  })

  test('fall back to the alphabet while the definitions load', () => {
    expect(qualityNamesBestFirst(['SDTV', 'Bluray-1080p'], [])).toEqual(['Bluray-1080p', 'SDTV'])
  })
})
