import type { TFunction } from 'i18next'
import type { ExtraFile } from '@/api/client'
import { formatLanguage } from '@/i18n'

/** "English · subtitle · forced": what an extra file's name says. */
export function extraSummary(extra: Pick<ExtraFile, 'kind' | 'language' | 'forced' | 'hearingImpaired'>, t: TFunction): string {
  if (extra.kind === 'other') return t('versions.extraOther')
  return [
    extra.language ? formatLanguage(extra.language) : t('versions.extraUnknownLanguage'),
    t('versions.extraSubtitle'),
    extra.forced ? t('versions.extraForced') : null,
    extra.hearingImpaired ? t('versions.extraSdh') : null,
  ]
    .filter(Boolean)
    .join(' · ')
}
