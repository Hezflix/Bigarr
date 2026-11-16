import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { PlusIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { auth, isAdmin, type SeriesLookupResult, series } from '@/api/client'
import { Poster } from '@/components/Poster'
import { Button } from '@/components/ui/button'
import { AdminAddDialog } from './AdminAddDialog'

/**
 * For the admin, under Discover's search: the series the library's lookup finds on TVDB that TMDB does not have, so
 * Discover cannot show them. The former series Add found them; they are added from here (title or <c>tvdb:81189</c>).
 */
export function TvdbOnlyResults({ query }: { query: string }) {
  const { t } = useTranslation()
  const me = useQuery({ queryKey: ['me'], queryFn: auth.me, staleTime: 60_000 })
  const admin = isAdmin(me.data)
  const lookup = useQuery({ queryKey: ['seriesLookup', query], queryFn: () => series.lookup(query), enabled: admin && query.trim().length >= 2 })
  const [adding, setAdding] = useState<SeriesLookupResult | null>(null)
  const only = (lookup.data ?? []).filter((r) => r.tmdbId == null && r.tvdbId != null)
  if (!admin || only.length === 0) return null

  return (
    <section className="flex flex-col gap-3" aria-label={t('requests.add.tvdbOnly')} data-testid="tvdb-only">
      <div>
        <h2 className="text-lg font-semibold tracking-tight">{t('requests.add.tvdbOnly')}</h2>
        <p className="text-sm text-muted-foreground">{t('requests.add.tvdbOnlyHint')}</p>
      </div>
      <ul className="flex flex-col divide-y rounded-lg border">
        {only.map((r) => (
          <li key={r.tvdbId} className="flex items-center gap-3 p-3">
            <Poster url={r.posterUrl} title={r.title} size="small" className="shrink-0" />
            <div className="min-w-0 flex-1">
              <p className="font-medium">
                {r.title} {r.year && <span className="text-sm font-normal text-muted-foreground">({r.year})</span>}
              </p>
              <p className="text-xs text-muted-foreground">{[r.network, `TVDB ${r.tvdbId}`].filter(Boolean).join(' · ')}</p>
            </div>
            {r.existingSeriesId ? (
              <Button asChild size="sm" variant="outline">
                <Link to="/series/$seriesId" params={{ seriesId: String(r.existingSeriesId) }}>
                  {t('discover.openInLibrary')}
                </Link>
              </Button>
            ) : (
              <Button size="sm" onClick={() => setAdding(r)} data-testid="tvdb-only-add">
                <PlusIcon />
                {t('requests.add.add')}
              </Button>
            )}
          </li>
        ))}
      </ul>
      {adding && (
        <AdminAddDialog
          mediaType="series"
          tmdbId={null}
          tvdbId={adding.tvdbId}
          preset={{ title: adding.title, year: adding.year, posterUrl: adding.posterUrl, overview: adding.overview }}
          onClose={() => setAdding(null)}
        />
      )}
    </section>
  )
}
