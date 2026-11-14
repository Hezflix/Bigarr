import { useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ApiError, movies, quality, queue, slugify, type Movie, type Release, profilesFor} from '@/api/client'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'

/** Escapes a parsed edition into a literal regex the constraint can hold. */
function escapeRegex(value: string): string {
  return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
}

/**
 * "Grab as an alternative version": a one-shot version is created next to the others, then the
 * release is grabbed for it. Two calls from here; the version is removed again when the grab is refused.
 */
export function OneShotGrabDialog({ movie, release, defaultProfileId, onClose }: { movie: Movie; release: Release; defaultProfileId: number; onClose: () => void }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const profiles = useQuery({ queryKey: ['qualityProfiles'], queryFn: quality.profiles, select: profilesFor('movies') })
  const suggested = slugify(release.edition ?? release.releaseGroup ?? release.quality) || 'alternate'
  const [name, setName] = useState(suggested)
  const [profileId, setProfileId] = useState(defaultProfileId)
  const [edition, setEdition] = useState(release.edition ? escapeRegex(release.edition) : '')
  const [requireGroup, setRequireGroup] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const run = useMutation({
    mutationFn: async () => {
      const version = await movies.addVersion(movie.id, {
        name: name.trim(),
        mode: 'oneShot',
        qualityProfileId: profileId,
        monitored: true,
        constraints:
          edition.trim() || (requireGroup && release.releaseGroup)
            ? { edition: edition.trim() || null, releaseGroup: requireGroup ? release.releaseGroup : null, requiredCustomFormatIds: null, forbiddenCustomFormatIds: null }
            : null,
      })
      try {
        const grabbed = await queue.grab({
          versionId: version.id,
          indexerId: release.indexerId,
          guid: release.guid,
          title: release.title,
          downloadUrl: release.downloadUrl,
          magnetUrl: release.magnetUrl,
          infoHash: release.infoHash,
          size: release.size,
          seeders: release.seeders,
          flags: release.flags,
          protocol: release.protocol,
          indexer: release.indexer,
          publishDate: release.publishDate,
          infoUrl: release.infoUrl,
        })
        return { version, grabbed }
      } catch (e) {
        await movies.deleteVersion(movie.id, version.id).catch(() => undefined)
        throw e instanceof ApiError ? new ApiError(e.status, `${e.message} ${t('oneShot.rolledBack')}`, e.problem) : e
      }
    },
    onSuccess: async ({ version }) => {
      toast.success(t('oneShot.done', { name: version.name, title: release.title }))
      await queryClient.invalidateQueries({ queryKey: ['movie', movie.id] })
      await queryClient.invalidateQueries({ queryKey: ['movies'] })
      await queryClient.invalidateQueries({ queryKey: ['queue'] })
      await queryClient.invalidateQueries({ queryKey: ['search', movie.id] })
      onClose()
    },
    onError: (e) => setError(e instanceof ApiError ? e.message : t('oneShot.failed')),
  })

  const submit = (e: FormEvent) => {
    e.preventDefault()
    setError(null)
    run.mutate()
  }

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="sm:max-w-lg" data-testid="oneshot-dialog">
        <form onSubmit={submit}>
          <DialogHeader>
            <DialogTitle>{t('oneShot.title')}</DialogTitle>
            <DialogDescription className="break-all">{release.title}</DialogDescription>
          </DialogHeader>

          <div className="my-6 grid gap-4">
            <p className="text-sm text-muted-foreground">{t('oneShot.hint')}</p>
            <div className="grid gap-2">
              <Label htmlFor="oneshot-name">{t('oneShot.name')}</Label>
              <Input id="oneshot-name" value={name} onChange={(e) => setName(e.target.value)} autoFocus data-testid="oneshot-name" />
              <span className="text-xs text-muted-foreground">{t('oneShot.nameHint')}</span>
            </div>
            <div className="grid gap-2">
              <Label htmlFor="oneshot-profile">{t('oneShot.profile')}</Label>
              <Select value={String(profileId)} onValueChange={(v) => setProfileId(Number(v))}>
                <SelectTrigger id="oneshot-profile" className="w-full">
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
              <Label htmlFor="oneshot-edition">{t('oneShot.edition')}</Label>
              <Input id="oneshot-edition" value={edition} onChange={(e) => setEdition(e.target.value)} placeholder="director'?s cut" />
              <span className="text-xs text-muted-foreground">{t('oneShot.editionHint')}</span>
            </div>
            {release.releaseGroup && (
              <label className="flex items-center gap-2 text-sm font-normal">
                <Checkbox checked={requireGroup} onCheckedChange={(v) => setRequireGroup(v === true)} />
                {t('oneShot.requireGroup', { group: release.releaseGroup })}
              </label>
            )}
            {error && (
              <Alert variant="destructive">
                <AlertDescription>{error}</AlertDescription>
              </Alert>
            )}
          </div>

          <DialogFooter>
            <Button type="button" variant="ghost" onClick={onClose}>
              {t('common.cancel')}
            </Button>
            <Button type="submit" disabled={run.isPending || !slugify(name)} data-testid="oneshot-submit">
              {run.isPending ? t('oneShot.creating') : t('oneShot.create')}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
