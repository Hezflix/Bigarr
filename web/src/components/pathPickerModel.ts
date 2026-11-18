import type { FileSystemItem } from '@/api/client'

/** A suggestion in the path picker: a folder to go into, or a file to pick. */
export type PathEntry = FileSystemItem & { kind: 'folder' | 'file' }

/** Whether a file name ends with one of the extensions (case-insensitive, with or without the leading dot). */
export function matchesExtension(name: string, extensions: readonly string[]): boolean {
  const lower = name.toLowerCase()
  return extensions.some((ext) => {
    const e = ext.toLowerCase()
    return lower.endsWith(e.startsWith('.') ? e : `.${e}`)
  })
}

/**
 * The folders, then the files with one of the extensions, whose name starts with what is being typed.
 * Without extensions the picker is a folder picker and files never show.
 */
export function pathEntries(
  listing: { directories: FileSystemItem[]; files: FileSystemItem[] } | undefined,
  leaf: string,
  fileExtensions?: readonly string[],
): PathEntry[] {
  if (!listing) return []
  const needle = leaf.toLowerCase()
  const starts = (item: FileSystemItem) => item.name.toLowerCase().startsWith(needle)
  const folders = listing.directories.filter(starts).map((d) => ({ ...d, kind: 'folder' as const }))
  const files = fileExtensions?.length
    ? listing.files.filter((f) => starts(f) && matchesExtension(f.name, fileExtensions)).map((f) => ({ ...f, kind: 'file' as const }))
    : []
  return [...folders, ...files]
}

/** Chosen folders keep a trailing separator so the next keystroke goes one level deeper; a file is the full path. */
export function acceptedPath(entry: PathEntry): string {
  if (entry.kind === 'file') return entry.path
  const separator = entry.path.includes('\\') && !entry.path.includes('/') ? '\\' : '/'
  return entry.path.endsWith(separator) ? entry.path : entry.path + separator
}
