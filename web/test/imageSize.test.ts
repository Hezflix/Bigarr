import { describe, expect, test } from 'bun:test'
import { sizedImage } from '../src/features/playback/imageSize'

describe('sizedImage', () => {
  test('asks TMDB for the width', () => {
    expect(sizedImage('https://image.tmdb.org/t/p/original/abc.jpg', 780)).toBe('https://image.tmdb.org/t/p/w780/abc.jpg')
  })

  test('leaves TVDB artwork alone, even with /original/ in its path', () => {
    const fanart = 'https://artworks.thetvdb.com/banners/fanart/original/12345-1.jpg'
    expect(sizedImage(fanart, 780)).toBe(fanart)
  })

  test('leaves any other address alone', () => {
    expect(sizedImage('https://example.org/original/poster.jpg', 185)).toBe('https://example.org/original/poster.jpg')
  })
})
