import { useQuery } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { ClapperboardIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { seriesFilms } from '@/api/client'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import type { FilesFilter } from './SpecialsFilmsPage'

/**
 * The library toolbars' way to the specials-as-films review, with the number of suggestions nobody decided on yet.
 * Hidden while no special was ever detected as a film. From the films it opens on the films already in the library,
 * from the series on the specials that have a file.
 */
export function SpecialsFilmsButton({ files }: { files: FilesFilter }) {
  const { t } = useTranslation()
  const summary = useQuery({ queryKey: ['seriesFilmsSummary'], queryFn: seriesFilms.summary, staleTime: 60_000 })
  const data = summary.data
  if (!data || data.suggested + data.linked + data.dismissed === 0) return null

  return (
    <Button asChild variant="outline">
      <Link to="/library/specials-films" search={{ files }} title={data.suggested > 0 ? t('specialsFilms.buttonHint', { count: data.suggested }) : undefined} data-testid="specials-films-button">
        <ClapperboardIcon />
        {t('specialsFilms.button')}
        {data.suggested > 0 && (
          <Badge variant="secondary" className="tabular-nums" data-testid="specials-films-badge">
            {data.suggested}
          </Badge>
        )}
      </Link>
    </Button>
  )
}
