import { Link } from '@tanstack/react-router'
import { useQuery } from '@tanstack/react-query'
import { Trans, useTranslation } from 'react-i18next'
import { movies } from '../../api/client'
import { Poster } from '../../components/Poster'

export function LibraryPage() {
  const { t } = useTranslation()
  const library = useQuery({ queryKey: ['movies'], queryFn: movies.list })

  if (library.isPending) return <p className="muted">{t('common.loading')}</p>
  if (library.error) return <p className="error">{t('library.loadError')}</p>

  const items = library.data
  return (
    <>
      <div className="page-header">
        <h1>{t('library.title')}</h1>
        <span className="muted">{t('library.count', { count: items.length })}</span>
        <Link to="/add" className="button">
          {t('library.addMovie')}
        </Link>
      </div>

      {items.length === 0 && (
        <div className="empty">
          <p>{t('library.empty')}</p>
          <p className="muted">
            <Trans i18nKey="library.emptyHint" components={{ add: <Link to="/add" />, scan: <Link to="/settings/library" /> }} />
          </p>
        </div>
      )}

      <div className="grid">
        {items.map((movie) => (
          <Link key={movie.id} to="/movies/$movieId" params={{ movieId: String(movie.id) }} className="card movie-card">
            <Poster url={movie.posterUrl} title={movie.title} />
            <div className="movie-card-body">
              <strong>{movie.title}</strong>
              <span className="muted">{movie.year ?? t('common.none')}</span>
              <div className="badges">
                {movie.versions.map((v) => (
                  <span
                    key={v.id}
                    className={`badge version-badge status-${v.status}`}
                    title={`${v.name} · ${v.quality ?? t('library.noFile')}`}
                  >
                    {v.isDefault ? '★ ' : ''}
                    {v.name}
                  </span>
                ))}
              </div>
            </div>
          </Link>
        ))}
      </div>
    </>
  )
}
