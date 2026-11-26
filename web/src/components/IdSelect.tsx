import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'

/** A row to choose. `disabled` greys it out; `note` says why in a few words, after its label. */
export type IdOption = { id: number; label: string; disabled?: boolean; note?: string }

type Shared = {
  options: readonly IdOption[] | undefined
  id?: string
  ariaLabel?: string
  className?: string
  testId?: string
  disabled?: boolean
}

const NONE = 'none'

/** The select both variants draw. `none`, when given, is a first choice that stands for no row at all. */
function Base({ value, onValue, none, placeholder, options, id, ariaLabel, className = 'w-full', testId, disabled }: Shared & { value: string; onValue: (value: string) => void; none?: string; placeholder?: string }) {
  return (
    <Select value={value} onValueChange={onValue} disabled={disabled}>
      <SelectTrigger id={id} className={className} aria-label={ariaLabel} data-testid={testId}>
        <SelectValue placeholder={placeholder} />
      </SelectTrigger>
      <SelectContent>
        {none !== undefined && <SelectItem value={NONE}>{none}</SelectItem>}
        {options?.map((option) => (
          <SelectItem key={option.id} value={String(option.id)} disabled={option.disabled}>
            {option.label}
            {option.note && <span className="text-xs text-muted-foreground">{option.note}</span>}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  )
}

/**
 * A select over rows that have a numeric id: quality profiles, root folders, versions, servers. Empty (the placeholder,
 * when there is one) until a row is chosen; the list is empty while `options` loads.
 */
export function IdSelect({ value, onChange, placeholder, ...shared }: Shared & { value: number | '' | null | undefined; onChange: (id: number) => void; placeholder?: string }) {
  return <Base {...shared} value={value === '' || value == null ? '' : String(value)} onValue={(v) => onChange(Number(v))} placeholder={placeholder} />
}

/**
 * The same select where choosing no row is an answer of its own ("Automatic", "Unknown"): `noneLabel` is listed first
 * and stands for null. Something is always chosen, so there is no placeholder.
 */
export function NullableIdSelect({ value, onChange, noneLabel, ...shared }: Shared & { value: number | null | undefined; onChange: (id: number | null) => void; noneLabel: string }) {
  return <Base {...shared} value={value == null ? NONE : String(value)} onValue={(v) => onChange(v === NONE ? null : Number(v))} none={noneLabel} />
}
