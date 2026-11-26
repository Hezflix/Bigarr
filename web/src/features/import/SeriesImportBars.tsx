import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { ListOrderedIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { series as seriesApi, type SeriesSummary } from '@/api/client'
import { episodeCode } from '@/lib/episodeCode'
import { QualitySelect } from '@/components/QualitySelect'
import { SeasonSelect } from '@/components/SeasonSelect'
import { seasonOptions } from '@/components/seasonOptions'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import { renumber, type Renumbered } from '@/features/import/seriesImportModel'
import { TitlePicker } from '@/features/import/TitlePicker'
import { VersionSelect } from '@/features/import/VersionSelect'

/** The series and version every file goes to; a row says otherwise only when it differs. */
export function SeriesBatchBar({
  library,
  seriesId,
  versionId,
  guess,
  onSeries,
  onVersion,
}: {
  library: SeriesSummary[]
  seriesId: number | null
  versionId: number | null
  guess: { title: string | null; first: number[] }
  onSeries: (id: number | null) => void
  onVersion: (versionId: number) => void
}) {
  const { t } = useTranslation()
  return (
    <div className="flex flex-wrap items-center gap-x-3 gap-y-2 rounded-lg border bg-muted/30 px-3 py-2 text-sm" data-testid="series-import-batch">
      <span className="font-medium">{t('seriesImport.batch.label')}</span>
      <span className="text-muted-foreground">{t('seriesImport.series')}</span>
      <TitlePicker kind="series" items={library} value={seriesId} guess={{ title: guess.title, year: null, first: guess.first }} onChange={onSeries} testId="series-import-batch-series" />
      <span className="text-muted-foreground">{t('manualImport.version')}</span>
      <VersionSelect versions={library.find((s) => s.id === seriesId)?.versions} value={versionId} onChange={onVersion} placeholder={t('manualImport.noVersion')} testId="series-import-batch-version" />
      <span className="text-xs text-muted-foreground">{t('seriesImport.batch.note')}</span>
    </div>
  )
}

export type BulkRow = { path: string; name: string }

/** What the bar does to the ticked rows; the dialog applies it. */
export type BulkAction =
  | { kind: 'series'; seriesId: number | null }
  | { kind: 'season'; season: number }
  | { kind: 'version'; versionId: number }
  | { kind: 'quality'; qualityId: number }
  | { kind: 'group'; group: string }
  | { kind: 'renumber'; result: Renumbered[] }

/**
 * Shown while rows are ticked: sets their series, season, version, quality or group at once, or renumbers them. Season,
 * version and renumbering need the ticked rows to share one series (`shared`).
 */
export function SeriesSelectionBar({
  rows,
  incomplete,
  shared,
  library,
  hint,
  onAction,
  onClear,
}: {
  rows: BulkRow[]
  /** Where renumbering starts: the season the other files are in, after its last episode they hold. */
  hint: { season: number | null; start: number }
  /** Ticked rows that still lack a version or episodes: they are not imported. */
  incomplete: number
  shared: number | null
  library: SeriesSummary[]
  onAction: (action: BulkAction) => void
  onClear: () => void
}) {
  const { t } = useTranslation()
  const [group, setGroup] = useState('')
  const show = library.find((s) => s.id === shared)
  const episodes = useQuery({ queryKey: ['episodes', shared], queryFn: () => seriesApi.episodes(shared as number), enabled: shared !== null })

  return (
    <div className="sticky top-0 z-10 grid gap-1 rounded-lg border bg-popover px-3 py-2 text-sm shadow-md" data-testid="series-import-selection">
      <div className="flex flex-wrap items-center gap-2">
        <Badge>{t('seriesImport.bulk.selected', { count: rows.length })}</Badge>
        <TitlePicker kind="series" items={library} value={null} guess={{ title: null, year: null, first: shared === null ? [] : [shared] }} onChange={(seriesId) => onAction({ kind: 'series', seriesId })} placeholder={t('seriesImport.bulk.series')} testId="series-import-bulk-series" />
        <SeasonSelect
          size="sm"
          className="min-w-28"
          seasons={(episodes.data ?? []).map((e) => e.seasonNumber)}
          value={null}
          onChange={(season) => onAction({ kind: 'season', season })}
          placeholder={t('seriesImport.bulk.season')}
          testId="series-import-bulk-season"
          disabled={shared === null}
        />
        <VersionSelect versions={show?.versions} value={null} onChange={(versionId) => onAction({ kind: 'version', versionId })} placeholder={t('seriesImport.bulk.version')} testId="series-import-bulk-version" />
        <QualitySelect value={null} onChange={(qualityId) => onAction({ kind: 'quality', qualityId })} placeholder={t('seriesImport.bulk.quality')} size="sm" className="min-w-28" testId="series-import-bulk-quality" />
        <form
          className="flex items-center gap-1"
          onSubmit={(e) => {
            e.preventDefault()
            onAction({ kind: 'group', group })
            setGroup('')
          }}
        >
          <Input value={group} onChange={(e) => setGroup(e.target.value)} placeholder={t('seriesImport.bulk.group')} aria-label={t('seriesImport.bulk.groupLabel')} className="h-8 w-28 text-xs" data-testid="series-import-bulk-group" />
        </form>
        <RenumberButton key={`${hint.season}-${hint.start}`} rows={rows} seriesId={shared} hint={hint} onApply={(result) => onAction({ kind: 'renumber', result })} />
        <Button type="button" size="sm" variant="ghost" className="ml-auto" onClick={onClear}>
          {t('seriesImport.bulk.clear')}
        </Button>
      </div>
      {(shared === null || incomplete > 0) && (
        <p className="text-xs text-muted-foreground">
          {shared === null && t('seriesImport.bulk.mixed')} {incomplete > 0 && t('seriesImport.bulk.incomplete', { count: incomplete })}
        </p>
      )}
    </div>
  )
}

/** Season and first episode; the ticked files get one episode each in name order, previewed before applying. */
function RenumberButton({ rows, seriesId, hint, onApply }: { rows: BulkRow[]; seriesId: number | null; hint: { season: number | null; start: number }; onApply: (result: Renumbered[]) => void }) {
  const { t } = useTranslation()
  const [open, setOpen] = useState(false)
  const episodes = useQuery({ queryKey: ['episodes', seriesId], queryFn: () => seriesApi.episodes(seriesId as number), enabled: seriesId !== null })
  const all = episodes.data ?? []
  const seasons = seasonOptions(all.map((e) => e.seasonNumber))
  const [season, setSeason] = useState<number | null>(null)
  const [start, setStart] = useState(String(hint.start))
  const chosenSeason = season ?? (hint.season !== null && seasons.includes(hint.season) ? hint.season : null) ?? seasons.find((n) => n > 0) ?? seasons[0] ?? null
  const first = Number.parseInt(start, 10)
  const preview = chosenSeason === null || !Number.isFinite(first) || first < 0 ? [] : renumber(rows, all, chosenSeason, first)
  const titleOf = (id: number | null) => all.find((e) => e.id === id)?.title
  const nameOf = (path: string) => rows.find((r) => r.path === path)?.name ?? path

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button type="button" size="sm" variant="outline" className="gap-1" disabled={seriesId === null} data-testid="series-import-renumber">
          <ListOrderedIcon />
          {t('seriesImport.bulk.renumber')}
        </Button>
      </PopoverTrigger>
      <PopoverContent align="start" className="w-[28rem]">
        <p className="mb-3 text-sm font-medium">{t('seriesImport.bulk.renumberTitle', { count: rows.length })}</p>
        <div className="mb-3 flex flex-wrap items-center gap-2 text-sm">
          <Label htmlFor="renumber-season" className="text-muted-foreground">
            {t('seriesImport.pickSeason')}
          </Label>
          <SeasonSelect id="renumber-season" size="sm" className="min-w-28" seasons={seasons} value={chosenSeason} onChange={setSeason} />
          <Label htmlFor="renumber-start" className="text-muted-foreground">
            {t('seriesImport.bulk.fromEpisode')}
          </Label>
          <Input id="renumber-start" value={start} onChange={(e) => setStart(e.target.value)} inputMode="numeric" className="h-8 w-16 tabular-nums" data-testid="series-import-renumber-start" />
        </div>
        <div className="grid max-h-56 gap-1 overflow-y-auto rounded-md border p-2 font-mono text-xs" data-testid="series-import-renumber-preview">
          {preview.map((r) => (
            <span key={r.path} className={r.episodeId === null ? 'text-destructive' : undefined}>
              {nameOf(r.path)} → {episodeCode(r.seasonNumber, r.episodeNumber)}
              {r.episodeId === null ? ` · ${t('seriesImport.bulk.pastEnd')}` : titleOf(r.episodeId) ? ` · ${titleOf(r.episodeId)}` : ''}
            </span>
          ))}
        </div>
        <div className="mt-3 flex justify-end">
          <Button
            type="button"
            size="sm"
            disabled={preview.length === 0 || preview.every((r) => r.episodeId === null)}
            onClick={() => {
              onApply(preview)
              setOpen(false)
            }}
            data-testid="series-import-renumber-apply"
          >
            {t('seriesImport.bulk.apply')}
          </Button>
        </div>
      </PopoverContent>
    </Popover>
  )
}
