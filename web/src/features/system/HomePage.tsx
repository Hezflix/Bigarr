import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useNavigate } from '@tanstack/react-router'
import { auth, system } from '../../api/client'

export function HomePage() {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const me = useQuery({ queryKey: ['me'], queryFn: auth.me })
  const status = useQuery({ queryKey: ['system', 'status'], queryFn: system.status })

  const logout = useMutation({
    mutationFn: auth.logout,
    onSuccess: async () => {
      queryClient.clear()
      await navigate({ to: '/login' })
    },
  })

  return (
    <main className="card">
      <header>
        <h1>Bigarr</h1>
        <button type="button" onClick={() => logout.mutate()} disabled={logout.isPending}>
          Déconnexion
        </button>
      </header>

      <p>
        Connecté en tant que <strong>{me.data?.username}</strong>.
      </p>

      <h2>État du système</h2>
      {status.isPending && <p>Chargement…</p>}
      {status.error && <p className="error">Impossible de lire l'état du système.</p>}
      {status.data && (
        <dl>
          <dt>Version</dt>
          <dd>{status.data.version}</dd>
          <dt>Environnement</dt>
          <dd>{status.data.environmentName}</dd>
          <dt>Runtime</dt>
          <dd>{status.data.runtimeVersion}</dd>
          <dt>Système</dt>
          <dd>{status.data.os}</dd>
          <dt>Démarré</dt>
          <dd>{new Date(status.data.startedAt).toLocaleString()}</dd>
          <dt>Config</dt>
          <dd>
            <code>{status.data.configDirectory}</code>
          </dd>
        </dl>
      )}

      <p className="muted">Jalon M0 : squelette. Le catalogue arrive avec M1.</p>
    </main>
  )
}
