import { type FormEvent, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ApiError } from '@/api/client'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { OverrideFields } from '@/features/search/OverrideFields'

/** What a file on disk is said to be. The edition is a film's only. */
export type FileFacts = { releaseGroup: string | null; edition?: string | null; qualityId: number; languages: string[] }

/** What the dialog sends: every field, a blank text clears it. */
export type FileFactsChange = { releaseGroup: string; edition?: string; qualityId: number; languages: string[] }

/**
 * Radarr's and Sonarr's "edit file": the release group (and the film's edition), the quality and the languages of a file,
 * when its name reads wrong. Custom formats and the cutoff follow at once; the file keeps its name until a rename.
 */
export function EditFileDialog({
  path,
  file,
  pending,
  error,
  onSave,
  onClose,
}: {
  path: string
  file: FileFacts
  pending: boolean
  error: unknown
  onSave: (change: FileFactsChange) => void
  onClose: () => void
}) {
  const { t } = useTranslation()
  const withEdition = file.edition !== undefined
  const [group, setGroup] = useState(file.releaseGroup ?? '')
  const [edition, setEdition] = useState(file.edition ?? '')
  const [qualityId, setQualityId] = useState(file.qualityId)
  const [languages, setLanguages] = useState(file.languages)

  const submit = (event: FormEvent) => {
    event.preventDefault()
    onSave({ releaseGroup: group, ...(withEdition ? { edition } : {}), qualityId, languages })
  }

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="sm:max-w-xl" data-testid="edit-file-dialog">
        <form onSubmit={submit} className="grid gap-4">
          <DialogHeader>
            <DialogTitle>{t('editFile.title')}</DialogTitle>
            <DialogDescription className="break-all font-mono text-xs">{path}</DialogDescription>
          </DialogHeader>
          <div className={withEdition ? 'grid gap-4 sm:grid-cols-2' : 'grid gap-4'}>
            <div className="grid content-start gap-2">
              <Label htmlFor="edit-file-group">{t('editFile.releaseGroup')}</Label>
              <Input id="edit-file-group" value={group} onChange={(e) => setGroup(e.target.value)} placeholder={t('editFile.none')} autoComplete="off" maxLength={255} data-testid="edit-file-group" />
            </div>
            {withEdition && (
              <div className="grid content-start gap-2">
                <Label htmlFor="edit-file-edition">{t('editFile.edition')}</Label>
                <Input id="edit-file-edition" value={edition} onChange={(e) => setEdition(e.target.value)} placeholder={t('editFile.none')} autoComplete="off" maxLength={255} data-testid="edit-file-edition" />
              </div>
            )}
          </div>
          <OverrideFields
            qualityId={qualityId}
            languages={languages}
            onQuality={setQualityId}
            onLanguages={setLanguages}
            idPrefix="edit-file"
            texts={{ qualityPlaceholder: t('editFile.none'), languagesPlaceholder: t('editFile.noLanguage'), clearLanguages: t('editFile.clearLanguages') }}
          />
          <p className="text-xs text-muted-foreground">{t('editFile.hint')}</p>
          {error != null && (
            <Alert variant="destructive">
              <AlertDescription>{error instanceof ApiError ? error.message : t('common.unexpectedError')}</AlertDescription>
            </Alert>
          )}
          <DialogFooter>
            <Button type="button" variant="ghost" onClick={onClose}>
              {t('common.cancel')}
            </Button>
            <Button type="submit" disabled={pending} data-testid="edit-file-save">
              {t('common.save')}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
