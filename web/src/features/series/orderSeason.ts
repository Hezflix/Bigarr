import type { Episode, EpisodeFile, Season, Series } from '@/api/client'

/**
 * A season of the order on screen, built from its episodes (the TVDB seasons carry the stored flags and statistics).
 * Its size is the sum of the distinct files its episodes sit in for each version: a multi-episode file counts once.
 * `files` holds the files known to the page; a version whose files are not loaded gets 0.
 */
export function orderSeason(s: Series, number: number, episodes: Episode[], files: Iterable<EpisodeFile>): Season {
  const sizeById = new Map<number, number>()
  for (const file of files) sizeById.set(file.id, file.size)
  const stats = (versionId: number) => {
    const states = episodes.map((e) => ({ e, v: e.versions.find((x) => x.versionId === versionId) }))
    const monitored = states.filter((x) => x.v?.monitored).length
    const files = states.filter((x) => x.v?.hasFile).length
    const fileIds = new Set(states.flatMap((x) => (x.v?.episodeFileId == null ? [] : [x.v.episodeFileId])))
    return {
      monitored: episodes.length > 0 && monitored === episodes.length,
      statistics: {
        episodeCount: states.filter((x) => x.v?.hasFile || (x.v?.monitored && x.e.hasAired)).length,
        episodeFileCount: files,
        monitoredEpisodeCount: monitored,
        totalEpisodeCount: episodes.length,
        sizeOnDisk: [...fileIds].reduce((sum, id) => sum + (sizeById.get(id) ?? 0), 0),
      },
    }
  }
  return {
    seasonNumber: number,
    title: null,
    overview: null,
    posterUrl: null,
    episodeCount: episodes.length,
    airedEpisodeCount: episodes.filter((e) => e.hasAired).length,
    versions: s.versions.map((v) => ({ versionId: v.id, ...stats(v.id) })),
  }
}
