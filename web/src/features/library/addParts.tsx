import type { FormEvent, ReactNode } from 'react'
import { Loader2Icon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { ApiError } from '@/api/client'
import { Poster } from '@/components/Poster'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'

/** The pieces of the one add form (`TitleForm`): the dialog around it, labelled fields, the version name and the error line. */

/**
 * The add form as a dialog: the title's poster, name and overview, the fields (`children`), then Cancel and the submit
 * button. Closing it (Esc, the cross, a click outside or Cancel) leaves the page as it was.
 */
export function AddDialog({
  posterUrl,
  title,
  year,
  overview,
  onCancel,
  onSubmit,
  submitLabel,
  submitTestId,
  submitDisabled,
  pending,
  children,
}: {
  posterUrl: string | null
  title: string
  year: number | null
  overview: string | null
  onCancel: () => void
  onSubmit: () => void
  submitLabel: string
  submitTestId: string
  submitDisabled: boolean
  pending: boolean
  children: ReactNode
}) {
  const { t } = useTranslation()
  const submit = (e: FormEvent) => {
    e.preventDefault()
    onSubmit()
  }
  return (
    <Dialog open onOpenChange={(open) => !open && onCancel()}>
      <DialogContent className="max-h-[90dvh] overflow-y-auto sm:max-w-2xl" data-testid="add-dialog" {...(overview ? {} : { 'aria-describedby': undefined })}>
        <form onSubmit={submit} className="flex flex-col gap-6">
          <div className="flex items-start gap-4 pr-6">
            <Poster url={posterUrl} title={title} size="small" className="shrink-0" />
            <DialogHeader className="min-w-0 gap-1 text-left">
              <DialogTitle className="flex flex-wrap items-baseline gap-2 leading-snug">
                {title}
                {year && <span className="text-sm font-normal text-muted-foreground">({year})</span>}
              </DialogTitle>
              {overview && <DialogDescription className="line-clamp-4">{overview}</DialogDescription>}
            </DialogHeader>
          </div>

          <div className="flex flex-col gap-4">{children}</div>

          <DialogFooter>
            <Button type="button" variant="ghost" onClick={onCancel}>
              {t('common.cancel')}
            </Button>
            <Button type="submit" data-testid={submitTestId} disabled={pending || submitDisabled}>
              {pending && <Loader2Icon className="animate-spin" />}
              {submitLabel}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}

/**
 * A labelled form field. The rows pack at the top (`content-start`): in a two-column row, the field with a hint is taller than its neighbour,
 * and without it the neighbour's label and control would be pushed down to fill the extra height instead of lining up at the top.
 */
export function FormField({ id, label, hint, children }: { id: string; label: string; hint?: ReactNode; children: ReactNode }) {
  return (
    <div className="grid grid-cols-1 content-start gap-2">
      <Label htmlFor={id}>{label}</Label>
      {children}
      {hint && <p className="text-xs text-muted-foreground">{hint}</p>}
    </div>
  )
}

/** A select over rows with a numeric id; empty until one is chosen. */
export function IdSelectField({ id, label, value, onChange, options }: { id: string; label: string; value: number | ''; onChange: (id: number) => void; options: { id: number; label: string }[] | undefined }) {
  return (
    <FormField id={id} label={label}>
      <Select value={value === '' ? undefined : String(value)} onValueChange={(v) => onChange(Number(v))}>
        <SelectTrigger id={id} className="w-full">
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          {options?.map((option) => (
            <SelectItem key={option.id} value={String(option.id)}>
              {option.label}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
    </FormField>
  )
}

/** The version's name; left empty, it takes the one shown as placeholder (derived from the profile). */
export function VersionNameField({ id, value, onChange, placeholder }: { id: string; value: string; onChange: (value: string) => void; placeholder: string }) {
  const { t } = useTranslation()
  return (
    <FormField id={id} label={t('addMovie.versionName')} hint={t('addMovie.versionNameHint')}>
      <Input id={id} value={value} onChange={(e) => onChange(e.target.value)} placeholder={placeholder} />
    </FormField>
  )
}

export function AddError({ error }: { error: unknown }) {
  const { t } = useTranslation()
  if (!error) return null
  return (
    <Alert variant="destructive">
      <AlertDescription>{error instanceof ApiError ? error.message : t('common.unexpectedError')}</AlertDescription>
    </Alert>
  )
}
