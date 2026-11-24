import { useMemo, useRef, useState, type FocusEvent, type KeyboardEvent } from 'react'
import { useQuery } from '@tanstack/react-query'
import { cn } from 'cn'
import { CornerLeftUpIcon, FileIcon, FolderIcon, FolderSearchIcon, HardDriveIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { ApiError, filesystem } from '@/api/client'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Popover, PopoverAnchor, PopoverContent } from '@/components/ui/popover'
import { Skeleton } from '@/components/ui/skeleton'
import { acceptedPath, type PathEntry, pathEntries } from './pathPickerModel'

/** Split what was typed into the folder to list and the name being written inside it. */
function splitPath(value: string): { dir: string; leaf: string } {
  const cut = Math.max(value.lastIndexOf('/'), value.lastIndexOf('\\'))
  return cut < 0 ? { dir: '', leaf: value } : { dir: value.slice(0, cut + 1), leaf: value.slice(cut + 1) }
}

type PathPickerProps = {
  value: string
  onChange: (path: string) => void
  id?: string
  placeholder?: string
  className?: string
  disabled?: boolean
  testId?: string
  /** Also offer the files with one of these extensions (e.g. `['.sqlite3', '.db']`); without it only folders show. */
  fileExtensions?: readonly string[]
}

/**
 * A path input that reads the server's filesystem: it suggests the folders under what has been typed,
 * and opens a browser for the times one would rather click than type. With `fileExtensions` it picks a file instead.
 */
export function PathPicker({ value, onChange, id, placeholder, className, disabled, testId, fileExtensions }: PathPickerProps) {
  const { t } = useTranslation()
  const inputRef = useRef<HTMLInputElement>(null)
  const [open, setOpen] = useState(false)
  const [browsing, setBrowsing] = useState(false)
  const [highlight, setHighlight] = useState(0)
  const { dir, leaf } = splitPath(value)
  const includeFiles = (fileExtensions?.length ?? 0) > 0

  // Keyed on the folder, not on every keystroke: typing inside it filters the listing already in hand.
  const listing = useQuery({
    queryKey: ['filesystem', dir, includeFiles],
    queryFn: () => filesystem.browse(dir, includeFiles),
    enabled: open && !disabled,
    staleTime: 15_000,
    retry: false,
  })

  const suggestions = useMemo(() => pathEntries(listing.data, leaf, fileExtensions), [listing.data, leaf, fileExtensions])

  const accept = (item: PathEntry) => {
    onChange(acceptedPath(item))
    setHighlight(0)
    // A folder keeps the suggestions open for the next level; a file is the end of the path.
    if (item.kind === 'file') setOpen(false)
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

  // The panel closes when focus leaves the input. It is portaled out of the input's box (a dialog that scrolls would clip
  // it otherwise), and a press on a suggestion keeps the focus in the input (see `onMouseDown` below), so the input's
  // blur is the only one to watch.
  const panelRef = useRef<HTMLDivElement>(null)
  const onBlur = (event: FocusEvent<HTMLElement>) => {
    if (!panelRef.current?.contains(event.relatedTarget)) setOpen(false)
  }

  const showPanel = open && (listing.isPending || listing.error !== null || suggestions.length > 0)

  return (
    <div className={cn('flex items-center gap-2', className)}>
      <Popover open={showPanel} modal={false}>
        <PopoverAnchor asChild>
          <div className="flex-1">
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
              onBlur={onBlur}
              onKeyDown={onKeyDown}
            />
          </div>
        </PopoverAnchor>
        <PopoverContent
          ref={panelRef}
          role="listbox"
          data-testid="path-suggestions"
          align="start"
          className="max-h-64 w-(--radix-popover-trigger-width) overflow-y-auto p-1"
          // The focus stays in the input while the suggestions show: typing goes on filtering them.
          onOpenAutoFocus={(e) => e.preventDefault()}
          onCloseAutoFocus={(e) => e.preventDefault()}
          onEscapeKeyDown={(e) => {
            // Escape closes the suggestions only, never the dialog around the picker.
            e.preventDefault()
            setOpen(false)
          }}
          onInteractOutside={(e) => {
            if (!(e.target instanceof Node && inputRef.current?.contains(e.target))) setOpen(false)
          }}
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
              tabIndex={-1}
              onMouseEnter={() => setHighlight(index)}
              // Pressing a suggestion must not take the focus from the input: the blur would close the panel before the click lands.
              onMouseDown={(e) => e.preventDefault()}
              onClick={() => accept(item)}
            >
              {item.kind === 'file' ? (
                <FileIcon className="size-3.5 shrink-0 text-primary" data-testid="path-suggestion-file" />
              ) : (
                <FolderIcon className="size-3.5 shrink-0 text-muted-foreground" />
              )}
              <span className="truncate">{item.name}</span>
            </button>
          ))}
        </PopoverContent>
      </Popover>

      <Button type="button" variant="outline" disabled={disabled} onClick={() => setBrowsing(true)} data-testid="path-browse">
        <FolderSearchIcon />
        {t('pathPicker.browse')}
      </Button>

      {browsing && (
        <BrowseDialog
          start={value}
          fileExtensions={fileExtensions}
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

/** Click your way down the server's folders; the mount points are the top of the tree. With extensions, a click on a file picks it. */
function BrowseDialog({
  start,
  fileExtensions,
  onPick,
  onClose,
}: {
  start: string
  fileExtensions?: readonly string[]
  onPick: (path: string) => void
  onClose: () => void
}) {
  const { t } = useTranslation()
  const [path, setPath] = useState(start)
  const includeFiles = (fileExtensions?.length ?? 0) > 0
  const listing = useQuery({
    queryKey: ['filesystem', path, includeFiles],
    queryFn: () => filesystem.browse(path, includeFiles),
    staleTime: 15_000,
    retry: false,
  })
  const data = listing.data
  const atMounts = data?.path === ''
  const files = includeFiles ? pathEntries(data, '', fileExtensions).filter((e) => e.kind === 'file') : []

  return (
    <Dialog open onOpenChange={(next) => !next && onClose()}>
      <DialogContent className="sm:max-w-2xl">
        <DialogHeader>
          <DialogTitle>{t(includeFiles ? 'pathPicker.browseFileTitle' : 'pathPicker.browseTitle')}</DialogTitle>
          <DialogDescription>{t(includeFiles ? 'pathPicker.browseFileHint' : 'pathPicker.browseHint')}</DialogDescription>
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
          {files.map((item) => (
            <button
              key={item.path}
              type="button"
              className="flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-left text-sm hover:bg-accent/60"
              onClick={() => onPick(item.path)}
              data-testid="browse-file"
            >
              <FileIcon className="size-4 shrink-0 text-primary" />
              <span className="truncate font-mono text-xs">{item.name}</span>
            </button>
          ))}
          {data && data.directories.length === 0 && files.length === 0 && <p className="px-2 py-1.5 text-sm text-muted-foreground">{t('pathPicker.empty')}</p>}
        </div>

        <DialogFooter>
          <Button type="button" variant="ghost" onClick={onClose}>
            {t('common.cancel')}
          </Button>
          {/* Picking a file is a click on it: a folder is not an answer there. */}
          {!includeFiles && (
            <Button type="button" disabled={!data || atMounts} onClick={() => data && onPick(data.path)} data-testid="browse-select">
              {t('pathPicker.select')}
            </Button>
          )}
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
