import type { FormEvent, ReactNode } from 'react'
import { cn } from 'cn'
import { useTranslation } from 'react-i18next'
import { PathPicker } from '@/components/PathPicker'
import { QualitySelect } from '@/components/QualitySelect'
import { ApiError, type ManualImportExtra, type TransferMode } from '@/api/client'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { DialogFooter } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import { TableCell } from '@/components/ui/table'
import { extraSummary } from '@/features/import/extraFiles'
import { formatBytes } from '@/i18n'

/** What the film and series manual imports share: everything about the file itself, nothing about what it is matched to. */

export type TransferChoice = 'default' | TransferMode

const modes: TransferMode[] = ['hardlinkOrCopy', 'copy', 'move']

/** The path to list, for an import that does not start from a queued download. */
export function ImportPathForm({ id, path, onPath, placeholder, loading, onLoad }: { id: string; path: string; onPath: (path: string) => void; placeholder: string; loading: boolean; onLoad: (path: string) => void }) {
  const { t } = useTranslation()
  const load = (e: FormEvent) => {
    e.preventDefault()
    // Enter keeps the focus in the path field: leaving it closes the folder suggestions over the listing.
    if (document.activeElement instanceof HTMLElement) document.activeElement.blur()
    if (path.trim()) onLoad(path.trim())
  }
  return (
    <form className="flex flex-wrap items-end gap-2" onSubmit={load}>
      <div className="grid min-w-64 flex-1 gap-2">
        <Label htmlFor={id}>{t('manualImport.path')}</Label>
        <PathPicker id={id} value={path} onChange={onPath} placeholder={placeholder} className="font-mono text-xs" testId={`${id}-input`} />
      </div>
      <Button type="submit" variant="outline" disabled={!path.trim() || loading}>
        {t('manualImport.load')}
      </Button>
    </form>
  )
}

/** The listing while it loads, when it failed, and when it found no file. */
export function ImportListingState({ fetching, error, empty }: { fetching: boolean; error: unknown; empty: boolean }) {
  const { t } = useTranslation()
  return (
    <>
      {fetching && (
        <div className="flex flex-col gap-2">
          <Skeleton className="h-8 w-full" />
          <Skeleton className="h-8 w-2/3" />
        </div>
      )}

      {!!error && (
        <Alert variant="destructive">
          <AlertDescription>{error instanceof ApiError ? error.message : t('manualImport.loadFailed')}</AlertDescription>
        </Alert>
      )}

      {empty && <p className="text-sm text-muted-foreground">{t('manualImport.empty')}</p>}
    </>
  )
}

/**
 * The file's own cell: path, size and what its name says, the side files to bring with it (all ticked at first), then
 * the outcome once imported. `children` is what a kind adds below.
 */
export function ImportFileCell({
  file,
  edition,
  selected,
  extras,
  onExtras,
  extrasLabel,
  extrasTestId,
  result,
  className,
  children,
}: {
  file: { relativePath: string; size: number; isSample: boolean; releaseGroup: string | null; languages: string[]; extras: ManualImportExtra[] }
  edition?: string | null
  selected: boolean
  /** Paths of the listed extra files the user keeps. */
  extras: string[]
  onExtras: (paths: string[]) => void
  extrasLabel: string
  extrasTestId?: string
  result: { imported: boolean; destinationPath: string | null; reason: string | null } | undefined
  className: string
  children?: ReactNode
}) {
  const { t } = useTranslation()
  return (
    <TableCell className={cn('min-w-[16rem] align-top whitespace-normal', className)}>
      <span className="block break-all text-sm">{file.relativePath}</span>
      <span className="mt-1 flex flex-wrap items-center gap-1 text-xs text-muted-foreground">
        <span>{formatBytes(file.size)}</span>
        {file.isSample && <Badge variant="outline">{t('manualImport.sample')}</Badge>}
        {edition && <Badge variant="outline">{edition}</Badge>}
        {file.languages.length > 0 && <span>{file.languages.join(', ')}</span>}
      </span>
      {file.extras.length > 0 && (
        <div className="mt-2 grid gap-1" data-testid={extrasTestId}>
          <span className="text-xs font-medium text-muted-foreground">{extrasLabel}</span>
          {file.extras.map((extra) => (
            <label key={extra.path} className="flex items-start gap-2 text-xs">
              <Checkbox
                checked={extras.includes(extra.path)}
                onCheckedChange={(v) => onExtras(v === true ? [...extras, extra.path] : extras.filter((p) => p !== extra.path))}
                disabled={!selected}
                aria-label={t('manualImport.selectExtra', { name: extra.relativePath })}
                className="mt-0.5"
              />
              <span className="grid">
                <span className="break-all">{extra.relativePath}</span>
                <span className="text-muted-foreground">{extraSummary(extra, t)}</span>
              </span>
            </label>
          ))}
        </div>
      )}
      {result && (
        <span className={cn('mt-1 block break-all text-xs', result.imported ? 'text-emerald-600 dark:text-emerald-400' : 'text-destructive')}>
          {result.imported ? t('manualImport.imported', { path: result.destinationPath }) : t('manualImport.failed', { reason: result.reason })}
        </span>
      )}
      {children}
    </TableCell>
  )
}

/** The file's quality, Unknown included: the import keeps what it detected until the user picks another. */
export function ImportQualityCell({ value, onChange }: { value: number; onChange: (qualityId: number) => void }) {
  return (
    <TableCell className="align-top">
      <QualitySelect value={value} onChange={onChange} allowUnknown size="sm" className="min-w-36" />
    </TableCell>
  )
}

/**
 * The file's release group, the one the names give until the user types another. `value` null is untouched: the import
 * then keeps what it parses itself; blank imports the file without a group.
 */
export function ImportReleaseGroupCell({ value, detected, onChange, fileName }: { value: string | null; detected: string | null; onChange: (group: string) => void; fileName: string }) {
  const { t } = useTranslation()
  return (
    <TableCell className="align-top">
      <Input
        value={value ?? detected ?? ''}
        onChange={(e) => onChange(e.target.value)}
        placeholder={t('manualImport.noReleaseGroup')}
        aria-label={t('manualImport.releaseGroupLabel', { name: fileName })}
        spellCheck={false}
        autoComplete="off"
        className="h-8 w-32 text-xs"
        data-testid="manual-import-release-group"
      />
    </TableCell>
  )
}

/** How the files travel (the library's setting unless changed here), then close and import. */
export function ImportFooter({
  id,
  mode,
  onMode,
  defaultMode,
  count,
  running,
  onRun,
  onClose,
  runTestId,
}: {
  id: string
  mode: TransferChoice
  onMode: (mode: TransferChoice) => void
  /** Undefined until the listing has loaded. */
  defaultMode: TransferMode | undefined
  count: number
  running: boolean
  onRun: () => void
  onClose: () => void
  runTestId: string
}) {
  const { t } = useTranslation()
  return (
    <DialogFooter className="items-center gap-2 sm:justify-between">
      <div className="flex items-center gap-2">
        <Label htmlFor={id} className="text-muted-foreground">
          {t('manualImport.transferMode')}
        </Label>
        <Select value={mode} onValueChange={(v) => onMode(v as TransferChoice)}>
          <SelectTrigger id={id} size="sm" className="min-w-52">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="default">{t('manualImport.useDefault', { mode: defaultMode ? t(`mediaManagement.transferModes.${defaultMode}`) : '…' })}</SelectItem>
            {modes.map((m) => (
              <SelectItem key={m} value={m}>
                {t(`mediaManagement.transferModes.${m}`)}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>
      <div className="flex gap-2">
        <Button type="button" variant="ghost" onClick={onClose}>
          {t('common.close')}
        </Button>
        <Button type="button" onClick={onRun} disabled={count === 0 || running} data-testid={runTestId}>
          {running ? t('manualImport.importing') : t('manualImport.importCount', { count })}
        </Button>
      </div>
    </DialogFooter>
  )
}
