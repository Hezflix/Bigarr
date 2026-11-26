import type { TFunction } from 'i18next'
import type { Episode } from '@/api/client'
import { seasonOptions } from '@/components/seasonOptions'
import { episodeCode } from '@/lib/episodeCode'
import { formatDate } from '@/i18n'
import type { PaletteParamOption } from './actions'

/**
 * What a series action can be narrowed to with Tab. A season is one of the order on screen: `orderSeason` when the show
 * follows another order than TVDB's, `episodeIds` its episodes (what an automatic search queues).
 */
export type SeriesScope =
  | { kind: 'series' }
  | { kind: 'season'; season: number; orderSeason: boolean; episodeIds: number[] }
  | { kind: 'episode'; episode: Episode }

const pad = (n: number) => String(n).padStart(2, '0')

/** An episode's season and number in the order on screen; null for one the order leaves out. */
function numbers(e: Episode, alt: boolean): { season: number; episode: number } | null {
  if (!alt) return { season: e.seasonNumber, episode: e.episodeNumber }
  return e.notInOrder ? null : { season: e.displaySeasonNumber, episode: e.displayEpisodeNumber }
}

/** `S01E03` in the order on screen; the TVDB code for an episode the order leaves out. */
export function shownEpisodeCode(e: Episode, alt: boolean): string {
  const n = numbers(e, alt) ?? { season: e.seasonNumber, episode: e.episodeNumber }
  return episodeCode(n.season, n.episode)
}

/** The searchable episodes (a film's own are left out: the film searches for them), grouped by season of the order on screen. */
function seasonsOf(episodes: Episode[], alt: boolean): Map<number, Episode[]> {
  const bySeason = new Map<number, Episode[]>()
  for (const e of episodes) {
    const n = numbers(e, alt)
    if (!e.ownedByFilm && n) bySeason.set(n.season, [...(bySeason.get(n.season) ?? []), e])
  }
  return bySeason
}

/**
 * The whole series, its seasons, then its episodes (listed once something is typed), numbered like the page: in the
 * show's order when it follows another one than TVDB's. Typing "s2", "s02e05", "2x05", "e5" or a word of the title finds them.
 */
export function seriesScopeOptions(t: TFunction, episodes: Episode[], alt: boolean): PaletteParamOption[] {
  const scope = t('palette.param.scopeGroup')
  const options: PaletteParamOption[] = [{ id: 'series', label: t('palette.param.wholeSeries'), group: scope, keywords: ['all', 'series'] }]
  const bySeason = seasonsOf(episodes, alt)
  // Specials last, as on the page.
  const seasons = seasonOptions([...bySeason.keys()])
  for (const n of seasons) {
    const label = n === 0 ? t('palette.param.specials') : t('palette.param.season', { number: n })
    const inSeason = [...(bySeason.get(n) ?? [])].sort((a, b) => (numbers(a, alt)?.episode ?? 0) - (numbers(b, alt)?.episode ?? 0))
    options.push({
      id: `season:${n}`,
      label,
      hint: t('palette.param.episodeCount', { count: inSeason.length }),
      group: t('palette.param.seasonsGroup'),
      keywords: [`s${n}`, `s${pad(n)}`, 'season', ...(n === 0 ? ['specials'] : [])],
      // Its episodes, all listed (nothing to type first) and found by their number alone.
      narrow: { label, options: inSeason.map((e) => episodeOption(t, e, alt, { drilled: true })) },
    })
  }
  // In order, those the order leaves out last.
  const rank = (e: Episode) => numbers(e, alt) ?? { season: Number.MAX_SAFE_INTEGER, episode: e.seasonNumber * 10_000 + e.episodeNumber }
  const searchable = episodes.filter((e) => !e.ownedByFilm).sort((a, b) => rank(a).season - rank(b).season || rank(a).episode - rank(b).episode)
  for (const e of searchable) options.push(episodeOption(t, e, alt, { drilled: false }))
  return options
}

/** One episode as a value: "S01E03 · Title", found by its code in any spelling (see `canonicalEpisodeQuery`) or a word of its title. */
function episodeOption(t: TFunction, e: Episode, alt: boolean, { drilled }: { drilled: boolean }): PaletteParamOption {
  const n = numbers(e, alt)
  return {
    id: `episode:${e.id}`,
    label: `${shownEpisodeCode(e, alt)} · ${e.title}`,
    hint: n ? formatDate(e.airDate) : t('seriesOrder.notInOrderTitle'),
    group: t('palette.param.episodesGroup'),
    keywords: [
      ...(n
        ? [`s${n.season}e${n.episode}`, `s${n.season}e${pad(n.episode)}`, `s${pad(n.season)}e${n.episode}`, `${n.season}x${pad(n.episode)}`, `${n.season}x${n.episode}`, `e${n.episode}`, `e${pad(n.episode)}`]
        : []),
      // Inside a season the number alone says which: "5", "05".
      ...(drilled && n ? [String(n.episode), pad(n.episode)] : []),
      ...(e.absoluteEpisodeNumber ? [String(e.absoluteEpisodeNumber)] : []),
    ],
    onlyWhenTyping: !drilled,
  }
}

/** Back from an option's id to what it names; null when the episode is gone (a refresh while the palette was open). */
export function parseSeriesScope(id: string, episodes: Episode[], alt: boolean): SeriesScope | null {
  if (id === 'series') return { kind: 'series' }
  const [kind, value] = id.split(':')
  if (kind === 'season') {
    const season = Number(value)
    return { kind: 'season', season, orderSeason: alt, episodeIds: (seasonsOf(episodes, alt).get(season) ?? []).map((e) => e.id) }
  }
  const episode = episodes.find((e) => e.id === Number(value))
  return episode ? { kind: 'episode', episode } : null
}
