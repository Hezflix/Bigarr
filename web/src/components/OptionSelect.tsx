import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'

/** A select over a fixed list of keys (an enum of the API), each shown under its label. One is always chosen. */
export function OptionSelect<T extends string>({
  value,
  onChange,
  options,
  id,
  ariaLabel,
  className = 'w-full',
  testId,
}: {
  value: T
  onChange: (value: T) => void
  options: readonly { value: T; label: string }[]
  id?: string
  ariaLabel?: string
  className?: string
  testId?: string
}) {
  return (
    <Select value={value} onValueChange={(v) => onChange(v as T)}>
      <SelectTrigger id={id} className={className} aria-label={ariaLabel} data-testid={testId}>
        <SelectValue />
      </SelectTrigger>
      <SelectContent>
        {options.map((option) => (
          <SelectItem key={option.value} value={option.value}>
            {option.label}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  )
}
