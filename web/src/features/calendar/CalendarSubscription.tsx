import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { CopyIcon, LinkIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ApiError, apiKeys, type ApiKeyCreated } from '@/api/client'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { formatAge } from '@/i18n'
import { subscriptionUrl } from './calendarModel'

async function copy(text: string): Promise<boolean> {
  try {
    await navigator.clipboard.writeText(text)
    return true
  } catch {
    return false
  }
}

/**
 * The calendar page's way to subscribe from a calendar app: one button makes a key that opens the feed and nothing
 * else, and the complete address is shown once. Only a hash of the key is stored, so the address cannot come back later;
 * it lives in this component's state and nowhere else.
 */
export function CalendarSubscription({ unmonitored }: { unmonitored: boolean }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const keys = useQuery({ queryKey: ['apiKeys'], queryFn: apiKeys.list })
  const [created, setCreated] = useState<ApiKeyCreated | null>(null)

  const create = useMutation({
    mutationFn: () => apiKeys.create(t('calendar.icalKeyName'), 'default', 'calendar'),
    onSuccess: async (key) => {
      setCreated(key)
      await queryClient.invalidateQueries({ queryKey: ['apiKeys'] })
    },
    onError: (e) => toast.error(e instanceof ApiError ? e.message : t('calendar.icalFailed')),
  })

  const existing = (keys.data ?? []).filter((k) => k.scope === 'calendar' && !k.revokedAt).sort((a, b) => b.createdAt.localeCompare(a.createdAt))
  const latest = existing[0]
  const url = created ? subscriptionUrl(window.location.origin, created.key, unmonitored) : ''

  const copyUrl = async () => {
    if (await copy(url)) toast.success(t('calendar.icalCopied'))
    else toast.error(t('calendar.icalCopyFailed'))
  }

  return (
    <div className="flex flex-col gap-2">
      <p className="text-xs text-muted-foreground">{t('calendar.icalHint')}</p>
      {latest && (
        <p className="text-xs text-muted-foreground" data-testid="calendar-link-existing">
          {t('calendar.icalExisting', {
            count: existing.length,
            created: formatAge(latest.createdAt),
            used: latest.lastUsedAt ? formatAge(latest.lastUsedAt) : t('calendar.icalNeverUsed'),
          })}{' '}
          <Link to="/settings/connect" className="underline underline-offset-2">
            {t('calendar.icalManage')}
          </Link>
        </p>
      )}
      <div>
        <Button type="button" size="sm" variant="outline" disabled={create.isPending} onClick={() => create.mutate()} data-testid="calendar-link-create">
          <LinkIcon />
          {t('calendar.icalCreate')}
        </Button>
      </div>
      {created && (
        <Alert data-testid="calendar-link-created">
          <AlertDescription className="flex flex-col gap-2">
            <span>{t('calendar.icalCreated')}</span>
            <div className="flex flex-wrap items-center gap-2">
              <code className="rounded bg-muted px-2 py-1 font-mono text-xs break-all select-all" data-testid="calendar-link-url">
                {url}
              </code>
              <Button type="button" size="sm" variant="outline" onClick={() => void copyUrl()} data-testid="calendar-link-copy">
                <CopyIcon />
                {t('calendar.icalCopy')}
              </Button>
            </div>
          </AlertDescription>
        </Alert>
      )}
      <p className="text-xs text-muted-foreground">{t('calendar.icalOldKeys')}</p>
    </div>
  )
}
