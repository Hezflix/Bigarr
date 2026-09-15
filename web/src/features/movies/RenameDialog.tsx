import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { cn } from 'cn'
import { PencilLineIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ApiError, rename, type Movie } from '@/api/client'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle, DialogTrigger } from '@/components/ui/dialog'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'

/** Preview then apply the current file name template to a film's files. */
export function RenameDialog({ movie, onRenamed }: { movie: Movie; onRenamed: () => void }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [open, setOpen] = useState(false)
  const preview = useQuery({ queryKey: ['rename', movie.id], queryFn: () => rename.preview(movie.id), enabled: open, staleTime: 0 })
  const changed = preview.data?.filter((r) => r.changed) ?? []

  const apply = useMutation({
    mutationFn: () => rename.apply(movie.id, changed.map((r) => r.mediaFileId)),
    onSuccess: async (results) => {
      const done = results.filter((r) => r.renamed).length
      results.filter((r) => r.error).forEach((r) => toast.error(r.error))
      if (done > 0) toast.success(t('rename.done', { count: done }))
      await queryClient.invalidateQueries({ queryKey: ['rename', movie.id] })
      onRenamed()
      if (results.every((r) => r.renamed)) setOpen(false)
    },
    onError: (e) => toast.error(e instanceof ApiError ? e.message : t('rename.failed')),
  })

  const hasFiles = movie.versions.some((v) => v.file)

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger asChild>
        <Button type="button" variant="outline" disabled={!hasFiles} data-testid="rename-files">
          <PencilLineIcon />
          {t('movie.renameFiles')}
        </Button>
      </DialogTrigger>
      <DialogContent className="sm:max-w-3xl">
        <DialogHeader>
          <DialogTitle>{t('rename.title')}</DialogTitle>
          <DialogDescription>{t('rename.hint')}</DialogDescription>
        </DialogHeader>

        {preview.isPending && (
          <div className="flex flex-col gap-2">
            <Skeleton className="h-8 w-full" />
            <Skeleton className="h-8 w-2/3" />
          </div>
        )}
        {preview.error && (
          <Alert variant="destructive">
            <AlertDescription>{preview.error instanceof ApiError ? preview.error.message : t('common.unexpectedError')}</AlertDescription>
          </Alert>
        )}
        {preview.data && preview.data.length === 0 && <p className="text-sm text-muted-foreground">{t('rename.empty')}</p>}
        {preview.data && preview.data.length > 0 && (
          <div className="overflow-x-auto rounded-lg border">
            <Table data-testid="rename-preview">
              <TableHeader>
                <TableRow>
                  <TableHead>{t('rename.version')}</TableHead>
                  <TableHead>{t('rename.current')}</TableHead>
                  <TableHead>{t('rename.new')}</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {preview.data.map((row) => (
                  <TableRow key={row.mediaFileId} className={cn(!row.changed && 'text-muted-foreground')}>
                    <TableCell className="align-top">
                      <Badge variant="secondary">{row.versionName}</Badge>
                    </TableCell>
                    <TableCell className="max-w-[20rem] align-top whitespace-normal break-all text-xs">{row.currentRelativePath}</TableCell>
                    <TableCell className="max-w-[20rem] align-top whitespace-normal break-all text-xs">
                      {row.changed ? <span className="font-medium text-foreground">{row.newRelativePath}</span> : <span>{t('rename.unchanged')}</span>}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        )}
        {preview.data && preview.data.length > 0 && changed.length === 0 && <p className="text-sm text-muted-foreground">{t('rename.nothing')}</p>}

        <DialogFooter>
          <Button type="button" variant="ghost" onClick={() => setOpen(false)}>
            {t('common.close')}
          </Button>
          <Button type="button" onClick={() => apply.mutate()} disabled={changed.length === 0 || apply.isPending} data-testid="rename-apply">
            {apply.isPending ? t('rename.renaming') : t('rename.applyCount', { count: changed.length })}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
