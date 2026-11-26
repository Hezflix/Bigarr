import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { profilesFor, quality, rootFolders, rootsOfKind, type RadarrImportPreview, type SonarrImportPreview } from '@/api/client'
import { Badge } from '@/components/ui/badge'
import { IdSelect } from '@/components/IdSelect'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'

/**
 * How a Radarr's films land in Bigarr: its quality profiles and root folders mapped to Bigarr's. Shared by the one-time
 * import and the Radarrs followed in shadow mode, which keep using the mapping on every pass.
 */
export type RadarrMappingValue = { profileMap: Record<number, number>; rootFolderMap: Record<string, number> }

/** What the mapping tables show above each column: Radarr's words by default, Sonarr's for series. */
export type MappingLabels = { profile: string; count: string; path: string }

/** A Sonarr preview in the tables' shape: series counted where films are. */
export function sonarrMappingPreview(preview: SonarrImportPreview): RadarrImportPreview {
  return {
    radarrVersion: preview.sonarrVersion,
    movies: preview.series,
    withFile: preview.episodeFiles,
    monitored: preview.monitored,
    alreadyInLibrary: preview.alreadyInLibrary,
    exclusions: preview.exclusions,
    historyRecords: preview.historyRecords,
    blocklist: preview.blocklist,
    customFormats: 0,
    suggestedVersionName: preview.suggestedVersionName,
    profiles: preview.profiles,
    rootFolders: preview.rootFolders.map((r) => ({ path: r.path, movies: r.series, suggestedRootFolderId: r.suggestedRootFolderId, reachable: r.reachable })),
    tags: preview.tags,
  }
}

export const slugPattern = /^[a-z0-9]+(?:-[a-z0-9]+)*$/

/** What the preview suggests: same-named profiles, root folders Bigarr already has. */
export function suggestedMapping(preview: RadarrImportPreview): RadarrMappingValue {
  return {
    profileMap: Object.fromEntries(preview.profiles.filter((p) => p.suggestedProfileId !== null).map((p) => [p.id, p.suggestedProfileId as number])),
    rootFolderMap: Object.fromEntries(preview.rootFolders.filter((r) => r.suggestedRootFolderId !== null).map((r) => [r.path, r.suggestedRootFolderId as number])),
  }
}

/** Every profile and root folder holding films is mapped. */
export function mappingComplete(preview: RadarrImportPreview, value: RadarrMappingValue): boolean {
  return (
    preview.profiles.filter((p) => p.movies > 0).every((p) => value.profileMap[p.id] !== undefined) &&
    preview.rootFolders.filter((r) => r.movies > 0).every((r) => value.rootFolderMap[r.path] !== undefined)
  )
}

export function ProfileMappingTable({
  preview,
  value,
  onChange,
  labels,
  kind = 'movies',
  allowCreate = false,
}: {
  preview: RadarrImportPreview
  value: Record<number, number>
  onChange: (value: Record<number, number>) => void
  labels?: MappingLabels
  /** Which Bigarr profiles can be chosen: Films (and Both) for a Radarr, Series (and Both) for a Sonarr. */
  kind?: 'movies' | 'series'
  /** The Sonarr import can create a Series profile from the source one (value 0). */
  allowCreate?: boolean
}) {
  const { t } = useTranslation()
  const profiles = useQuery({ queryKey: ['qualityProfiles'], queryFn: quality.profiles, select: profilesFor(kind) })
  return (
    <div className="overflow-x-auto rounded-lg border">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>{labels?.profile ?? t('importRadarr.radarrProfile')}</TableHead>
            <TableHead>{labels?.count ?? t('importRadarr.movies')}</TableHead>
            <TableHead className="w-[40%]">{t('importRadarr.bigarrProfile')}</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {preview.profiles.map((profile) => (
            <TableRow key={profile.id}>
              <TableCell className="font-medium">{profile.name}</TableCell>
              <TableCell>{profile.movies}</TableCell>
              <TableCell>
                <IdSelect
                  value={value[profile.id]}
                  onChange={(next) => onChange({ ...value, [profile.id]: next })}
                  options={[...(allowCreate ? [{ id: 0, label: t('importSonarr.createProfile') }] : []), ...(profiles.data ?? []).map((p) => ({ id: p.id, label: p.name }))]}
                  placeholder={t('importRadarr.chooseProfile')}
                  ariaLabel={t('importRadarr.bigarrProfile')}
                />
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  )
}

export function RootFolderMappingTable({
  preview,
  value,
  onChange,
  labels,
  kind = 'movies',
}: {
  preview: RadarrImportPreview
  value: Record<string, number>
  onChange: (value: Record<string, number>) => void
  labels?: MappingLabels
  /** Which root folders can be chosen: film ones for a Radarr, series ones for a Sonarr. */
  kind?: 'movies' | 'series'
}) {
  const { t } = useTranslation()
  const roots = useQuery({ queryKey: ['rootFolders'], queryFn: rootFolders.list, select: rootsOfKind(kind) })
  return (
    <div className="overflow-x-auto rounded-lg border">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>{labels?.path ?? t('importRadarr.radarrPath')}</TableHead>
            <TableHead>{labels?.count ?? t('importRadarr.movies')}</TableHead>
            <TableHead className="w-[40%]">{t('importRadarr.bigarrRootFolder')}</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {preview.rootFolders.map((root) => (
            <TableRow key={root.path}>
              <TableCell>
                <span className="flex flex-wrap items-center gap-2">
                  <code className="rounded bg-muted px-1.5 py-0.5 text-xs break-all">{root.path}</code>
                  {root.reachable ? (
                    <Badge variant="outline" className="border-emerald-600/30 bg-emerald-500/15 text-emerald-700 dark:border-emerald-400/30 dark:text-emerald-300">
                      {t('importRadarr.reachable')}
                    </Badge>
                  ) : (
                    <Badge variant="outline" className="border-amber-600/30 bg-amber-500/15 text-amber-700 dark:border-amber-400/30 dark:text-amber-300">
                      {t('importRadarr.notReachable')}
                    </Badge>
                  )}
                </span>
              </TableCell>
              <TableCell>{root.movies}</TableCell>
              <TableCell>
                <IdSelect
                  value={value[root.path]}
                  onChange={(next) => onChange({ ...value, [root.path]: next })}
                  options={roots.data?.map((r) => ({ id: r.id, label: r.path }))}
                  placeholder={t('importRadarr.chooseRootFolder')}
                  ariaLabel={t('importRadarr.bigarrRootFolder')}
                />
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  )
}
