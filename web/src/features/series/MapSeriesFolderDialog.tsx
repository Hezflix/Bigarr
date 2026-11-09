import { useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ApiError, series as seriesApi, type SeriesLookupResult, type UnmappedFolder } from '@/api/client'
import { Poster } from '@/components/Poster'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Skeleton } from '@/components/ui/skeleton'

/** "Breaking Bad (2008) {tvdb-81189}" → "Breaking Bad": a starting point for the search. */
const searchTermFor = (folder: string) =>
  folder
    .replace(/\{[^}]*\}|\[[^\]]*\]/g, ' ')
    .replace(/\(?(19|20)\d{2}\)?\s*$/, ' ')
    .replace(/[._]/g, ' ')
    .replace(/\s{2,}/g, ' ')
    .trim()

/**
 * The manual answer for a series folder the scan could not match: search the show, pick it. A show already in the
 * library takes the folder as a version; another one is added, unmonitored, on that folder. A rescan follows.
 */
export function MapSeriesFolderDialog({ folder, onClose }: { folder: UnmappedFolder; onClose: () => void }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [draft, setDraft] = useState(() => searchTermFor(folder.relativePath))
  const [term, setTerm] = useState(draft)
  const results = useQuery({ queryKey: ['seriesLookup', term], queryFn: () => seriesApi.lookup(term), enabled: term.length > 0 })

  const map = useMutation({
    mutationFn: (show: SeriesLookupResult) =>
      seriesApi.mapFolder(folder.id, show.existingSeriesId ? { seriesId: show.existingSeriesId } : { tvdbId: show.tvdbId, tmdbId: show.tmdbId }),
    onSuccess: (mapped, show) => {
      void queryClient.invalidateQueries({ queryKey: ['unmapped'] })
      void queryClient.invalidateQueries({ queryKey: ['rootFolders'] })
      void queryClient.invalidateQueries({ queryKey: ['series'] })
      toast.success(t('mapSeries.done', { folder: folder.relativePath, title: show.title }), {
        action: (
          <Link to="/series/$seriesId" params={{ seriesId: String(mapped.seriesId) }} className="text-sm underline underline-offset-4">
            {t('mapSeries.open')}
          </Link>
        ),
      })
      onClose()
    },
  })

  const search = (e: FormEvent) => {
    e.preventDefault()
    setTerm(draft.trim())
  }

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="max-h-[90vh] overflow-y-auto sm:max-w-2xl" data-testid="map-series-folder">
        <DialogHeader>
          <DialogTitle>{t('mapSeries.title', { folder: folder.relativePath })}</DialogTitle>
          <DialogDescription>{t('mapSeries.hint')}</DialogDescription>
        </DialogHeader>

        <form className="flex gap-2" onSubmit={search}>
          <Input value={draft} onChange={(e) => setDraft(e.target.value)} placeholder={t('mapSeries.placeholder')} aria-label={t('mapSeries.placeholder')} />
          <Button type="submit" variant="outline" disabled={!draft.trim()}>
            {t('common.search')}
          </Button>
        </form>

        {map.error && (
          <Alert variant="destructive">
            <AlertDescription>{map.error instanceof ApiError ? map.error.message : t('common.unexpectedError')}</AlertDescription>
          </Alert>
        )}

        <div className="flex flex-col gap-2">
          {results.isLoading && <Skeleton className="h-20 w-full" />}
          {results.data?.length === 0 && <p className="text-sm text-muted-foreground">{t('mapSeries.noResult')}</p>}
          {results.data?.map((show) => (
            <div key={`${show.tvdbId}-${show.tmdbId}`} className="flex items-center gap-3 rounded-lg border p-2">
              <Poster url={show.posterUrl} title={show.title} size="small" />
              <div className="min-w-0 flex-1">
                <p className="truncate font-medium">
                  {show.title} {show.year && <span className="font-normal text-muted-foreground">({show.year})</span>}
                </p>
                <p className="text-xs text-muted-foreground">
                  {[show.network, show.tvdbId ? `TVDB ${show.tvdbId}` : null].filter(Boolean).join(' · ')}
                </p>
                {show.existingSeriesId && (
                  <Badge variant="outline" className="mt-1">
                    {t('mapSeries.inLibrary')}
                  </Badge>
                )}
              </div>
              <Button type="button" size="sm" onClick={() => map.mutate(show)} disabled={map.isPending}>
                {show.existingSeriesId ? t('mapSeries.addVersion') : t('mapSeries.add')}
              </Button>
            </div>
          ))}
        </div>
      </DialogContent>
    </Dialog>
  )
}
