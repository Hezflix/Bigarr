import type { ComponentProps, ReactNode } from 'react'
import { cn } from 'cn'
import { TextSearchIcon, ZapIcon, type LucideIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Button } from '@/components/ui/button'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'

/**
 * The two searches, the same everywhere (film page, series page, season and episode rows, episode details, Wanted):
 * a lightning bolt for the automatic search, which grabs on its own, and a list with a magnifier for the interactive
 * search, which lists releases to pick from. A label where there is room; in a table row the icon alone, with the
 * label in its aria-label and a tooltip saying what it does.
 */

type SearchButtonProps = {
  onClick: () => void
  /** Icon alone (table rows); the label still goes to the aria-label and the tooltip. */
  iconOnly?: boolean
  /** What is searched ("Season 2", "S01E03", a version's name), added to the icon-only button's name. */
  target?: string
  /** The visible label instead of the default one ("Automatic search" / "Interactive search"). */
  label?: string
  /** The tooltip instead of the default explanation (e.g. why the button is disabled). */
  hint?: ReactNode
  pending?: boolean
  disabled?: boolean
  size?: 'sm' | 'default'
  variant?: ComponentProps<typeof Button>['variant']
  className?: string
  'data-testid'?: string
}

function SearchButton({
  icon: Icon,
  name,
  explanation,
  onClick,
  iconOnly,
  target,
  label,
  hint,
  pending,
  disabled,
  size = 'default',
  variant = 'outline',
  className,
  'data-testid': testId,
}: SearchButtonProps & { icon: LucideIcon; name: string; explanation: string }) {
  const { t } = useTranslation()
  const accessibleName = iconOnly && target ? t('searchButtons.withTarget', { action: name, target }) : undefined
  const button = (
    <Button
      type="button"
      variant={iconOnly ? 'ghost' : variant}
      size={iconOnly ? 'icon' : size}
      onClick={onClick}
      disabled={disabled || pending}
      aria-label={iconOnly ? (accessibleName ?? name) : undefined}
      aria-busy={pending || undefined}
      className={cn(iconOnly && 'size-7', className)}
      data-testid={testId}
    >
      <Icon className={pending ? 'animate-pulse' : undefined} aria-hidden="true" />
      {!iconOnly && (pending ? t('search.searching') : (label ?? name))}
    </Button>
  )
  return (
    <Tooltip>
      {/* A disabled button gets no pointer events: the wrapper keeps its tooltip (why it is disabled) reachable. */}
      <TooltipTrigger asChild>{disabled ? <span className="inline-flex">{button}</span> : button}</TooltipTrigger>
      <TooltipContent className="max-w-72">
        {iconOnly && <p className="font-medium">{accessibleName ?? name}</p>}
        {hint ?? explanation}
      </TooltipContent>
    </Tooltip>
  )
}

/** Searches the indexers and grabs the best release the profile accepts, without asking. */
export function AutomaticSearchButton(props: SearchButtonProps) {
  const { t } = useTranslation()
  return <SearchButton icon={ZapIcon} name={t('searchButtons.automatic')} explanation={t('searchButtons.automaticHint')} {...props} />
}

/** Lists the releases with every version's verdict, to pick one. */
export function InteractiveSearchButton(props: SearchButtonProps) {
  const { t } = useTranslation()
  return <SearchButton icon={TextSearchIcon} name={t('searchButtons.interactive')} explanation={t('searchButtons.interactiveHint')} {...props} />
}

export { TextSearchIcon as InteractiveSearchIcon, ZapIcon as AutomaticSearchIcon }
