import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'

/**
 * A select over rows that have a numeric id: quality profiles, root folders, versions, servers. Empty (the placeholder,
 * when there is one) until a row is chosen; the list is empty while `options` loads.
 */
export function IdSelect({
  value,
  onChange,
  options,
  placeholder,
  id,
  ariaLabel,
  className = 'w-full',
  testId,
  disabled,
}: {
  value: number | '' | null | undefined
  onChange: (id: number) => void
  options: readonly { id: number; label: string }[] | undefined
  placeholder?: string
  id?: string
  ariaLabel?: string
  className?: string
  testId?: string
  disabled?: boolean
}) {
  return (
    <Select value={value === '' || value == null ? '' : String(value)} onValueChange={(v) => onChange(Number(v))} disabled={disabled}>
      <SelectTrigger id={id} className={className} aria-label={ariaLabel} data-testid={testId}>
        <SelectValue placeholder={placeholder} />
      </SelectTrigger>
      <SelectContent>
        {options?.map((option) => (
          <SelectItem key={option.id} value={String(option.id)}>
            {option.label}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  )
}
