import { useTranslation } from 'react-i18next'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'

/**
 * The version a file is imported to, among the versions of the film or the series picked for it; the default one is
 * marked. Disabled until a title is picked (`versions` undefined). `value` null shows the placeholder.
 */
export function VersionSelect({
  versions,
  value,
  onChange,
  placeholder,
  testId,
}: {
  versions: readonly { id: number; name: string; isDefault: boolean }[] | undefined
  value: number | null
  onChange: (versionId: number) => void
  placeholder: string
  testId?: string
}) {
  const { t } = useTranslation()
  return (
    <Select value={value === null ? '' : String(value)} onValueChange={(v) => onChange(Number(v))} disabled={!versions}>
      <SelectTrigger size="sm" className="min-w-32" data-testid={testId}>
        <SelectValue placeholder={placeholder} />
      </SelectTrigger>
      <SelectContent>
        {(versions ?? []).map((v) => (
          <SelectItem key={v.id} value={String(v.id)}>
            {v.name}
            {v.isDefault ? ` · ${t('versions.default').toLowerCase()}` : ''}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  )
}
