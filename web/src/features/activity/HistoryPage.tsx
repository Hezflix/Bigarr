import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { history, type HistoryEventType } from '@/api/client'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import { HistoryTable } from './HistoryTable'

const types: HistoryEventType[] = ['grabbed', 'imported', 'upgraded', 'importFailed', 'downloadFailed', 'blocklisted', 'fileDeleted', 'renamed', 'defaultChanged']

export function HistoryPage() {
  const { t } = useTranslation()
  const [type, setType] = useState<'all' | HistoryEventType>('all')
  const query = useQuery({
    queryKey: ['history', 'all', type],
    queryFn: () => history.list({ limit: 200, eventType: type === 'all' ? undefined : type }),
  })

  return (
    <>
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="text-2xl font-semibold tracking-tight">{t('history.title')}</h1>
        <div className="ml-auto flex items-center gap-2">
          <Label htmlFor="history-type" className="text-muted-foreground">
            {t('history.filter')}
          </Label>
          <Select value={type} onValueChange={(v) => setType(v as 'all' | HistoryEventType)}>
            <SelectTrigger id="history-type" className="min-w-48">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">{t('history.all')}</SelectItem>
              {types.map((x) => (
                <SelectItem key={x} value={x}>
                  {t(`history.events.${x}`)}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
      </div>

      {query.isPending && (
        <div className="flex flex-col gap-2">
          <Skeleton className="h-8 w-full" />
          <Skeleton className="h-8 w-full" />
        </div>
      )}
      {query.error && (
        <Alert variant="destructive">
          <AlertDescription>{t('history.loadError')}</AlertDescription>
        </Alert>
      )}
      {query.data && query.data.length === 0 && <p className="text-sm text-muted-foreground">{t('history.empty')}</p>}
      {query.data && query.data.length > 0 && <HistoryTable events={query.data} />}
    </>
  )
}
