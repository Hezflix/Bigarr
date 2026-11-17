import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { Release, Version } from '@/api/client'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { type GrabOverride, grabOverride, overriddenParts } from '@/features/search/grabOverride'
import { OverrideFields } from '@/features/search/OverrideFields'

/**
 * Override and grab a film release (Sonarr's "Override and add to download queue"): the version it goes to, the quality
 * and the languages it really has when the title reads wrong. Only what differs from the parse is sent; the import then
 * takes it over the title and the file name.
 */
export function GrabOverrideDialog({
  release,
  versions,
  defaultVersionId,
  onGrab,
  onClose,
}: {
  release: Release
  versions: Version[]
  defaultVersionId: number | undefined
  onGrab: (versionId: number, override: GrabOverride) => void
  onClose: () => void
}) {
  const { t } = useTranslation()
  const [versionId, setVersionId] = useState(defaultVersionId ?? versions[0]?.id ?? 0)
  const [qualityId, setQualityId] = useState(release.qualityId)
  const [languages, setLanguages] = useState(release.languages)
  const override = grabOverride(release, { qualityId, languages })
  const parts = overriddenParts(override)
  const version = versions.find((v) => v.id === versionId)
  const busy = version?.status === 'downloading'

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="sm:max-w-xl" data-testid="grab-override-dialog">
        <DialogHeader>
          <DialogTitle>{t('grabOverride.title')}</DialogTitle>
          <DialogDescription className="break-all">{release.title}</DialogDescription>
        </DialogHeader>
        <p className="text-sm text-muted-foreground">{t('grabOverride.hintFilm')}</p>
        <div className="grid gap-2">
          <Label htmlFor="grab-override-version">{t('grabOverride.version')}</Label>
          <Select value={String(versionId)} onValueChange={(v) => setVersionId(Number(v))}>
            <SelectTrigger id="grab-override-version" className="w-full" data-testid="grab-override-version">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {versions.map((v) => (
                <SelectItem key={v.id} value={String(v.id)} disabled={v.status === 'downloading'}>
                  {v.name}
                  {v.status === 'downloading' && <span className="text-xs text-muted-foreground">{t('search.activeDownload')}</span>}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
        <OverrideFields
          parsedQuality={release.quality}
          parsedLanguages={release.languages}
          qualityId={qualityId}
          languages={languages}
          onQuality={setQualityId}
          onLanguages={setLanguages}
          idPrefix="grab-override"
        />
        <DialogFooter className="items-center sm:justify-between">
          <span className="text-xs text-muted-foreground" data-testid="grab-override-summary">
            {parts.length === 0 ? t('grabOverride.asParsed') : t('grabOverride.changed', { parts: parts.map((p) => t(`grabOverride.part.${p}`)).join(', ') })}
          </span>
          <div className="flex gap-2">
            <Button type="button" variant="ghost" onClick={onClose}>
              {t('common.cancel')}
            </Button>
            <Button
              type="button"
              disabled={!version || busy}
              onClick={() => {
                onGrab(versionId, override)
                onClose()
              }}
              data-testid="grab-override-go"
            >
              {t('grabOverride.grab', { version: version?.name ?? '' })}
            </Button>
          </div>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
