import { type FormEvent, useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { ApiError, movies, profilesFor, quality, type MinimumAvailability, type Movie, type Version, type VersionMode, minimumAvailabilities } from '@/api/client'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'


/**
 * A film version's settings: name, mode, quality profile, minimum availability. Its folder, monitoring and constraints
 * have their own controls. Mounted only while open, so the fields start from the version every time.
 */
export function EditVersionDialog({ movie, version, onClose, onSaved }: { movie: Movie; version: Version; onClose: () => void; onSaved: () => void }) {
  const { t } = useTranslation()
  const profiles = useQuery({ queryKey: ['qualityProfiles'], queryFn: quality.profiles, select: profilesFor('movies') })
  const [name, setName] = useState(version.name)
  const [mode, setMode] = useState<VersionMode>(version.mode)
  const [profileId, setProfileId] = useState(version.qualityProfileId)
  const [availability, setAvailability] = useState<MinimumAvailability>(version.minimumAvailability ?? 'released')

  const save = useMutation({
    mutationFn: () => movies.updateVersion(movie.id, version.id, { name: name.trim(), mode, qualityProfileId: profileId, minimumAvailability: availability }),
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
        <form onSubmit={submit} className="flex flex-col gap-4" data-testid="edit-version">
          <DialogHeader>
            <DialogTitle>{t('versions.editTitle', { name: version.name })}</DialogTitle>
            <DialogDescription>{movie.title}</DialogDescription>
          </DialogHeader>
          <div className="grid gap-2">
            <Label htmlFor="edit-version-name">{t('versions.name')}</Label>
            <Input id="edit-version-name" value={name} onChange={(e) => setName(e.target.value)} autoFocus />
          </div>
          <div className="grid gap-2">
            <Label htmlFor="edit-version-mode">{t('versions.mode')}</Label>
            <Select value={mode} onValueChange={(value) => setMode(value as VersionMode)}>
              <SelectTrigger id="edit-version-mode" className="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="managed">{t('versions.modeManaged')}</SelectItem>
                <SelectItem value="oneShot">{t('versions.modeOneShot')}</SelectItem>
              </SelectContent>
            </Select>
          </div>
          <div className="grid gap-2">
            <Label htmlFor="edit-version-profile">{t('addMovie.qualityProfile')}</Label>
            <Select value={String(profileId)} onValueChange={(value) => setProfileId(Number(value))}>
              <SelectTrigger id="edit-version-profile" className="w-full">
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
          <div className="grid gap-2">
            <Label htmlFor="edit-version-availability">{t('versions.minAvailability')}</Label>
            <Select value={availability} onValueChange={(value) => setAvailability(value as MinimumAvailability)}>
              <SelectTrigger id="edit-version-availability" className="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {minimumAvailabilities.map((a) => (
                  <SelectItem key={a} value={a}>
                    {t(`availability.${a}`)}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
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
            <Button type="submit" disabled={save.isPending || name.trim() === ''} data-testid="edit-version-save">
              {t('common.save')}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
