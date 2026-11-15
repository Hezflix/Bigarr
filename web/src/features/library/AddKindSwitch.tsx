import { Link } from '@tanstack/react-router'
import { FilmIcon, TvIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import type { TitleKind } from './TitleForm'

/** The library's one "Add": films and series side by side, the same search and the same form, one switch between them. */
export function AddKindSwitch({ kind }: { kind: TitleKind }) {
  const { t } = useTranslation()
  const item = 'inline-flex min-h-9 items-center gap-1.5 rounded-sm px-3 text-sm text-muted-foreground transition-colors hover:text-foreground aria-[current=page]:bg-background aria-[current=page]:font-medium aria-[current=page]:text-foreground aria-[current=page]:shadow-sm'
  return (
    <nav className="inline-flex rounded-md bg-muted p-1" aria-label={t('addTitle.kind')} data-testid="add-kind-switch">
      <Link to="/add" className={item} aria-current={kind === 'movie' ? 'page' : undefined}>
        <FilmIcon className="size-4" aria-hidden="true" />
        {t('addTitle.film')}
      </Link>
      <Link to="/series/add" className={item} aria-current={kind === 'series' ? 'page' : undefined}>
        <TvIcon className="size-4" aria-hidden="true" />
        {t('addTitle.series')}
      </Link>
    </nav>
  )
}
