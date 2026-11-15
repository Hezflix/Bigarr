import { type FormEvent, useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { ApiError, profilesFor, quality, series as seriesApi, type Series, type SeriesVersion } from '@/api/client'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'

/**
 * A series version's settings: name, quality profile, season folders, new seasons. Monitoring, constraints and the
 * episodes' monitoring have their own controls. Mounted only while open, so the fields start from the version.
 */
export function EditSeriesVersionDialog({ series: s, version, onClose, onSaved }: { series: Series; version: SeriesVersion; onClose: () => void; onSaved: () => void }) {
  const { t } = useTranslation()
  const profiles = useQuery({ queryKey: ['qualityProfiles'], queryFn: quality.profiles, select: profilesFor('series') })
  const [name, setName] = useState(version.name)
  const [profileId, setProfileId] = useState(version.qualityProfileId)
  const [seasonFolder, setSeasonFolder] = useState(version.seasonFolder)
  const [monitorNew, setMonitorNew] = useState(version.monitorNewItems === 'all')

  const save = useMutation({
    mutationFn: () =>
      seriesApi.updateVersion(s.id, version.id, {
        name: name.trim(),
        qualityProfileId: profileId,
        seasonFolder,
        monitorNewItems: monitorNew ? 'all' : 'none',
        clearConstraints: false,
      }),
    onSuccess: () => {
      onSaved()
      onClose()
    },
  })
  const submit = (e: FormEvent) => {
    e.preventDefault()
    save.mutate()
  }

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent>
        <form onSubmit={submit} className="flex flex-col gap-4" data-testid="edit-series-version">
          <DialogHeader>
            <DialogTitle>{t('versions.editTitle', { name: version.name })}</DialogTitle>
            <DialogDescription>{s.title}</DialogDescription>
          </DialogHeader>
          <div className="grid gap-2">
            <Label htmlFor="edit-series-version-name">{t('seriesDetail.versionName')}</Label>
            <Input id="edit-series-version-name" value={name} onChange={(e) => setName(e.target.value)} autoFocus />
          </div>
          <div className="grid gap-2">
            <Label htmlFor="edit-series-version-profile">{t('addMovie.qualityProfile')}</Label>
            <Select value={String(profileId)} onValueChange={(value) => setProfileId(Number(value))}>
              <SelectTrigger id="edit-series-version-profile" className="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {profiles.data?.map((p) => (
                  <SelectItem key={p.id} value={String(p.id)}>
                    {p.name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <div className="flex flex-col gap-3">
            <div className="flex items-center gap-2">
              <Checkbox id="edit-series-season-folder" checked={seasonFolder} onCheckedChange={(checked) => setSeasonFolder(checked === true)} />
              <Label htmlFor="edit-series-season-folder" className="font-normal">
                {t('addSeries.seasonFolder')}
              </Label>
            </div>
            <div className="flex items-center gap-2">
              <Checkbox id="edit-series-monitor-new" checked={monitorNew} onCheckedChange={(checked) => setMonitorNew(checked === true)} />
              <Label htmlFor="edit-series-monitor-new" className="font-normal">
                {t('addSeries.monitorNewItems')}
              </Label>
            </div>
          </div>
          {save.error && (
            <Alert variant="destructive">
              <AlertDescription>{save.error instanceof ApiError ? save.error.message : t('common.unexpectedError')}</AlertDescription>
            </Alert>
          )}
          <DialogFooter>
            <Button type="button" variant="ghost" onClick={onClose}>
              {t('common.cancel')}
            </Button>
            <Button type="submit" disabled={save.isPending || name.trim() === ''} data-testid="edit-series-version-save">
              {t('common.save')}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
