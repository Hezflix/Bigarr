import type { TFunction } from 'i18next'
import { CopyIcon, EyeIcon, EyeOffIcon, PencilIcon } from 'lucide-react'
import { toast } from 'sonner'
import { ApiError, type ReadMediaInfoResult } from '@/api/client'
import type { PaletteAction } from './actions'

/** What a film and a series page both offer on their default version: edit it, copy its folder, (un)monitor it. */
export function versionActions(
  t: TFunction,
  version: { name: string; path: string; monitored: boolean } | undefined,
  setMonitored: (monitored: boolean) => Promise<unknown>,
  onChanged: () => void,
  onEdit: () => void,
): PaletteAction[] {
  if (!version) return []
  const name = version.name
  return [
    {
      id: 'edit',
      label: t('palette.page.edit', { version: name }),
      icon: PencilIcon,
      keywords: ['settings', 'profile', 'quality', 'rename version'],
      run: onEdit,
    },
    {
      id: 'copy-path',
      label: t('palette.page.copyPath'),
      icon: CopyIcon,
      keywords: ['folder', 'path', 'clipboard', version.path],
      run: () =>
        navigator.clipboard.writeText(version.path).then(
          () => toast.success(t('palette.page.pathCopied'), { description: version.path }),
          () => toast.error(t('palette.page.copyFailed')),
        ),
    },
    {
      id: 'toggle-monitored',
      label: t(version.monitored ? 'palette.page.unmonitor' : 'palette.page.monitor', { version: name }),
      icon: version.monitored ? EyeOffIcon : EyeIcon,
      keywords: ['monitored', 'monitoring'],
      run: () =>
        setMonitored(!version.monitored).then(
          () => {
            toast.success(t(version.monitored ? 'palette.page.unmonitored' : 'palette.page.monitored', { version: name }))
            onChanged()
          },
          (e) => toast.error(e instanceof ApiError ? e.message : t('common.unexpectedError')),
        ),
    },
  ]
}

/** Cmd+K "Read media info now" on a film or series page: one toast from start to result, then the page reloads what it shows. */
export function readMediaInfo(t: TFunction, read: () => Promise<ReadMediaInfoResult>, onDone: () => void): Promise<unknown> {
  const id = toast.loading(t('palette.page.readingMediaInfo'))
  return read().then(
    (result) => {
      if (!result.mediaInfoAvailable) toast.error(t('palette.page.mediaInfoUnavailable'), { id })
      else if (result.files === 0) toast.info(t('palette.page.noFileToRead'), { id })
      else if (result.read === result.files) toast.success(t('palette.page.mediaInfoRead', { count: result.read }), { id })
      else toast.warning(t('palette.page.mediaInfoPartlyRead', { count: result.files, read: result.read }), { id })
      onDone()
    },
    (e) => toast.error(e instanceof ApiError ? e.message : t('common.unexpectedError'), { id }),
  )
}
