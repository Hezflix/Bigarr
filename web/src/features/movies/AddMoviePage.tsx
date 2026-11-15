import { useEffect, useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { Link, useNavigate, useSearch } from '@tanstack/react-router'
import { Trans, useTranslation } from 'react-i18next'
import { movies, slugify, type MinimumAvailability, type MovieLookupResult } from '@/api/client'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { FormField, LookupResultCard, LookupSearch, useAddTargets, VersionNameField } from '@/features/library/addParts'
import { AddKindSwitch } from '@/features/library/AddKindSwitch'
import { TitleForm } from '@/features/library/TitleForm'
import { useDebounced } from '@/hooks/useDebounced'

const availabilities: MinimumAvailability[] = ['announced', 'inCinemas', 'released']

export function AddMoviePage() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const { term: askedTerm } = useSearch({ strict: false })
  const [term, setTerm] = useState(askedTerm ?? '')
  const [selected, setSelected] = useState<MovieLookupResult | null>(null)
  // The palette's "Add '…'" entry can land here while the page is already open.
  useEffect(() => {
    if (askedTerm) {
      setTerm(askedTerm)
      setSelected(null)
    }
  }, [askedTerm])

  const debounced = useDebounced(term.trim(), 400)

  const results = useQuery({
    queryKey: ['lookup', debounced],
    queryFn: () => movies.lookup(debounced),
    enabled: debounced.length >= 2,
  })

  return (
    <>
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-semibold tracking-tight">{t('lookup.title')}</h1>
        <AddKindSwitch kind="movie" />
      </div>

      <LookupSearch
        testId="lookup-input"
        placeholder={t('lookup.placeholder')}
        value={term}
        onChange={(value) => {
          setTerm(value)
          setSelected(null)
        }}
        fetching={results.isFetching}
        failed={!!results.error}
      />

      {results.data && (
        <div className="flex flex-col gap-3">
          {results.data.length === 0 && <p className="text-sm text-muted-foreground">{t('lookup.noResults')}</p>}
          {results.data.map((r) => (
            <LookupResultCard
              key={r.tmdbId}
              testId="lookup-result"
              posterUrl={r.posterUrl}
              title={r.title}
              year={r.year}
              badge={r.excluded && <Badge variant="secondary">{t('lookup.excluded')}</Badge>}
              subtitle={r.originalTitle && r.originalTitle !== r.title && <span className="text-sm text-muted-foreground">{r.originalTitle}</span>}
              overview={r.overview}
            >
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
            </LookupResultCard>
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
  const { profiles, roots, profileId, setProfileId, rootId, setRootId, profile, noRoot } = useAddTargets('movies')
  const [versionName, setVersionName] = useState('')
  const [monitored, setMonitored] = useState(true)
  const [searchOnAdd, setSearchOnAdd] = useState(true)
  const [availability, setAvailability] = useState<MinimumAvailability>('released')

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

  return (
    <TitleForm
      kind="movie"
      posterUrl={movie.posterUrl}
      title={movie.title}
      year={movie.year}
      overview={movie.overview}
      targets={{
        profiles,
        roots: roots?.map((r) => ({ id: r.id, name: r.path })),
        profileId,
        onProfile: setProfileId,
        rootId,
        onRoot: setRootId,
        notice: noRoot && (
          <Alert variant="destructive">
            <AlertDescription>
              <Trans i18nKey="addMovie.noRootFolder" components={{ link: <Link to="/settings/library" className="underline underline-offset-4" /> }} />
            </AlertDescription>
          </Alert>
        ),
      }}
      onCancel={onCancel}
      onSubmit={() => add.mutate()}
      submitLabel={t('addMovie.submit')}
      submitTestId="add-form-submit"
      submitDisabled={noRoot || profileId === '' || rootId === ''}
      pending={add.isPending}
      error={add.error}
    >
      <div className="grid gap-4 sm:grid-cols-2">
        <VersionNameField id="add-version-name" value={versionName} onChange={setVersionName} placeholder={effectiveName} />

        <FormField id="add-availability" label={t('addMovie.minimumAvailability')}>
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
        </FormField>
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

    </TitleForm>
  )
}
