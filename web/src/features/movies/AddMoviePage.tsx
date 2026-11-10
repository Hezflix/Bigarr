import { useEffect, useState, type FormEvent } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { Link, useNavigate } from '@tanstack/react-router'
import { Loader2Icon, SearchIcon } from 'lucide-react'
import { Trans, useTranslation } from 'react-i18next'
import { ApiError, movies, quality, rootFolders, rootsOfKind, slugify, type MinimumAvailability, type MovieLookupResult, profilesFor} from '@/api/client'
import { Poster } from '@/components/Poster'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardFooter, CardHeader, CardTitle } from '@/components/ui/card'
import { Checkbox } from '@/components/ui/checkbox'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'

const availabilities: MinimumAvailability[] = ['announced', 'inCinemas', 'released']

export function AddMoviePage() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const [term, setTerm] = useState('')
  const [debounced, setDebounced] = useState('')
  const [selected, setSelected] = useState<MovieLookupResult | null>(null)

  useEffect(() => {
    const handle = setTimeout(() => setDebounced(term.trim()), 400)
    return () => clearTimeout(handle)
  }, [term])

  const results = useQuery({
    queryKey: ['lookup', debounced],
    queryFn: () => movies.lookup(debounced),
    enabled: debounced.length >= 2,
  })

  return (
    <>
      <h1 className="text-2xl font-semibold tracking-tight">{t('lookup.title')}</h1>

      <div className="relative max-w-xl">
        <SearchIcon className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground" />
        <Input
          data-testid="lookup-input"
          className="pl-9"
          placeholder={t('lookup.placeholder')}
          value={term}
          onChange={(e) => {
            setTerm(e.target.value)
            setSelected(null)
          }}
          autoFocus
        />
        {results.isFetching && (
          <Loader2Icon className="absolute top-1/2 right-3 size-4 -translate-y-1/2 animate-spin text-muted-foreground" />
        )}
      </div>

      {results.isFetching && <p className="text-sm text-muted-foreground">{t('lookup.searching')}</p>}
      {results.error && (
        <Alert variant="destructive" className="max-w-xl">
          <AlertDescription>{t('lookup.failed')}</AlertDescription>
        </Alert>
      )}

      {!selected && results.data && (
        <div className="flex flex-col gap-3">
          {results.data.length === 0 && <p className="text-sm text-muted-foreground">{t('lookup.noResults')}</p>}
          {results.data.map((r) => (
            <Card key={r.tmdbId} data-testid="lookup-result" className="py-4">
              <CardContent className="flex items-start gap-4">
                <Poster url={r.posterUrl} title={r.title} size="small" />
                <div className="flex min-w-0 flex-1 flex-col gap-1">
                  <div className="flex flex-wrap items-baseline gap-2">
                    <span className="font-medium">{r.title}</span>
                    {r.year && <span className="text-sm text-muted-foreground">({r.year})</span>}
                    {r.excluded && <Badge variant="secondary">{t('lookup.excluded')}</Badge>}
                  </div>
                  {r.originalTitle && r.originalTitle !== r.title && (
                    <span className="text-sm text-muted-foreground">{r.originalTitle}</span>
                  )}
                  {r.overview && <p className="line-clamp-3 text-sm text-muted-foreground">{r.overview}</p>}
                </div>
                {r.existingMovieId ? (
                  <Button asChild variant="outline">
                    <Link to="/movies/$movieId" params={{ movieId: String(r.existingMovieId) }}>
                      {t('lookup.alreadyInLibrary')}
                    </Link>
                  </Button>
                ) : (
                  <Button type="button" onClick={() => setSelected(r)}>
                    {t('lookup.choose')}
                  </Button>
                )}
              </CardContent>
            </Card>
          ))}
        </div>
      )}

      {selected && (
        <AddForm
          movie={selected}
          onCancel={() => setSelected(null)}
          onAdded={(id) => navigate({ to: '/movies/$movieId', params: { movieId: String(id) } })}
        />
      )}
    </>
  )
}

function AddForm({ movie, onCancel, onAdded }: { movie: MovieLookupResult; onCancel: () => void; onAdded: (id: number) => void }) {
  const { t } = useTranslation()
  const profiles = useQuery({ queryKey: ['qualityProfiles'], queryFn: quality.profiles, select: profilesFor('movies') })
  const roots = useQuery({ queryKey: ['rootFolders'], queryFn: rootFolders.list, select: rootsOfKind('movies') })
  const [profileId, setProfileId] = useState<number | ''>('')
  const [rootId, setRootId] = useState<number | ''>('')
  const [versionName, setVersionName] = useState('')
  const [monitored, setMonitored] = useState(true)
  const [searchOnAdd, setSearchOnAdd] = useState(true)
  const [availability, setAvailability] = useState<MinimumAvailability>('released')

  useEffect(() => {
    if (profileId === '' && profiles.data?.length) setProfileId(profiles.data[0].id)
  }, [profiles.data, profileId])
  useEffect(() => {
    if (rootId === '' && roots.data?.length) setRootId(roots.data[0].id)
  }, [roots.data, rootId])

  const profile = profiles.data?.find((p) => p.id === profileId)
  const effectiveName = versionName.trim() || (profile ? slugify(profile.name) : '')

  const add = useMutation({
    mutationFn: () =>
      movies.add({
        tmdbId: movie.tmdbId,
        qualityProfileId: Number(profileId),
        rootFolderId: Number(rootId),
        versionName: versionName.trim() || null,
        monitored,
        minimumAvailability: availability,
        searchOnAdd: monitored && searchOnAdd,
      }),
    onSuccess: (created) => onAdded(created.id),
  })

  const submit = (e: FormEvent) => {
    e.preventDefault()
    add.mutate()
  }

  const noRoot = roots.data && roots.data.length === 0

  return (
    <Card className="max-w-3xl">
      <form onSubmit={submit}>
        <CardHeader className="grid-cols-[auto_1fr] gap-4">
          <Poster url={movie.posterUrl} title={movie.title} size="small" className="row-span-2" />
          <CardTitle className="flex flex-wrap items-baseline gap-2 text-lg">
            {movie.title}
            {movie.year && <span className="text-sm font-normal text-muted-foreground">({movie.year})</span>}
          </CardTitle>
          {movie.overview && <p className="line-clamp-3 text-sm text-muted-foreground">{movie.overview}</p>}
        </CardHeader>

        <CardContent className="mt-6 flex flex-col gap-4">
          {noRoot && (
            <Alert variant="destructive">
              <AlertDescription>
                <Trans
                  i18nKey="addMovie.noRootFolder"
                  components={{ link: <Link to="/settings/library" className="underline underline-offset-4" /> }}
                />
              </AlertDescription>
            </Alert>
          )}

          <div className="grid gap-4 sm:grid-cols-2">
            <div className="grid grid-cols-1 gap-2">
              <Label htmlFor="add-profile">{t('addMovie.qualityProfile')}</Label>
              <Select
                value={profileId === '' ? undefined : String(profileId)}
                onValueChange={(value) => setProfileId(Number(value))}
              >
                <SelectTrigger id="add-profile" className="w-full">
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

            <div className="grid grid-cols-1 gap-2">
              <Label htmlFor="add-root">{t('addMovie.rootFolder')}</Label>
              <Select value={rootId === '' ? undefined : String(rootId)} onValueChange={(value) => setRootId(Number(value))}>
                <SelectTrigger id="add-root" className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {roots.data?.map((r) => (
                    <SelectItem key={r.id} value={String(r.id)}>
                      {r.path}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>

            <div className="grid grid-cols-1 gap-2">
              <Label htmlFor="add-version-name">{t('addMovie.versionName')}</Label>
              <Input
                id="add-version-name"
                value={versionName}
                onChange={(e) => setVersionName(e.target.value)}
                placeholder={effectiveName}
              />
              <p className="text-xs text-muted-foreground">{t('addMovie.versionNameHint')}</p>
            </div>

            <div className="grid grid-cols-1 gap-2">
              <Label htmlFor="add-availability">{t('addMovie.minimumAvailability')}</Label>
              <Select value={availability} onValueChange={(value) => setAvailability(value as MinimumAvailability)}>
                <SelectTrigger id="add-availability" className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {availabilities.map((a) => (
                    <SelectItem key={a} value={a}>
                      {t(`availability.${a}`)}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          </div>

          <div className="flex flex-col gap-3">
            <div className="flex items-center gap-2">
              <Checkbox id="add-monitored" checked={monitored} onCheckedChange={(checked) => setMonitored(checked === true)} />
              <Label htmlFor="add-monitored" className="font-normal">
                {t('addMovie.monitor')}
              </Label>
            </div>
            <div className="flex items-start gap-2">
              <Checkbox id="add-search" checked={monitored && searchOnAdd} disabled={!monitored} onCheckedChange={(checked) => setSearchOnAdd(checked === true)} className="mt-0.5" data-testid="add-search-on-add" />
              <div className="grid gap-0.5">
                <Label htmlFor="add-search" className="font-normal">
                  {t('addMovie.searchOnAdd')}
                </Label>
                <span className="text-xs text-muted-foreground">{t('addMovie.searchOnAddHint')}</span>
              </div>
            </div>
          </div>

          {add.error && (
            <Alert variant="destructive">
              <AlertDescription>{add.error instanceof ApiError ? add.error.message : t('common.unexpectedError')}</AlertDescription>
            </Alert>
          )}
        </CardContent>

        <CardFooter className="mt-6 justify-end gap-2">
          <Button type="button" variant="ghost" onClick={onCancel}>
            {t('common.cancel')}
          </Button>
          <Button type="submit" data-testid="add-form-submit" disabled={add.isPending || noRoot || profileId === '' || rootId === ''}>
            {t('addMovie.submit')}
          </Button>
        </CardFooter>
      </form>
    </Card>
  )
}
