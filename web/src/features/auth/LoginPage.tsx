import { useState, type FormEvent } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useNavigate } from '@tanstack/react-router'
import { useTranslation } from 'react-i18next'
import { ApiError, auth } from '../../api/client'

export function LoginPage() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')

  const login = useMutation({
    mutationFn: () => auth.login(username, password),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['me'] })
      await navigate({ to: '/' })
    },
  })

  const onSubmit = (event: FormEvent) => {
    event.preventDefault()
    login.mutate()
  }

  const error =
    login.error instanceof ApiError && login.error.status === 401
      ? t('auth.invalidCredentials')
      : login.error
        ? t('auth.unexpectedError')
        : null

  return (
    <main className="card">
      <h1>{t('app.name')}</h1>
      <form onSubmit={onSubmit}>
        <label>
          {t('auth.username')}
          <input value={username} onChange={(e) => setUsername(e.target.value)} autoComplete="username" autoFocus />
        </label>
        <label>
          {t('auth.password')}
          <input type="password" value={password} onChange={(e) => setPassword(e.target.value)} autoComplete="current-password" />
        </label>
        {error && (
          <p role="alert" className="error">
            {error}
          </p>
        )}
        <button type="submit" disabled={login.isPending || !username || !password}>
          {t('auth.login')}
        </button>
      </form>
    </main>
  )
}
