import { useTranslation } from 'react-i18next'
import { type MinimumAvailability, minimumAvailabilities } from '@/api/client'
import { OptionSelect } from '@/components/OptionSelect'

/** From when a film version counts as out, and so as missing: announced, in cinemas or released. */
export function AvailabilitySelect({ value, onChange, id }: { value: MinimumAvailability; onChange: (value: MinimumAvailability) => void; id?: string }) {
  const { t } = useTranslation()
  return <OptionSelect value={value} onChange={onChange} options={minimumAvailabilities.map((a) => ({ value: a, label: t(`availability.${a}`) }))} id={id} />
}
