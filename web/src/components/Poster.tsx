import { cn } from 'cn'

const sizes = {
  /** Fills its grid cell. */
  grid: 'w-full text-2xl',
  small: 'w-14 text-sm',
  /** A row thumbnail in the library table and matrix. */
  thumb: 'w-7 text-[9px]',
  large: 'w-40 text-4xl sm:w-48',
} satisfies Record<string, string>

export type PosterSize = keyof typeof sizes

/**
 * The width each size is laid out at (the classes above), for the browser to pick a file from `srcset`. A grid cell's
 * width follows the window and the view options: `auto` reads the laid-out width (lazy images only), and browsers
 * without it fall back to the guess after the comma.
 */
const layoutWidths: Record<PosterSize, string> = {
  grid: 'auto, (min-width: 640px) 240px, 50vw',
  small: '56px',
  thumb: '28px',
  large: '(min-width: 640px) 192px, 160px',
}

/** TMDB's poster widths. Its URLs carry the size as a path segment, which the catalogue stores as `/original/`. */
const tmdbWidths = [92, 154, 185, 342, 500, 780]

/** Every TMDB width of the poster: a 28 px thumbnail has no use for the 2000×3000 original. */
function tmdbSrcSet(url: string) {
  if (!url.includes('/original/')) return undefined
  return tmdbWidths.map((width) => `${url.replace('/original/', `/w${width}/`)} ${width}w`).join(', ')
}

type Props = { url: string | null; title: string; size?: PosterSize; className?: string }

/** Poster with a typographic fallback: the fixture catalogue has no images. */
export function Poster({ url, title, size = 'grid', className }: Props) {
  const base = cn('aspect-[2/3] shrink-0 overflow-hidden rounded-md bg-muted', sizes[size], className)

  if (url) {
    return <img className={cn(base, 'object-cover')} src={url} srcSet={tmdbSrcSet(url)} sizes={layoutWidths[size]} alt="" loading="lazy" />
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
