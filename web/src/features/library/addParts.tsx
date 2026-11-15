import { useEffect, useState, type FormEvent, type ReactNode } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Loader2Icon, SearchIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { ApiError, profilesFor, quality, rootFolders, rootsOfKind } from '@/api/client'
import { Poster } from '@/components/Poster'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'

/** What adding a film and adding a series share: the lookup box, a result card, the dialog around the form, and its profile, root folder and version name. */

/** The search box of a lookup, with what the request is doing below it. */
export function LookupSearch({ testId, placeholder, value, onChange, fetching, failed }: { testId: string; placeholder: string; value: string; onChange: (value: string) => void; fetching: boolean; failed: boolean }) {
  const { t } = useTranslation()
  return (
    <>
      <div className="relative max-w-xl">
        <SearchIcon className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground" />
        <Input data-testid={testId} className="pl-9" placeholder={placeholder} value={value} onChange={(e) => onChange(e.target.value)} autoFocus />
        {fetching && <Loader2Icon className="absolute top-1/2 right-3 size-4 -translate-y-1/2 animate-spin text-muted-foreground" />}
      </div>

      {fetching && <p className="text-sm text-muted-foreground">{t('lookup.searching')}</p>}
      {failed && (
        <Alert variant="destructive" className="max-w-xl">
          <AlertDescription>{t('lookup.failed')}</AlertDescription>
        </Alert>
      )}
    </>
  )
}

/** One lookup result: poster, title and year, then the kind's badge and second line, the overview, and the action (`children`). */
export function LookupResultCard({
  testId,
  posterUrl,
  title,
  year,
  badge,
  subtitle,
  overview,
  children,
}: {
  testId: string
  posterUrl: string | null
  title: string
  year: number | null
  badge?: ReactNode
  subtitle?: ReactNode
  overview: string | null
  children: ReactNode
}) {
  return (
    <Card data-testid={testId} className="py-4">
      <CardContent className="flex items-start gap-4">
        <Poster url={posterUrl} title={title} size="small" />
        <div className="flex min-w-0 flex-1 flex-col gap-1">
          <div className="flex flex-wrap items-baseline gap-2">
            <span className="font-medium">{title}</span>
            {year && <span className="text-sm text-muted-foreground">({year})</span>}
            {badge}
          </div>
          {subtitle}
          {overview && <p className="line-clamp-3 text-sm text-muted-foreground">{overview}</p>}
        </div>
        {children}
      </CardContent>
    </Card>
  )
}

/**
 * The add form of a lookup result, as a dialog over the results: the poster, title and overview of the result, the fields
 * (`children`), then Cancel and the submit button. Closing it (Esc, the cross, a click outside or Cancel) leaves the search as it was.
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

/** The quality profiles and root folders of one kind, with the first of each chosen as soon as they load. */
export function useAddTargets(kind: 'movies' | 'series') {
  const profiles = useQuery({ queryKey: ['qualityProfiles'], queryFn: quality.profiles, select: profilesFor(kind) })
  const roots = useQuery({ queryKey: ['rootFolders'], queryFn: rootFolders.list, select: rootsOfKind(kind) })
  const [profileId, setProfileId] = useState<number | ''>('')
  const [rootId, setRootId] = useState<number | ''>('')

  useEffect(() => {
    if (profileId === '' && profiles.data?.length) setProfileId(profiles.data[0].id)
  }, [profiles.data, profileId])
  useEffect(() => {
    if (rootId === '' && roots.data?.length) setRootId(roots.data[0].id)
  }, [roots.data, rootId])

  return {
    profiles: profiles.data,
    roots: roots.data,
    profileId,
    setProfileId,
    rootId,
    setRootId,
    profile: profiles.data?.find((p) => p.id === profileId),
    noRoot: roots.data !== undefined && roots.data.length === 0,
  }
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
