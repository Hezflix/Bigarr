import { useState } from 'react'
import { Link, Outlet, useNavigate } from '@tanstack/react-router'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { auth, system } from '../api/client'
import { FilmIcon, FolderIcon, LogOutIcon, MenuIcon, PlusIcon } from './icons'

type NavItem = {
  to: '/' | '/add' | '/settings/library'
  label: string
  icon: React.ReactNode
  exact?: boolean
}

type NavSection = { title: string; items: NavItem[] }

/** Adding a page = one entry here. Sections keep the sidebar readable as slices land. */
function useSections(): NavSection[] {
  const { t } = useTranslation()
  return [
    {
      title: t('nav.movies'),
      items: [
        { to: '/', label: t('nav.library'), icon: <FilmIcon />, exact: true },
        { to: '/add', label: t('nav.addMovie'), icon: <PlusIcon /> },
      ],
    },
    {
      title: t('nav.settings'),
      items: [{ to: '/settings/library', label: t('nav.settingsLibrary'), icon: <FolderIcon /> }],
    },
  ]
}

export function AppShell() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const sections = useSections()
  const [open, setOpen] = useState(false)

  const me = useQuery({ queryKey: ['me'], queryFn: auth.me })
  const status = useQuery({ queryKey: ['system', 'status'], queryFn: system.status, staleTime: 5 * 60_000 })
  const logout = useMutation({
    mutationFn: auth.logout,
    onSuccess: async () => {
      queryClient.clear()
      await navigate({ to: '/login' })
    },
  })

  return (
    <div className={open ? 'layout drawer-open' : 'layout'}>
      <header className="mobile-bar">
        <button type="button" className="ghost icon-button" onClick={() => setOpen((v) => !v)} aria-label={t('nav.menu')} aria-expanded={open}>
          <MenuIcon />
        </button>
        <Link to="/" className="brand" onClick={() => setOpen(false)}>
          {t('app.name')}
        </Link>
      </header>

      <aside className="sidebar">
        <Link to="/" className="brand" onClick={() => setOpen(false)}>
          {t('app.name')}
        </Link>

        <nav>
          {sections.map((section) => (
            <div key={section.title} className="nav-section">
              <div className="nav-title">{section.title}</div>
              {section.items.map((item) => (
                <Link
                  key={item.to}
                  to={item.to}
                  className="nav-item"
                  activeProps={{ className: 'nav-item active' }}
                  activeOptions={{ exact: item.exact ?? false }}
                  onClick={() => setOpen(false)}
                >
                  {item.icon}
                  <span>{item.label}</span>
                </Link>
              ))}
            </div>
          ))}
        </nav>

        <div className="sidebar-footer">
          <div className="user">
            <strong>{me.data?.username}</strong>
            <span className="muted small">{status.data ? `v${status.data.version.split('+')[0]}` : ''}</span>
          </div>
          <button type="button" className="ghost icon-button" onClick={() => logout.mutate()} disabled={logout.isPending} title={t('nav.logout')}>
            <LogOutIcon />
          </button>
        </div>
      </aside>

      {open && <div className="backdrop" onClick={() => setOpen(false)} aria-hidden="true" />}

      <main className="content">
        <Outlet />
      </main>
    </div>
  )
}
