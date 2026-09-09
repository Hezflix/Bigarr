import { useEffect, useState, type FormEvent } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { Link, useNavigate } from '@tanstack/react-router'
import { Trans, useTranslation } from 'react-i18next'
import { ApiError, movies, quality, rootFolders, slugify, type MinimumAvailability, type MovieLookupResult } from '../../api/client'
import { Poster } from '../../components/Poster'

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
      <div className="page-header">
        <h1>{t('lookup.title')}</h1>
      </div>

      <input
        className="search"
        placeholder={t('lookup.placeholder')}
        value={term}
        onChange={(e) => {
          setTerm(e.target.value)
          setSelected(null)
        }}
        autoFocus
      />

      {results.isFetching && <p className="muted">{t('lookup.searching')}</p>}
      {results.error && <p className="error">{t('lookup.failed')}</p>}

      {!selected && results.data && (
        <ul className="results">
          {results.data.length === 0 && <li className="muted">{t('lookup.noResults')}</li>}
          {results.data.map((r) => (
            <li key={r.tmdbId} className="result">
              <Poster url={r.posterUrl} title={r.title} className="small" />
              <div className="result-body">
                <strong>
                  {r.title} {r.year && <span className="muted">({r.year})</span>}
                </strong>
                {r.originalTitle && r.originalTitle !== r.title && <div className="muted">{r.originalTitle}</div>}
                <p className="overview">{r.overview}</p>
                {r.excluded && <span className="badge">{t('lookup.excluded')}</span>}
              </div>
              {r.existingMovieId ? (
                <Link to="/movies/$movieId" params={{ movieId: String(r.existingMovieId) }} className="button ghost">
                  {t('lookup.alreadyInLibrary')}
                </Link>
              ) : (
                <button type="button" onClick={() => setSelected(r)}>
                  {t('lookup.choose')}
                </button>
              )}
            </li>
          ))}
        </ul>
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
  const profiles = useQuery({ queryKey: ['qualityProfiles'], queryFn: quality.profiles })
  const roots = useQuery({ queryKey: ['rootFolders'], queryFn: rootFolders.list })
  const [profileId, setProfileId] = useState<number | ''>('')
  const [rootId, setRootId] = useState<number | ''>('')
  const [versionName, setVersionName] = useState('')
  const [monitored, setMonitored] = useState(true)
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
      }),
    onSuccess: (created) => onAdded(created.id),
  })

  const submit = (e: FormEvent) => {
    e.preventDefault()
    add.mutate()
  }

  const noRoot = roots.data && roots.data.length === 0

  return (
    <form className="panel" onSubmit={submit}>
      <div className="panel-header">
        <Poster url={movie.posterUrl} title={movie.title} className="small" />
        <div>
          <h2>
            {movie.title} {movie.year && <span className="muted">({movie.year})</span>}
          </h2>
          <p className="overview">{movie.overview}</p>
        </div>
      </div>

      {noRoot && (
        <p className="error">
          <Trans i18nKey="addMovie.noRootFolder" components={{ link: <Link to="/settings/library" /> }} />
        </p>
      )}

      <div className="form-grid">
        <label>
          {t('addMovie.qualityProfile')}
          <select value={profileId} onChange={(e) => setProfileId(Number(e.target.value))}>
            {profiles.data?.map((p) => (
              <option key={p.id} value={p.id}>
                {p.name}
              </option>
            ))}
          </select>
        </label>
        <label>
          {t('addMovie.rootFolder')}
          <select value={rootId} onChange={(e) => setRootId(Number(e.target.value))}>
            {roots.data?.map((r) => (
              <option key={r.id} value={r.id}>
                {r.path}
              </option>
            ))}
          </select>
        </label>
        <label>
          {t('addMovie.versionName')}
          <input value={versionName} onChange={(e) => setVersionName(e.target.value)} placeholder={effectiveName} />
          <small className="muted">{t('addMovie.versionNameHint')}</small>
        </label>
        <label>
          {t('addMovie.minimumAvailability')}
          <select value={availability} onChange={(e) => setAvailability(e.target.value as MinimumAvailability)}>
            {availabilities.map((a) => (
              <option key={a} value={a}>
                {t(`availability.${a}`)}
              </option>
            ))}
          </select>
        </label>
        <label className="checkbox">
          <input type="checkbox" checked={monitored} onChange={(e) => setMonitored(e.target.checked)} />
          {t('addMovie.monitor')}
        </label>
      </div>

      {add.error && <p className="error">{add.error instanceof ApiError ? add.error.message : t('common.unexpectedError')}</p>}

      <div className="actions">
        <button type="button" className="ghost" onClick={onCancel}>
          {t('common.cancel')}
        </button>
        <button type="submit" disabled={add.isPending || noRoot || profileId === '' || rootId === ''}>
          {t('addMovie.submit')}
        </button>
      </div>
    </form>
  )
}
