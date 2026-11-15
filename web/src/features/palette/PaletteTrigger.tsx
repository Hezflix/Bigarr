import { cn } from 'cn'
import { SearchIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Button } from '@/components/ui/button'
import { Kbd } from './Kbd'
import { isApplePlatform } from './keys'
import { usePalette } from './PaletteProvider'

/** The palette for mouse and touch users: a search field look-alike with its shortcut. */
export function PaletteTrigger({ className }: { className?: string }) {
  const { t } = useTranslation()
  const { setOpen } = usePalette()
  return (
    <Button
      type="button"
      variant="outline"
      size="sm"
      onClick={() => setOpen(true)}
      className={cn('h-8 justify-start gap-2 bg-background px-2.5 font-normal text-muted-foreground shadow-none', className)}
      aria-label={t('palette.open')}
      aria-keyshortcuts="Meta+K Control+K"
      data-testid="palette-trigger"
    >
      <SearchIcon />
      <span className="truncate">{t('palette.trigger')}</span>
      <Kbd className="ml-auto hidden sm:inline-flex">{isApplePlatform() ? '⌘K' : 'Ctrl K'}</Kbd>
    </Button>
  )
}
