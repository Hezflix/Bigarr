/** "S01E02": an episode's number the way Sonarr, the indexers and the file names write it, two digits at least. */
export const episodeCode = (season: number, episode: number) => `S${String(season).padStart(2, '0')}E${String(episode).padStart(2, '0')}`
