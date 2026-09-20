import { useMemo, useRef, useState, type FocusEvent, type KeyboardEvent } from 'react'
import { useQuery } from '@tanstack/react-query'
import { cn } from 'cn'
import { CornerLeftUpIcon, FolderIcon, FolderSearchIcon, HardDriveIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { ApiError, filesystem, type FileSystemItem } from '@/api/client'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Skeleton } from '@/components/ui/skeleton'

/** The separator the typed path already uses; a Windows server answers with backslashes. */
function separatorOf(path: string): string {
  return path.includes('\\') && !path.includes('/') ? '\\' : '/'
}

/** Split what was typed into the folder to list and the name being written inside it. */
function splitPath(value: string): { dir: string; leaf: string } {
  const cut = Math.max(value.lastIndexOf('/'), value.lastIndexOf('\\'))
  return cut < 0 ? { dir: '', leaf: value } : { dir: value.slice(0, cut + 1), leaf: value.slice(cut + 1) }
}

/** Chosen folders keep a trailing separator so the next keystroke goes one level deeper. */
function asPrefix(path: string): string {
  const separator = separatorOf(path)
  return path.endsWith(separator) ? path : path + separator
}

type PathPickerProps = {
  value: string
  onChange: (path: string) => void
  id?: string
  placeholder?: string
  className?: string
  disabled?: boolean
  testId?: string
}

/**
 * A path input that reads the server's filesystem: it suggests the folders under what has been typed,
 * and opens a browser for the times one would rather click than type.
 */
export function PathPicker({ value, onChange, id, placeholder, className, disabled, testId }: PathPickerProps) {
  const { t } = useTranslation()
  const inputRef = useRef<HTMLInputElement>(null)
  const [open, setOpen] = useState(false)
  const [browsing, setBrowsing] = useState(false)
  const [highlight, setHighlight] = useState(0)
  const { dir, leaf } = splitPath(value)

  // Keyed on the folder, not on every keystroke: typing inside it filters the listing already in hand.
  const listing = useQuery({
    queryKey: ['filesystem', dir],
    queryFn: () => filesystem.browse(dir),
    enabled: open && !disabled,
    staleTime: 15_000,
    retry: false,
  })

  const suggestions = useMemo(() => {
    const folders = listing.data?.directories ?? []
    const needle = leaf.toLowerCase()
    return folders.filter((d) => d.name.toLowerCase().startsWith(needle))
  }, [listing.data, leaf])

  const accept = (item: FileSystemItem) => {
    onChange(asPrefix(item.path))
    setHighlight(0)
    inputRef.current?.focus()
  }

  const onKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'Escape' && open) {
      event.stopPropagation()
      setOpen(false)
      return
    }
    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      event.preventDefault()
      if (!open) {
        setOpen(true)
        return
      }
      const last = suggestions.length - 1
      setHighlight((current) => (event.key === 'ArrowDown' ? Math.min(current + 1, last) : Math.max(current - 1, 0)))
      return
    }
    const suggestion = open ? suggestions[highlight] : undefined
    if (!suggestion) return
    // Enter on the folder already typed in full belongs to the form, not to the completion.
    if (event.key === 'Enter' && suggestion.name.toLowerCase() === leaf.toLowerCase()) return
    if (event.key === 'Enter' || event.key === 'Tab') {
      event.preventDefault()
      accept(suggestion)
    }
  }

  // The suggestions live inside the wrapper, so a click on one is not a blur out of it.
  const onBlur = (event: FocusEvent<HTMLDivElement>) => {
    if (!event.currentTarget.contains(event.relatedTarget)) setOpen(false)
  }

  const showPanel = open && (listing.isPending || listing.error !== null || suggestions.length > 0)

  return (
    <div className={cn('relative flex items-center gap-2', className)} onBlur={onBlur}>
      <div className="relative flex-1">
        <Input
          id={id}
          ref={inputRef}
          value={value}
          disabled={disabled}
          placeholder={placeholder}
          autoComplete="off"
          spellCheck={false}
          role="combobox"
          aria-expanded={showPanel}
          aria-autocomplete="list"
          data-testid={testId}
          className="font-mono text-xs"
          onChange={(e) => {
            onChange(e.target.value)
            setHighlight(0)
            setOpen(true)
          }}
          onFocus={() => setOpen(true)}
          onKeyDown={onKeyDown}
        />
        {showPanel && (
          <div
            role="listbox"
            data-testid="path-suggestions"
            className="absolute top-full right-0 left-0 z-50 mt-1 max-h-64 overflow-y-auto rounded-md border bg-popover p-1 text-popover-foreground shadow-md"
          >
            {listing.isPending && <Skeleton className="h-7 w-full" />}
            {listing.error && (
              <p className="px-2 py-1.5 text-xs text-destructive">{listing.error instanceof ApiError ? listing.error.message : t('common.unexpectedError')}</p>
            )}
            {suggestions.map((item, index) => (
              <button
                key={item.path}
                type="button"
                role="option"
                aria-selected={index === highlight}
                className={cn(
                  'flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-left font-mono text-xs',
                  index === highlight ? 'bg-accent text-accent-foreground' : 'hover:bg-accent/60',
                )}
                onMouseEnter={() => setHighlight(index)}
                onClick={() => accept(item)}
              >
                <FolderIcon className="size-3.5 shrink-0 text-muted-foreground" />
                <span className="truncate">{item.name}</span>
              </button>
            ))}
          </div>
        )}
      </div>

      <Button type="button" variant="outline" disabled={disabled} onClick={() => setBrowsing(true)} data-testid="path-browse">
        <FolderSearchIcon />
        {t('pathPicker.browse')}
      </Button>

      {browsing && (
        <BrowseDialog
          start={value}
          onPick={(path) => {
            onChange(path)
            setBrowsing(false)
            setOpen(false)
          }}
          onClose={() => setBrowsing(false)}
        />
      )}
    </div>
  )
}

/** Click your way down the server's folders; the mount points are the top of the tree. */
function BrowseDialog({ start, onPick, onClose }: { start: string; onPick: (path: string) => void; onClose: () => void }) {
  const { t } = useTranslation()
  const [path, setPath] = useState(start)
  const listing = useQuery({ queryKey: ['filesystem', path], queryFn: () => filesystem.browse(path), staleTime: 15_000, retry: false })
  const data = listing.data
  const atMounts = data?.path === ''

  return (
    <Dialog open onOpenChange={(next) => !next && onClose()}>
      <DialogContent className="sm:max-w-2xl">
        <DialogHeader>
          <DialogTitle>{t('pathPicker.browseTitle')}</DialogTitle>
          <DialogDescription>{t('pathPicker.browseHint')}</DialogDescription>
        </DialogHeader>

        <p className="truncate rounded-md bg-muted px-2 py-1.5 font-mono text-xs" data-testid="browse-current">
          {atMounts ? t('pathPicker.mounts') : (data?.path ?? start)}
        </p>

        <div className="h-72 overflow-y-auto rounded-lg border p-1" data-testid="browse-listing">
          {listing.isPending && (
            <div className="flex flex-col gap-1 p-1">
              <Skeleton className="h-7 w-full" />
              <Skeleton className="h-7 w-2/3" />
            </div>
          )}
          {listing.error && (
            <p className="px-2 py-1.5 text-sm text-destructive">{listing.error instanceof ApiError ? listing.error.message : t('common.unexpectedError')}</p>
          )}
          {data && !atMounts && (
            <button
              type="button"
              className="flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-left text-sm hover:bg-accent/60"
              onClick={() => setPath(data.parent ?? '')}
              data-testid="browse-up"
            >
              <CornerLeftUpIcon className="size-4 shrink-0 text-muted-foreground" />
              {t('pathPicker.up')}
            </button>
          )}
          {data?.directories.map((item) => (
            <button
              key={item.path}
              type="button"
              className="flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-left text-sm hover:bg-accent/60"
              onClick={() => setPath(item.path)}
              data-testid="browse-folder"
            >
              {atMounts ? <HardDriveIcon className="size-4 shrink-0 text-muted-foreground" /> : <FolderIcon className="size-4 shrink-0 text-muted-foreground" />}
              <span className="truncate font-mono text-xs">{item.name}</span>
            </button>
          ))}
          {data && data.directories.length === 0 && <p className="px-2 py-1.5 text-sm text-muted-foreground">{t('pathPicker.empty')}</p>}
        </div>

        <DialogFooter>
          <Button type="button" variant="ghost" onClick={onClose}>
            {t('common.cancel')}
          </Button>
          <Button type="button" disabled={!data || atMounts} onClick={() => data && onPick(data.path)} data-testid="browse-select">
            {t('pathPicker.select')}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
