import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { playback } from '@/api/client'
import { Badge } from '@/components/ui/badge'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { formatDateTime } from '@/i18n'
import { PlaysTable } from './PlaysTable'

/** How much each version of this film is played: is the 4K worth its disk? Hidden until someone played the film. */
export function MoviePlays({ movieId }: { movieId: number }) {
  const { t } = useTranslation()
  const perVersion = useQuery({ queryKey: ['playback', 'movie', movieId], queryFn: () => playback.moviePlays(movieId) })
  const recent = useQuery({ queryKey: ['playback', 'plays', 'movie', movieId], queryFn: () => playback.plays({ movieId, pageSize: 20 }) })
  if (!perVersion.data || perVersion.data.length === 0) return null

  return (
    <section className="flex flex-col gap-3" data-testid="movie-plays">
      <h2 className="text-xl font-semibold tracking-tight">{t('playback.moviePlays')}</h2>
      <div className="overflow-x-auto rounded-lg border">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>{t('playback.version')}</TableHead>
              <TableHead className="text-right">{t('playback.plays')}</TableHead>
              <TableHead className="text-right">{t('playback.users')}</TableHead>
              <TableHead className="text-right">{t('playback.transcodes')}</TableHead>
              <TableHead>{t('playback.lastPlayed')}</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {perVersion.data.map((row) => (
              <TableRow key={row.versionId ?? 'unknown'}>
                <TableCell>{row.versionName ? <Badge variant="secondary">{row.versionName}</Badge> : <span className="text-muted-foreground">{t('playback.unknownVersion')}</span>}</TableCell>
                <TableCell className="text-right tabular-nums">{row.plays}</TableCell>
                <TableCell className="text-right tabular-nums">{row.users}</TableCell>
                <TableCell className="text-right tabular-nums">{row.transcodes}</TableCell>
                <TableCell className="text-sm text-muted-foreground">{formatDateTime(row.lastPlayedAt)}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>
      {recent.data && recent.data.records.length > 0 && <PlaysTable plays={recent.data.records} showItem={false} />}
    </section>
  )
}
