import type { TFunction } from 'i18next'
import type { DiscoverSliderKind } from '@/api/client'

/** The name of a row: the admin's title for a custom row, the built-in name otherwise. */
export function sliderTitle(t: TFunction, kind: DiscoverSliderKind, title: string | null) {
  return title ?? t(`discover.slider.${kind}`)
}

export const builtInKinds: DiscoverSliderKind[] = [
  'trending',
  'popularMovies',
  'upcomingMovies',
  'movieGenres',
  'studios',
  'popularSeries',
  'upcomingSeries',
  'seriesGenres',
  'networks',
]

/** The rows an admin can add, and what their value is. */
export const customKinds: { kind: DiscoverSliderKind; value: 'genre-movie' | 'genre-series' | 'lookup' | 'words' }[] = [
  { kind: 'movieGenre', value: 'genre-movie' },
  { kind: 'seriesGenre', value: 'genre-series' },
  { kind: 'movieKeyword', value: 'lookup' },
  { kind: 'seriesKeyword', value: 'lookup' },
  { kind: 'studio', value: 'lookup' },
  { kind: 'network', value: 'lookup' },
  { kind: 'movieSearch', value: 'words' },
  { kind: 'seriesSearch', value: 'words' },
]
