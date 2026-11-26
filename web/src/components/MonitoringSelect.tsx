import { useTranslation } from 'react-i18next'
import { type MonitoringOption, monitoringOptions } from '@/api/client'
import { OptionSelect } from '@/components/OptionSelect'

/**
 * Which episodes of a series version to monitor: all, future, missing… A form with a choice of its own (the add
 * dialog's "the seasons picked") lists it before the monitoring options, as `first`.
 */
export function MonitoringSelect<Extra extends string = never>({
  value,
  onChange,
  first,
  id,
  ariaLabel,
  testId,
}: {
  value: MonitoringOption | NoInfer<Extra>
  onChange: (value: MonitoringOption | NoInfer<Extra>) => void
  first?: { value: Extra; label: string }
  id?: string
  ariaLabel?: string
  testId?: string
}) {
  const { t } = useTranslation()
  const options: { value: MonitoringOption | Extra; label: string }[] = monitoringOptions.map((o) => ({ value: o, label: t(`monitoringOption.${o}`) }))
  return <OptionSelect value={value} onChange={onChange} options={first ? [first, ...options] : options} id={id} ariaLabel={ariaLabel} testId={testId} />
}
