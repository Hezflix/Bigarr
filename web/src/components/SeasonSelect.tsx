import { useTranslation } from 'react-i18next'
import { seasonOptions } from '@/components/seasonOptions'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'

const ALL = 'all'

/**
 * The one season picker: "Season 1", "Season 2", then "Specials", whatever order the caller holds them in. `value` null
 * shows the placeholder (an action select that resets after each pick passes null), or the `all` entry when there is
 * one: a first choice that stands for every season.
 */
export function SeasonSelect({
  seasons,
  value,
  onChange,
  all,
  placeholder,
  id,
  size,
  className,
  testId,
  disabled,
}: {
  seasons: readonly number[]
  value: number | null
  onChange: (season: number) => void
  all?: { label: string; onPick: () => void }
  placeholder?: string
  id?: string
  size?: 'sm' | 'default'
  className?: string
  testId?: string
  disabled?: boolean
}) {
  const { t } = useTranslation()
  const selected = value === null ? (all ? ALL : '') : String(value)

  return (
    <Select value={selected} onValueChange={(v) => (v === ALL ? all?.onPick() : onChange(Number(v)))} disabled={disabled}>
      <SelectTrigger id={id} size={size} className={className} data-testid={testId}>
        <SelectValue placeholder={placeholder} />
      </SelectTrigger>
      <SelectContent>
        {all && <SelectItem value={ALL}>{all.label}</SelectItem>}
        {seasonOptions(seasons).map((n) => (
          <SelectItem key={n} value={String(n)}>
            {n === 0 ? t('seriesDetail.specials') : t('seriesDetail.season', { number: n })}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  )
}
