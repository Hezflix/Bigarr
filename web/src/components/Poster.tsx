import { cn } from 'cn'

const sizes = {
  /** Fills its grid cell. */
  grid: 'w-full text-2xl',
  small: 'w-14 text-sm',
  large: 'w-40 text-4xl sm:w-48',
} satisfies Record<string, string>

export type PosterSize = keyof typeof sizes

type Props = { url: string | null; title: string; size?: PosterSize; className?: string }

/** Poster with a typographic fallback: the fixture catalogue has no images. */
export function Poster({ url, title, size = 'grid', className }: Props) {
  const base = cn('aspect-[2/3] shrink-0 overflow-hidden rounded-md bg-muted', sizes[size], className)

  if (url) {
    return <img className={cn(base, 'object-cover')} src={url} alt="" loading="lazy" />
  }

  const initials = title
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((w) => w[0]?.toUpperCase())
    .join('')

  return (
    <div
      className={cn(base, 'flex items-center justify-center font-semibold tracking-wide text-muted-foreground select-none')}
      aria-hidden="true"
    >
      {initials}
    </div>
  )
}
