import { cn } from 'cn'
import { CheckIcon } from 'lucide-react'

/**
 * A checkbox drawn, not a control: the whole card or row toggles the selection, and a real checkbox (a <button>)
 * cannot sit inside the card's <button>.
 */
export function SelectionMark({ checked, className }: { checked: boolean; className?: string }) {
  return (
    <span
      aria-hidden="true"
      className={cn(
        'flex size-4 shrink-0 items-center justify-center rounded-[4px] border border-input shadow-xs dark:bg-input/30',
        checked && 'border-primary bg-primary text-primary-foreground dark:bg-primary',
        className,
      )}
    >
      {checked && <CheckIcon className="size-3.5" />}
    </span>
  )
}
