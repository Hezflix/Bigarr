import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react'

export const themeModes = ['light', 'dark', 'system'] as const
export type ThemeMode = (typeof themeModes)[number]

/** Shared with the inline bootstrap script in index.html, which applies the class before first paint. */
const storageKey = 'bigarr.theme'
const darkQuery = '(prefers-color-scheme: dark)'

type ThemeContextValue = {
  /** What the user picked. */
  theme: ThemeMode
  /** What is actually applied: `system` resolved against the OS preference. */
  resolvedTheme: Exclude<ThemeMode, 'system'>
  setTheme: (mode: ThemeMode) => void
}

const ThemeContext = createContext<ThemeContextValue | null>(null)

function readStoredTheme(): ThemeMode {
  try {
    const stored = localStorage.getItem(storageKey)
    if (stored && (themeModes as readonly string[]).includes(stored)) return stored as ThemeMode
  } catch {
    /* storage can be unavailable (private mode, blocked site data) */
  }
  return 'system'
}

function prefersDark(): boolean {
  return window.matchMedia(darkQuery).matches
}

export function ThemeProvider({ children }: { children: React.ReactNode }) {
  const [theme, setThemeState] = useState<ThemeMode>(readStoredTheme)
  const [systemDark, setSystemDark] = useState<boolean>(prefersDark)

  // In `system` mode the OS drives the theme, so follow it while the app is open.
  useEffect(() => {
    const media = window.matchMedia(darkQuery)
    const onChange = (event: MediaQueryListEvent) => setSystemDark(event.matches)
    media.addEventListener('change', onChange)
    return () => media.removeEventListener('change', onChange)
  }, [])

  const resolvedTheme = theme === 'system' ? (systemDark ? 'dark' : 'light') : theme

  // shadcn's dark mode is class based: `.dark` on <html>.
  useEffect(() => {
    const root = document.documentElement
    root.classList.toggle('dark', resolvedTheme === 'dark')
    root.style.colorScheme = resolvedTheme
  }, [resolvedTheme])

  const setTheme = useCallback((mode: ThemeMode) => {
    setThemeState(mode)
    try {
      localStorage.setItem(storageKey, mode)
    } catch {
      /* the choice simply does not survive a reload */
    }
  }, [])

  const value = useMemo<ThemeContextValue>(() => ({ theme, resolvedTheme, setTheme }), [theme, resolvedTheme, setTheme])

  return <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>
}

export function useTheme(): ThemeContextValue {
  const value = useContext(ThemeContext)
  if (!value) throw new Error('useTheme must be used inside a ThemeProvider.')
  return value
}
