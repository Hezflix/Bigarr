import { describe, expect, test } from 'bun:test'
import { bestGuesses, titleFromFileName } from '../src/features/import/titleGuess'

const library = [
  { id: 1, title: 'Blade Runner', year: 1982 },
  { id: 2, title: 'Blade Runner 2049', year: 2017 },
  { id: 3, title: 'The Matrix', year: 1999 },
  { id: 4, title: 'Amélie', year: 2001 },
  { id: 5, title: 'Serenity', year: 2005 },
]

describe('bestGuesses', () => {
  test('shared words first, then the closest year', () => {
    expect(bestGuesses(library, 'Blade Runner', 1982).map((c) => c.id)).toEqual([1, 2])
    expect(bestGuesses(library, 'Blade Runner', 2017).map((c) => c.id)).toEqual([2, 1])
    expect(bestGuesses(library, 'Blade Runner 2049', 2017).map((c) => c.id)).toEqual([2, 1])
  })

  test('accents do not count', () => {
    expect(bestGuesses(library, 'Le Fabuleux Destin d Amelie Poulain', 2001).map((c) => c.id)).toEqual([4])
  })

  test('nothing in common, nothing guessed', () => {
    expect(bestGuesses(library, "Seconds (L'Opération diabolique)", 1966)).toEqual([])
    expect(bestGuesses(library, null, null)).toEqual([])
  })

  test('pinned ids come first, once, and the count holds', () => {
    expect(bestGuesses(library, 'Blade Runner', 1982, 2, [5]).map((c) => c.id)).toEqual([5, 1])
    expect(bestGuesses(library, 'Blade Runner', 1982, 5, [1]).map((c) => c.id)).toEqual([1, 2])
  })
})

describe('titleFromFileName', () => {
  test('the words before the episode marker, quality or year', () => {
    expect(titleFromFileName('Breaking.Bad.S02E01.1080p.BluRay.x264-DEMAND.mkv')).toBe('Breaking Bad')
    expect(titleFromFileName('Firefly - 1x01 - Serenity.mkv')).toBe('Firefly')
    expect(titleFromFileName('The.Office.US.2005.S01.1080p.mkv')).toBe('The Office US')
    expect(titleFromFileName('Show Name Season 2 Episode 3.mkv')).toBe('Show Name')
  })

  test('a name that starts with its marker has no title', () => {
    expect(titleFromFileName('S01E01.mkv')).toBe('')
  })
})
