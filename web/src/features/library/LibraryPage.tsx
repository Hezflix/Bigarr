import { Link } from '@tanstack/react-router'
import { useQuery } from '@tanstack/react-query'
import { cn } from 'cn'
import { PlusIcon, StarIcon } from 'lucide-react'
import { Trans, useTranslation } from 'react-i18next'
import { movies } from '@/api/client'
import { Poster } from '@/components/Poster'
import { statusStyles } from '@/components/StatusBadge'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'

const gridClasses = 'grid grid-cols-2 gap-4 sm:grid-cols-3 md:grid-cols-4 xl:grid-cols-5 2xl:grid-cols-6'

export function LibraryPage() {
  const { t } = useTranslation()
  const library = useQuery({ queryKey: ['movies'], queryFn: movies.list })

  if (library.isPending) {
    return (
      <>
        <PageHeader count={null} />
        <div className={gridClasses}>
          {Array.from({ length: 10 }, (_, i) => (
            <div key={i} className="flex flex-col gap-2">
              <Skeleton className="aspect-[2/3] w-full rounded-xl" />
              <Skeleton className="h-4 w-3/4" />
              <Skeleton className="h-3 w-1/3" />
            </div>
          ))}
        </div>
      </>
    )
  }

  if (library.error) {
    return (
      <Alert variant="destructive">
        <AlertDescription>{t('library.loadError')}</AlertDescription>
      </Alert>
    )
  }

  const items = library.data

  return (
    <>
      <PageHeader count={items.length} />

      {items.length === 0 && (
        <Card className="items-center py-12 text-center">
          <CardContent className="flex flex-col gap-2">
            <p className="font-medium">{t('library.empty')}</p>
            <p className="text-sm text-muted-foreground">
              <Trans
                i18nKey="library.emptyHint"
                components={{
                  add: <Link to="/add" className="text-foreground underline underline-offset-4" />,
                  scan: <Link to="/settings/library" className="text-foreground underline underline-offset-4" />,
                }}
              />
            </p>
          </CardContent>
        </Card>
      )}

      <div className={gridClasses} data-testid="library-grid">
        {items.map((movie) => (
          <Link
            key={movie.id}
            to="/movies/$movieId"
            params={{ movieId: String(movie.id) }}
            data-testid="movie-card"
            className="group rounded-xl focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
          >
            <Card className="h-full gap-0 overflow-hidden p-0 transition-colors group-hover:border-ring/40 group-hover:bg-accent/40">
              <Poster url={movie.posterUrl} title={movie.title} size="grid" className="rounded-none" />
              <CardContent className="flex flex-col gap-1 px-3 py-2.5">
                <span className="line-clamp-2 text-sm leading-snug font-medium">{movie.title}</span>
                <span className="text-xs text-muted-foreground">{movie.year ?? t('common.none')}</span>
                {movie.versions.length > 0 && (
                  <div className="mt-1 flex flex-wrap gap-1">
                    {movie.versions.map((v) => (
                      <Badge
                        key={v.id}
                        variant="outline"
                        className={cn('gap-0.5', statusStyles[v.status])}
                        title={`${v.name} · ${v.quality ?? t('library.noFile')}`}
                      >
                        {v.isDefault && <StarIcon className="size-2.5 fill-current" aria-hidden="true" />}
                        {v.name}
                      </Badge>
                    ))}
                  </div>
                )}
              </CardContent>
            </Card>
          </Link>
        ))}
      </div>
    </>
  )
}

function PageHeader({ count }: { count: number | null }) {
  const { t } = useTranslation()
  return (
    <div className="flex flex-wrap items-center gap-x-3 gap-y-2">
      <h1 className="text-2xl font-semibold tracking-tight">{t('library.title')}</h1>
      {count !== null && <span className="text-sm text-muted-foreground">{t('library.count', { count })}</span>}
      <Button asChild className="ml-auto">
        <Link to="/add">
          <PlusIcon />
          {t('library.addMovie')}
        </Link>
      </Button>
    </div>
  )
}
