type Props = { url: string | null; title: string; className?: string }

/** Poster with a typographic fallback: the fixture catalogue has no images. */
export function Poster({ url, title, className }: Props) {
  if (url) {
    return <img className={className ? `poster ${className}` : 'poster'} src={url} alt="" loading="lazy" />
  }
  const initials = title
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((w) => w[0]?.toUpperCase())
    .join('')
  return (
    <div className={className ? `poster poster-fallback ${className}` : 'poster poster-fallback'} aria-hidden="true">
      {initials}
    </div>
  )
}
