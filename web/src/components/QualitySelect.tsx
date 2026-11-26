import { useQuery } from '@tanstack/react-query'
import { quality } from '@/api/client'
import { qualityOptions } from '@/components/qualityOptions'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'

/**
 * The one quality picker: imports, overrides and file edits all list the qualities best first. `value` null shows the
 * placeholder (an action select that resets after each pick passes null). Unknown, the parser's placeholder, is listed
 * only with `allowUnknown`: an import can keep it, an override or an edit cannot choose it.
 */
export function QualitySelect({
  value,
  onChange,
  placeholder,
  allowUnknown = false,
  id,
  size,
  className,
  contentClassName,
  testId,
  disabled,
}: {
  value: number | null
  onChange: (qualityId: number) => void
  placeholder?: string
  allowUnknown?: boolean
  id?: string
  size?: 'sm' | 'default'
  className?: string
  contentClassName?: string
  testId?: string
  disabled?: boolean
}) {
  const definitions = useQuery({ queryKey: ['qualityDefinitions'], queryFn: quality.definitions, staleTime: 5 * 60_000 })
  const options = qualityOptions(definitions.data ?? [], allowUnknown)
  const selected = value === null || (!allowUnknown && value === 0) ? '' : String(value)

  return (
    <Select value={selected} onValueChange={(v) => onChange(Number(v))} disabled={disabled}>
      <SelectTrigger id={id} size={size} className={className} data-testid={testId}>
        <SelectValue placeholder={placeholder} />
      </SelectTrigger>
      <SelectContent className={contentClassName}>
        {options.map((q) => (
          <SelectItem key={q.id} value={String(q.id)}>
            {q.title || q.name}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  )
}
