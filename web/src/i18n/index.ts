import i18n from 'i18next'
import { initReactI18next } from 'react-i18next'
import en from './en.json'

// English only for now. Adding a language = one JSON file here, plus a user setting to pick it.
void i18n.use(initReactI18next).init({
  resources: { en: { translation: en } },
  lng: 'en',
  fallbackLng: 'en',
  interpolation: { escapeValue: false },
})

export default i18n

export function formatDate(iso: string | null | undefined): string {
  if (!iso) return ''
  return new Intl.DateTimeFormat(i18n.language, { dateStyle: 'medium' }).format(new Date(iso))
}

export function formatDateTime(iso: string | null | undefined): string {
  if (!iso) return ''
  return new Intl.DateTimeFormat(i18n.language, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(iso))
}

const byteUnits = ['B', 'KB', 'MB', 'GB', 'TB']

function scaleBytes(bytes: number) {
  let value = bytes
  let unit = 0
  while (value >= 1024 && unit < byteUnits.length - 1) {
    value /= 1024
    unit++
  }
  return { value, unit: byteUnits[unit], decimals: unit >= 3 ? 2 : 0 }
}

export function formatBytes(bytes: number | null | undefined): string {
  if (bytes === null || bytes === undefined) return '—'
  const { value, unit, decimals } = scaleBytes(bytes)
  return `${new Intl.NumberFormat(i18n.language, { maximumFractionDigits: decimals }).format(value)} ${unit}`
}

/** A shorter size for tight places: whole units from 10 up, one decimal below ("39 GB", "4.1 GB", "1.2 TB"). */
export function formatBytesShort(bytes: number): string {
  const { value, unit } = scaleBytes(bytes)
  return `${new Intl.NumberFormat(i18n.language, { maximumFractionDigits: value >= 10 ? 0 : 1 }).format(value)} ${unit}`
}

/** "en" → "English" in the UI language; the code itself when the browser does not know it. */
export function formatLanguage(code: string): string {
  try {
    return new Intl.DisplayNames([i18n.language], { type: 'language' }).of(code) ?? code
  } catch {
    return code
  }
}

/** "3d", "5h", "2y": the age column of a release list. */
export function formatAge(iso: string | null | undefined, now: number = Date.now()): string {
  if (!iso) return ''
  const seconds = Math.max(0, Math.round((now - new Date(iso).getTime()) / 1000))
  const units: [Intl.RelativeTimeFormatUnit, number][] = [
    ['year', 31_536_000],
    ['month', 2_592_000],
    ['day', 86_400],
    ['hour', 3_600],
    ['minute', 60],
  ]
  const format = new Intl.RelativeTimeFormat(i18n.language, { numeric: 'always', style: 'narrow' })
  for (const [unit, size] of units) {
    if (seconds >= size) {
      return format.format(-Math.floor(seconds / size), unit)
    }
  }
  // Under a minute "0m" would read as a future date once rounded; seconds keep the "ago".
  return format.format(-Math.max(1, seconds), 'second')
}
