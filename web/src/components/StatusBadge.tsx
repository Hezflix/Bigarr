import { useTranslation } from 'react-i18next'
import type { VersionStatus } from '../api/client'

export function StatusBadge({ status }: { status: VersionStatus }) {
  const { t } = useTranslation()
  return <span className={`badge status-${status}`}>{t(`status.${status}`)}</span>
}
