/**
 * Whether two addresses are the same destination for a stored secret: same scheme, host and port. The path may differ.
 * Mirrors the server's rule: an address that cannot be read never equals another one.
 */
export function sameDestination(stored: string | null | undefined, typed: string | null | undefined): boolean {
  const from = (stored ?? '').trim()
  const to = (typed ?? '').trim()
  if (from === '' || to === '') return from === to
  try {
    const a = new URL(from)
    const b = new URL(to)
    return a.protocol === b.protocol && a.hostname.toLowerCase() === b.hostname.toLowerCase() && a.port === b.port
  } catch {
    return false
  }
}
