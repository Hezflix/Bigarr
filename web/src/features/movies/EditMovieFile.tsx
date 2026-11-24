import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { PencilIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { movies, type Version } from '@/api/client'
import { Button } from '@/components/ui/button'
import { EditFileDialog } from '@/features/library/EditFileDialog'

/** The pencil of a version's file: its release group, edition, quality and languages, as Radarr's "edit file". */
export function EditMovieFileButton({ movieId, version, onChanged }: { movieId: number; version: Version; onChanged: () => void }) {
  const { t } = useTranslation()
  const [open, setOpen] = useState(false)
  const save = useMutation({
    mutationFn: (body: Parameters<typeof movies.updateFile>[2]) => movies.updateFile(movieId, version.id, body),
    onSuccess: () => {
      toast.success(t('editFile.saved'))
      setOpen(false)
      onChanged()
    },
  })
  const file = version.file
  if (!file) return null

  return (
    <>
      <Button
        type="button"
        size="icon"
        variant="ghost"
        className="size-6 text-muted-foreground"
        onClick={() => {
          save.reset()
          setOpen(true)
        }}
        aria-label={t('editFile.open')}
        title={t('editFile.open')}
        data-testid="edit-file"
      >
        <PencilIcon className="size-3.5" />
      </Button>
      {open && (
        <EditFileDialog
          path={file.path}
          file={{ releaseGroup: file.releaseGroup, edition: file.edition, qualityId: file.qualityId, languages: file.languages }}
          pending={save.isPending}
          error={save.error}
          onSave={(change) => save.mutate(change)}
          onClose={() => setOpen(false)}
        />
      )}
    </>
  )
}
