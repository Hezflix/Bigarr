/**
 * A smaller copy of an artwork URL for the given width. Only TMDB serves sizes in the path; any other source
 * (TVDB's artworks, for one, whose paths also contain `/original/`) is returned as it is.
 */
export function sizedImage(url: string, width: number): string {
  return url.startsWith('https://image.tmdb.org/t/p/original/') ? url.replace('/t/p/original/', `/t/p/w${width}/`) : url
}

/** Hides an artwork that fails to load, so the page shows its plain background instead of a broken image. */
export function hideBroken(event: { currentTarget: HTMLImageElement }) {
  event.currentTarget.style.visibility = 'hidden'
}
