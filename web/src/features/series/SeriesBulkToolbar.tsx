import { useMemo, useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { ChevronDownIcon, EyeIcon, EyeOffIcon, Trash2Icon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import {
  ApiError,
  series as seriesApi,
  type NewItemMonitoring,
  type QualityProfile,
  type SeriesBulkDeleteResponse,
  type SeriesBulkUpdateRequest,
  type SeriesType,
  type Tag,
  type VersionSelector,
} from '@/api/client'
import { Button } from '@/components/ui/button'
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from '@/components/ui/dropdown-menu'
import { BulkDeleteDialog } from '@/features/library/BulkDeleteDialog'
import type { BulkReport } from '@/features/library/BulkResults'
import { BulkBar, TagsPopover, useTargetPhrase } from '@/features/library/BulkToolbar'
import { targetVersions, type VersionName } from '@/features/library/core'
import type { SeriesRow } from './seriesView'

const newItemOptions: NewItemMonitoring[] = ['all', 'none']
const seriesTypes: SeriesType[] = ['standard', 'anime', 'daily']

type Props = {
  library: SeriesRow[]
  shown: SeriesRow[]
  filtered: boolean
  selected: ReadonlySet<number>
  onSelect: (ids: number[]) => void
  onDeselect: (ids: number[]) => void
  onClear: () => void
  onDone: () => void
  names: VersionName[]
  profiles: QualityProfile[]
  tags: Tag[]
  onReport: (report: BulkReport) => void
}

/**
 * The film bulk bar on series: monitoring, quality profile, new seasons, tags and delete act on the chosen version of
 * each series; the series type on the series themselves. Every action is one request; the server reports series by series.
 */
export function SeriesBulkToolbar({ library, shown, filtered, selected, onSelect, onDeselect, onClear, onDone, names, profiles, tags, onReport }: Props) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [target, setTarget] = useState<VersionSelector>('default')
  const [deleting, setDeleting] = useState(false)
  const targetPhrase = useTargetPhrase(target)

  const selectedSeries = useMemo(() => library.filter((s) => selected.has(s.id)), [library, selected])
  const ids = useMemo(() => selectedSeries.map((s) => s.id), [selectedSeries])
  const covered = useMemo(() => selectedSeries.filter((s) => targetVersions(s, target).length > 0).length, [selectedSeries, target])
  const count = selectedSeries.length

  const invalidate = () => {
    void queryClient.invalidateQueries({ queryKey: ['series'] })
    void queryClient.invalidateQueries({ queryKey: ['seriesDetail'] })
  }
  const onError = (e: Error) => toast.error(e instanceof ApiError ? e.message : t('bulk.failed'))

  const update = useMutation({
    mutationFn: ({ body }: { body: Omit<SeriesBulkUpdateRequest, 'seriesIds' | 'versions'>; action: string }) => seriesApi.bulkUpdate({ seriesIds: ids, versions: target, ...body }),
    onSuccess: (response, { action }) => {
      onReport({ action, results: response.results })
      invalidate()
    },
    onError,
  })
  const onDeleted = (response: SeriesBulkDeleteResponse) => {
    onReport({ action: t('bulk.actions.delete', { target: targetPhrase }), results: response.results })
    onDeselect(response.results.filter((r) => r.outcome === 'seriesDeleted').map((r) => r.seriesId))
    invalidate()
  }

  const disabled = count === 0 || update.isPending

  return (
    <BulkBar
      count={count}
      total={library.length}
      shown={shown.length}
      filtered={filtered}
      onSelectAll={() => onSelect(library.map((s) => s.id))}
      onSelectShown={() => onSelect(shown.map((s) => s.id))}
      onClear={onClear}
      onDone={onDone}
      names={names}
      target={target}
      onTarget={setTarget}
      uncovered={count - covered}
    >
      <div className="flex flex-wrap items-center gap-2">
        <Button
          type="button"
          variant="outline"
          size="sm"
          disabled={disabled}
          onClick={() => update.mutate({ body: { monitored: true }, action: t('bulk.actions.monitor', { target: targetPhrase }) })}
          data-testid="bulk-monitor"
        >
          <EyeIcon />
          {t('bulk.monitor')}
        </Button>
        <Button
          type="button"
          variant="outline"
          size="sm"
          disabled={disabled}
          onClick={() => update.mutate({ body: { monitored: false }, action: t('bulk.actions.unmonitor', { target: targetPhrase }) })}
          data-testid="bulk-unmonitor"
        >
          <EyeOffIcon />
          {t('bulk.unmonitor')}
        </Button>

        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <Button type="button" variant="outline" size="sm" disabled={disabled} data-testid="bulk-profile">
              {t('bulk.profile')}
              <ChevronDownIcon />
            </Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="start">
            {profiles.map((profile) => (
              <DropdownMenuItem
                key={profile.id}
                onSelect={() => update.mutate({ body: { qualityProfileId: profile.id }, action: t('bulk.actions.profile', { target: targetPhrase, name: profile.name }) })}
                data-testid={`bulk-profile-${profile.id}`}
              >
                {profile.name}
              </DropdownMenuItem>
            ))}
          </DropdownMenuContent>
        </DropdownMenu>

        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <Button type="button" variant="outline" size="sm" disabled={disabled} data-testid="bulk-new-seasons">
              {t('seriesLibrary.bulk.newSeasons')}
              <ChevronDownIcon />
            </Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="start">
            {newItemOptions.map((value) => (
              <DropdownMenuItem
                key={value}
                onSelect={() =>
                  update.mutate({
                    body: { monitorNewItems: value },
                    action: t('seriesLibrary.bulk.actions.newSeasons', { target: targetPhrase, value: t(`seriesLibrary.newSeasons.${value}`) }),
                  })
                }
                data-testid={`bulk-new-seasons-${value}`}
              >
                {t(`seriesLibrary.newSeasons.${value}`)}
              </DropdownMenuItem>
            ))}
          </DropdownMenuContent>
        </DropdownMenu>

        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <Button type="button" variant="outline" size="sm" disabled={disabled} data-testid="bulk-series-type">
              {t('seriesLibrary.bulk.seriesType')}
              <ChevronDownIcon />
            </Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="start">
            {seriesTypes.map((value) => (
              <DropdownMenuItem
                key={value}
                onSelect={() => update.mutate({ body: { seriesType: value }, action: t('seriesLibrary.bulk.actions.seriesType', { value: t(`seriesType.${value}`), count }) })}
                data-testid={`bulk-series-type-${value}`}
              >
                {t(`seriesType.${value}`)}
              </DropdownMenuItem>
            ))}
          </DropdownMenuContent>
        </DropdownMenu>

        <TagsPopover
          tags={tags}
          disabled={disabled}
          onApply={(mode, tagIds) => update.mutate({ body: { tagIds, applyTags: mode }, action: t('bulk.actions.tags', { target: targetPhrase, mode: t(`bulk.tagsMode.${mode}`) }) })}
        />

        <Button type="button" variant="destructive" size="sm" className="ml-auto" disabled={disabled} onClick={() => setDeleting(true)} data-testid="bulk-delete">
          <Trash2Icon />
          {t('bulk.delete.button')}
        </Button>
      </div>

      <BulkDeleteDialog
        open={deleting}
        onOpenChange={setDeleting}
        movieIds={ids}
        target={target}
        targetPhrase={targetPhrase}
        onDeleted={onDeleted}
        kind="series"
        run={({ versions, deleteFiles, dryRun }) => seriesApi.bulkDelete({ seriesIds: ids, versions, deleteFiles, dryRun })}
      />
    </BulkBar>
  )
}
