import { useEffect, useState, type ReactNode } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Loader2Icon, SearchIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { ApiError, profilesFor, quality, rootFolders, rootsOfKind } from '@/api/client'
import { Poster } from '@/components/Poster'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'

/** What adding a film and adding a series share: the lookup box, a result card, and the profile, root folder and version name of the form. */

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

export function AddFormHeader({ posterUrl, title, year, overview }: { posterUrl: string | null; title: string; year: number | null; overview: string | null }) {
  return (
    <CardHeader className="grid-cols-[auto_1fr] gap-4">
      <Poster url={posterUrl} title={title} size="small" className="row-span-2" />
      <CardTitle className="flex flex-wrap items-baseline gap-2 text-lg">
        {title}
        {year && <span className="text-sm font-normal text-muted-foreground">({year})</span>}
      </CardTitle>
      {overview && <p className="line-clamp-3 text-sm text-muted-foreground">{overview}</p>}
    </CardHeader>
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

/** A select over rows with a numeric id; empty until one is chosen. */
export function IdSelectField({ id, label, value, onChange, options }: { id: string; label: string; value: number | ''; onChange: (id: number) => void; options: { id: number; label: string }[] | undefined }) {
  return (
    <div className="grid grid-cols-1 gap-2">
      <Label htmlFor={id}>{label}</Label>
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
    </div>
  )
}

/** The version's name; left empty, it takes the one shown as placeholder (derived from the profile). */
export function VersionNameField({ id, value, onChange, placeholder }: { id: string; value: string; onChange: (value: string) => void; placeholder: string }) {
  const { t } = useTranslation()
  return (
    <div className="grid grid-cols-1 gap-2">
      <Label htmlFor={id}>{t('addMovie.versionName')}</Label>
      <Input id={id} value={value} onChange={(e) => onChange(e.target.value)} placeholder={placeholder} />
      <p className="text-xs text-muted-foreground">{t('addMovie.versionNameHint')}</p>
    </div>
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
