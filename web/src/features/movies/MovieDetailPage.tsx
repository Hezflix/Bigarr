import { useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useParams } from '@tanstack/react-router'
import { useTranslation } from 'react-i18next'
import { ApiError, movies, quality, rootFolders, type Movie, type MinimumAvailability, type Version, type VersionMode } from '../../api/client'
import { Poster } from '../../components/Poster'
import { StatusBadge } from '../../components/StatusBadge'
import { formatBytes, formatDate } from '../../i18n'

const availabilities: MinimumAvailability[] = ['announced', 'inCinemas', 'released']

export function MovieDetailPage() {
  const { t } = useTranslation()
  const { movieId } = useParams({ from: '/app/movies/$movieId' })
  const id = Number(movieId)
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const movie = useQuery({ queryKey: ['movie', id], queryFn: () => movies.get(id) })

  const invalidate = () => {
    void queryClient.invalidateQueries({ queryKey: ['movie', id] })
    void queryClient.invalidateQueries({ queryKey: ['movies'] })
  }

  const refresh = useMutation({ mutationFn: () => movies.refresh(id), onSuccess: invalidate })
  const remove = useMutation({
    mutationFn: (exclude: boolean) => movies.remove(id, exclude),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['movies'] })
      await navigate({ to: '/' })
    },
  })

  if (movie.isPending) return <p className="muted">{t('common.loading')}</p>
  if (movie.error) return <p className="error">{t('movie.notFound')}</p>
  const m = movie.data

  return (
    <>
      <div className="movie-header">
        <Poster url={m.posterUrl} title={m.title} className="large" />
        <div className="movie-header-body">
          <h1>
            {m.title} {m.year && <span className="muted">({m.year})</span>}
          </h1>
          {m.originalTitle && m.originalTitle !== m.title && <div className="muted">{m.originalTitle}</div>}
          <div className="meta">
            <span className="badge">{t(`movieStatus.${m.effectiveStatus}`)}</span>
            {m.certification && <span className="badge">{m.certification}</span>}
            {m.runtime && <span>{t('common.minutes', { count: m.runtime })}</span>}
            {m.genres.length > 0 && <span>{m.genres.join(', ')}</span>}
            {m.ratings?.tmdb && <span>{t('movie.tmdbRating', { value: m.ratings.tmdb.value.toFixed(1) })}</span>}
          </div>
          <p className="overview">{m.overview}</p>
          <div className="meta muted">
            <a href={`https://www.themoviedb.org/movie/${m.tmdbId}`} target="_blank" rel="noreferrer">
              TMDB {m.tmdbId}
            </a>
            {m.imdbId && (
              <a href={`https://www.imdb.com/title/${m.imdbId}/`} target="_blank" rel="noreferrer">
                IMDb {m.imdbId}
              </a>
            )}
            {m.inCinemasDate && <span>{t('movie.inCinemas', { date: formatDate(m.inCinemasDate) })}</span>}
            {m.digitalReleaseDate && <span>{t('movie.digital', { date: formatDate(m.digitalReleaseDate) })}</span>}
            {m.physicalReleaseDate && <span>{t('movie.physical', { date: formatDate(m.physicalReleaseDate) })}</span>}
          </div>
          <div className="actions">
            <button type="button" className="ghost" onClick={() => refresh.mutate()} disabled={refresh.isPending}>
              {t('movie.refresh')}
            </button>
            <button
              type="button"
              className="danger"
              onClick={() => {
                if (window.confirm(t('movie.confirmDelete', { title: m.title }))) {
                  remove.mutate(window.confirm(t('movie.confirmExclude')))
                }
              }}
              disabled={remove.isPending}
            >
              {t('movie.delete')}
            </button>
          </div>
        </div>
      </div>

      <h2>{t('versions.title')}</h2>
      <VersionsTable movie={m} onChanged={invalidate} />

      <AddVersionForm movie={m} onAdded={invalidate} />
    </>
  )
}

function VersionsTable({ movie, onChanged }: { movie: Movie; onChanged: () => void }) {
  const { t } = useTranslation()
  const [error, setError] = useState<string | null>(null)
  const fail = (e: unknown) => setError(e instanceof ApiError ? e.message : t('common.unexpectedError'))

  const toggle = useMutation({
    mutationFn: (v: Version) => movies.updateVersion(movie.id, v.id, { monitored: !v.monitored }),
    onSuccess: onChanged,
    onError: fail,
  })
  const promote = useMutation({ mutationFn: (v: Version) => movies.promote(movie.id, v.id), onSuccess: onChanged, onError: fail })
  const remove = useMutation({
    mutationFn: (v: Version) => {
      let promoteId: number | undefined
      if (v.isDefault && movie.versions.length > 1) {
        const candidates = movie.versions.filter((o) => o.id !== v.id && o.mode === 'managed')
        if (candidates.length === 0) throw new ApiError(409, t('versions.noManagedCandidate'))
        promoteId = candidates[0].id
      }
      return movies.deleteVersion(movie.id, v.id, promoteId)
    },
    onSuccess: onChanged,
    onError: fail,
  })

  return (
    <>
      {error && (
        <p className="error" role="alert">
          {error}
        </p>
      )}
      <div className="table-wrap">
        <table>
          <thead>
            <tr>
              <th>{t('versions.version')}</th>
              <th>{t('versions.mode')}</th>
              <th>{t('versions.profile')}</th>
              <th>{t('versions.minAvailability')}</th>
              <th>{t('versions.status')}</th>
              <th>{t('versions.file')}</th>
              <th>{t('versions.folder')}</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {movie.versions.map((v) => (
              <tr key={v.id}>
                <td>
                  {v.isDefault && <span title={t('versions.default')}>★ </span>}
                  <strong>{v.name}</strong>
                  {v.constraints?.edition && <div className="muted small">{t('versions.edition', { value: v.constraints.edition })}</div>}
                  {v.constraints?.releaseGroup && <div className="muted small">{t('versions.group', { value: v.constraints.releaseGroup })}</div>}
                </td>
                <td>{v.mode === 'managed' ? t('versions.managed') : t('versions.oneShot')}</td>
                <td>{v.qualityProfile}</td>
                <td>{t(`availability.${v.minimumAvailability}`)}</td>
                <td>
                  <StatusBadge status={v.status} />
                </td>
                <td>
                  {v.file ? (
                    <>
                      <div>
                        {v.file.quality}
                        {v.file.isRepack ? ` ${t('versions.repack')}` : ''} · {formatBytes(v.file.size)}
                      </div>
                      <div className="muted small" title={v.file.path}>
                        {v.file.relativePath}
                      </div>
                    </>
                  ) : (
                    <span className="muted">{t('common.none')}</span>
                  )}
                </td>
                <td className="muted small" title={v.path}>
                  {v.folderName}
                </td>
                <td className="row-actions">
                  <button type="button" className="ghost" onClick={() => toggle.mutate(v)} title={v.monitored ? t('versions.unmonitor') : t('versions.monitor')}>
                    {v.monitored ? '👁' : '🚫'}
                  </button>
                  {!v.isDefault && v.mode === 'managed' && (
                    <button type="button" className="ghost" onClick={() => promote.mutate(v)} title={t('versions.promote')}>
                      ★
                    </button>
                  )}
                  <button
                    type="button"
                    className="ghost danger"
                    onClick={() => {
                      if (window.confirm(t('versions.confirmDelete', { name: v.name }))) remove.mutate(v)
                    }}
                    title={t('versions.delete')}
                  >
                    ✕
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </>
  )
}

function AddVersionForm({ movie, onAdded }: { movie: Movie; onAdded: () => void }) {
  const { t } = useTranslation()
  const profiles = useQuery({ queryKey: ['qualityProfiles'], queryFn: quality.profiles })
  const roots = useQuery({ queryKey: ['rootFolders'], queryFn: rootFolders.list })
  const [open, setOpen] = useState(false)
  const [name, setName] = useState('')
  const [mode, setMode] = useState<VersionMode>('managed')
  const [profileId, setProfileId] = useState<number>(movie.versions[0]?.qualityProfileId ?? 0)
  const [rootId, setRootId] = useState<number>(movie.defaultRootFolderId)
  const [availability, setAvailability] = useState<MinimumAvailability>('released')
  const [edition, setEdition] = useState('')
  const [group, setGroup] = useState('')

  const add = useMutation({
    mutationFn: () =>
      movies.addVersion(movie.id, {
        name: name.trim(),
        mode,
        qualityProfileId: profileId,
        rootFolderId: rootId,
        monitored: true,
        minimumAvailability: availability,
        constraints: edition.trim() || group.trim() ? { edition: edition.trim() || null, releaseGroup: group.trim() || null } : null,
      }),
    onSuccess: () => {
      setOpen(false)
      setName('')
      setEdition('')
      setGroup('')
      onAdded()
    },
  })

  if (!open) {
    return (
      <button type="button" className="ghost" onClick={() => setOpen(true)}>
        {t('versions.add')}
      </button>
    )
  }

  const submit = (e: FormEvent) => {
    e.preventDefault()
    add.mutate()
  }

  return (
    <form className="panel" onSubmit={submit}>
      <h3>{t('versions.new')}</h3>
      <div className="form-grid">
        <label>
          {t('versions.name')}
          <input value={name} onChange={(e) => setName(e.target.value)} placeholder={t('versions.namePlaceholder')} autoFocus />
        </label>
        <label>
          {t('versions.mode')}
          <select value={mode} onChange={(e) => setMode(e.target.value as VersionMode)}>
            <option value="managed">{t('versions.modeManaged')}</option>
            <option value="oneShot">{t('versions.modeOneShot')}</option>
          </select>
        </label>
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
          {t('addMovie.minimumAvailability')}
          <select value={availability} onChange={(e) => setAvailability(e.target.value as MinimumAvailability)}>
            {availabilities.map((a) => (
              <option key={a} value={a}>
                {t(`availability.${a}`)}
              </option>
            ))}
          </select>
        </label>
        <label>
          {t('versions.editionConstraint')}
          <input value={edition} onChange={(e) => setEdition(e.target.value)} placeholder="director'?s cut" />
        </label>
        <label>
          {t('versions.releaseGroup')}
          <input value={group} onChange={(e) => setGroup(e.target.value)} placeholder="FraMeSToR" />
        </label>
      </div>
      {add.error && <p className="error">{add.error instanceof ApiError ? add.error.message : t('common.unexpectedError')}</p>}
      <div className="actions">
        <button type="button" className="ghost" onClick={() => setOpen(false)}>
          {t('common.cancel')}
        </button>
        <button type="submit" disabled={add.isPending || !name.trim()}>
          {t('versions.create')}
        </button>
      </div>
      <p className="muted small">
        <Link to="/settings/library">{t('versions.manageRootFolders')}</Link>
      </p>
    </form>
  )
}
