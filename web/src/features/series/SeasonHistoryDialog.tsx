import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { HistoryIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { history } from '@/api/client'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Skeleton } from '@/components/ui/skeleton'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { HistoryTable } from '@/features/activity/HistoryTable'
import { foldGrabs } from './seasonHistory'

/** The history of a season, whatever the version: an icon in the season's header, the journal in a dialog. */
export function SeasonHistoryButton({ seriesId, seasonNumber, seasonName }: { seriesId: number; seasonNumber: number; seasonName: string }) {
  const { t } = useTranslation()
  const [open, setOpen] = useState(false)
  const label = t('seasonHistory.open', { season: seasonName })
  return (
    <>
      <Tooltip>
        <TooltipTrigger asChild>
          <Button type="button" variant="ghost" size="icon" className="size-7" aria-label={label} onClick={() => setOpen(true)} data-testid="season-history">
            <HistoryIcon aria-hidden="true" />
          </Button>
        </TooltipTrigger>
        <TooltipContent>{label}</TooltipContent>
      </Tooltip>
      {open && <SeasonHistoryDialog seriesId={seriesId} seasonNumber={seasonNumber} seasonName={seasonName} onClose={() => setOpen(false)} />}
    </>
  )
}

function SeasonHistoryDialog({ seriesId, seasonNumber, seasonName, onClose }: { seriesId: number; seasonNumber: number; seasonName: string; onClose: () => void }) {
  const { t } = useTranslation()
  const events = useQuery({
    queryKey: ['history', 'season', seriesId, seasonNumber],
    queryFn: () => history.list({ seriesId, seasonNumber, limit: 500 }),
    select: foldGrabs,
  })
  return (
    <Dialog open onOpenChange={(next) => !next && onClose()}>
      <DialogContent className="flex max-h-[90vh] flex-col sm:max-w-5xl" data-testid="season-history-dialog">
        <DialogHeader className="pr-8 text-left">
          <DialogTitle className="text-base">{t('seasonHistory.title', { season: seasonName })}</DialogTitle>
          <DialogDescription className="text-xs">{t('seasonHistory.hint')}</DialogDescription>
        </DialogHeader>
        <div className="min-h-32 overflow-y-auto">
          {events.isPending ? (
            <Skeleton className="h-16 w-full" />
          ) : events.data && events.data.length > 0 ? (
            <HistoryTable events={events.data} showMovie={false} showEpisode />
          ) : (
            <p className="text-sm text-muted-foreground">{events.isError ? t('common.unexpectedError') : t('seasonHistory.empty')}</p>
          )}
        </div>
      </DialogContent>
    </Dialog>
  )
}
