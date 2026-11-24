import { type FormEvent, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { cn } from 'cn'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ApiError, newEpisodes, type NewEpisodeAnnouncement, type WebhookDeliveryState } from '@/api/client'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { formatAge } from '@/i18n'

const messageStyles: Record<WebhookDeliveryState, string> = {
  delivered: 'border-emerald-600/30 bg-emerald-500/15 text-emerald-700 dark:border-emerald-400/30 dark:text-emerald-300',
  pending: 'border-amber-600/30 bg-amber-500/15 text-amber-700 dark:border-amber-400/30 dark:text-amber-300',
  givenUp: 'border-red-600/30 bg-red-500/15 text-red-700 dark:border-red-400/30 dark:text-red-300',
}

/** The Discord bot that tells users about new episodes: its token (a secret, shown masked) and the latest announcements. */
export function DiscordBotCard() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const bot = useQuery({ queryKey: ['notifications', 'discord-bot'], queryFn: newEpisodes.bot })
  const announcements = useQuery({ queryKey: ['notifications', 'new-episodes'], queryFn: () => newEpisodes.announcements(), refetchInterval: 30_000 })
  const [token, setToken] = useState<string | null>(null)
  const shown = token ?? bot.data?.token ?? ''
  const save = useMutation({
    mutationFn: () => newEpisodes.updateBot(shown),
    onSuccess: (saved) => {
      queryClient.setQueryData(['notifications', 'discord-bot'], saved)
      setToken(null)
      toast.success(saved.configured ? t('notifications.bot.saved') : t('notifications.bot.removed'))
    },
    onError: (e) => toast.error(e instanceof ApiError ? e.message : t('common.unexpectedError')),
  })
  const submit = (e: FormEvent) => {
    e.preventDefault()
    save.mutate()
  }

  return (
    <Card data-testid="discord-bot">
      <CardHeader>
        <CardTitle>{t('notifications.bot.title')}</CardTitle>
        <CardDescription className="max-w-3xl">{t('notifications.bot.hint')}</CardDescription>
      </CardHeader>
      <CardContent className="grid gap-5">
        <form className="grid max-w-xl gap-2" onSubmit={submit}>
          <Label htmlFor="discord-bot-token">{t('notifications.bot.token')}</Label>
          <div className="flex flex-wrap gap-2">
            <Input
              id="discord-bot-token"
              type="password"
              autoComplete="off"
              className="min-w-0 flex-1 basis-60"
              value={shown}
              placeholder={t('notifications.bot.tokenPlaceholder')}
              onChange={(e) => setToken(e.target.value)}
              data-testid="discord-bot-token"
            />
            <Button type="submit" disabled={save.isPending || token === null} data-testid="discord-bot-save">
              {t('common.save')}
            </Button>
          </div>
          <p className="text-xs text-muted-foreground">{bot.data?.configured ? t('notifications.bot.tokenSet') : t('notifications.bot.tokenHint')}</p>
        </form>

        <div className="grid gap-2">
          <h3 className="text-sm font-medium">{t('notifications.bot.recent')}</h3>
          {announcements.data?.length === 0 && <p className="text-sm text-muted-foreground">{t('notifications.bot.none')}</p>}
          {announcements.data && announcements.data.length > 0 && (
            <div className="overflow-x-auto rounded-lg border">
              <Table data-testid="new-episode-announcements">
                <TableHeader>
                  <TableRow>
                    <TableHead>{t('notifications.bot.show')}</TableHead>
                    <TableHead>{t('notifications.bot.plex')}</TableHead>
                    <TableHead>{t('notifications.bot.messages')}</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {announcements.data.map((a) => (
                    <AnnouncementRow key={a.id} announcement={a} />
                  ))}
                </TableBody>
              </Table>
            </div>
          )}
        </div>
      </CardContent>
    </Card>
  )
}

function AnnouncementRow({ announcement: a }: { announcement: NewEpisodeAnnouncement }) {
  const { t } = useTranslation()
  return (
    <TableRow>
      <TableCell className="align-top">
        <span className="block font-medium">{a.seriesTitle ?? t('notifications.bot.deletedShow')}</span>
        <span className="block text-xs text-muted-foreground">
          {a.episodes.join(', ')} · {formatAge(a.createdAt)}
        </span>
      </TableCell>
      <TableCell className="align-top text-sm">{a.readyAt ? t(`notifications.bot.wait.${a.plexWait ?? 'notChecked'}`) : t('notifications.bot.waiting')}</TableCell>
      <TableCell className="align-top">
        <span className="flex max-w-[28rem] flex-wrap gap-1">
          {a.messages.map((m) => (
            <Badge key={m.id} variant="outline" className={cn(messageStyles[m.state])} title={m.lastError ?? undefined}>
              {m.username ?? `#${m.userId}`}
            </Badge>
          ))}
        </span>
        {a.messages
          .filter((m) => m.state === 'givenUp' && m.lastError)
          .map((m) => (
            <span key={m.id} className="mt-1 block text-xs text-red-700 dark:text-red-300">
              {m.username ?? `#${m.userId}`}: {m.lastError}
            </span>
          ))}
      </TableCell>
    </TableRow>
  )
}
