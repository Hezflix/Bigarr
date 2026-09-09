import { useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { ApiError, rootFolders, type RootFolder } from '../../api/client'
import { formatBytes } from '../../i18n'

export function LibrarySettingsPage() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const roots = useQuery({ queryKey: ['rootFolders'], queryFn: rootFolders.list })
  const unmapped = useQuery({ queryKey: ['unmapped'], queryFn: rootFolders.unmapped })
  const [path, setPath] = useState('')

  const add = useMutation({
    mutationFn: () => rootFolders.add(path.trim()),
    onSuccess: async () => {
      setPath('')
      await queryClient.invalidateQueries({ queryKey: ['rootFolders'] })
    },
  })
  const remove = useMutation({
    mutationFn: (id: number) => rootFolders.remove(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['rootFolders'] }),
  })

  const submit = (e: FormEvent) => {
    e.preventDefault()
    add.mutate()
  }

  return (
    <>
      <div className="page-header">
        <h1>{t('settings.library')}</h1>
      </div>

      <h2>{t('settings.rootFolders')}</h2>
      <div className="table-wrap">
        <table>
          <thead>
            <tr>
              <th>{t('settings.path')}</th>
              <th>{t('settings.freeSpace')}</th>
              <th>{t('settings.versions')}</th>
              <th>{t('settings.unmapped')}</th>
              <th>{t('settings.scan')}</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {roots.data?.map((r) => (
              <RootFolderRow key={r.id} root={r} onDelete={() => remove.mutate(r.id)} />
            ))}
            {roots.data?.length === 0 && (
              <tr>
                <td colSpan={6} className="muted">
                  {t('settings.noRootFolders')}
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
      {remove.error && <p className="error">{remove.error instanceof ApiError ? remove.error.message : t('settings.removeFailed')}</p>}

      <form className="inline-form" onSubmit={submit}>
        <input value={path} onChange={(e) => setPath(e.target.value)} placeholder={t('settings.pathPlaceholder')} />
        <button type="submit" disabled={add.isPending || !path.trim()}>
          {t('settings.addRootFolder')}
        </button>
        {add.error && <span className="error">{add.error instanceof ApiError ? add.error.message : t('settings.addFailed')}</span>}
      </form>
      <p className="muted small">{t('settings.scanHint')}</p>

      <h2>{t('settings.unmappedFolders')}</h2>
      {unmapped.data?.length === 0 && <p className="muted">{t('settings.unmappedEmpty')}</p>}
      {unmapped.data && unmapped.data.length > 0 && (
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>{t('settings.folder')}</th>
                <th>{t('settings.files')}</th>
                <th>{t('settings.reason')}</th>
                <th>{t('settings.detail')}</th>
              </tr>
            </thead>
            <tbody>
              {unmapped.data.map((u) => (
                <tr key={u.id}>
                  <td title={u.path}>{u.relativePath}</td>
                  <td>{u.fileCount}</td>
                  <td>{t(`unmappedReason.${u.reason}`)}</td>
                  <td className="muted small">{u.detail}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </>
  )
}

function RootFolderRow({ root, onDelete }: { root: RootFolder; onDelete: () => void }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const status = useQuery({
    queryKey: ['scan', root.id],
    queryFn: () => rootFolders.scanStatus(root.id),
    refetchInterval: (query) => (query.state.data?.state === 'running' || query.state.data?.state === 'queued' ? 1000 : false),
  })
  const scan = useMutation({
    mutationFn: () => rootFolders.scan(root.id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['scan', root.id] }),
  })

  const s = status.data
  const busy = s?.state === 'running' || s?.state === 'queued'
  if (s?.state === 'completed' && status.dataUpdatedAt && Date.now() - status.dataUpdatedAt < 1500) {
    void queryClient.invalidateQueries({ queryKey: ['movies'] })
    void queryClient.invalidateQueries({ queryKey: ['rootFolders'] })
    void queryClient.invalidateQueries({ queryKey: ['unmapped'] })
  }

  return (
    <tr>
      <td>
        <code>{root.path}</code>
        {!root.accessible && <span className="badge status-missing">{t('settings.inaccessible')}</span>}
      </td>
      <td>
        {formatBytes(root.freeSpace)} <span className="muted">/ {formatBytes(root.totalSpace)}</span>
      </td>
      <td>{root.versionCount}</td>
      <td>{root.unmappedFolderCount}</td>
      <td>
        {busy && s && (
          <span className="muted small">
            {s.foldersProcessed}/{s.foldersTotal} {s.currentFolder ? `· ${s.currentFolder}` : ''}
          </span>
        )}
        {s?.state === 'completed' && (
          <span className="muted small">
            {t('settings.scanMovies', { count: s.moviesAdded })}, {t('settings.scanVersions', { count: s.versionsAdded })},{' '}
            {t('settings.scanUnmapped', { count: s.unmapped })}
          </span>
        )}
        {s?.state === 'failed' && <span className="error small">{s.error}</span>}
      </td>
      <td className="row-actions">
        <button type="button" onClick={() => scan.mutate()} disabled={busy || scan.isPending || !root.accessible}>
          {busy ? t('settings.scanning') : t('settings.scan')}
        </button>
        <button
          type="button"
          className="ghost danger"
          onClick={() => {
            if (window.confirm(t('settings.confirmRemoveRoot', { path: root.path }))) onDelete()
          }}
          title={t('settings.remove')}
        >
          ✕
        </button>
      </td>
    </tr>
  )
}
