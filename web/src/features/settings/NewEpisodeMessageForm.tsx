import { type FormEvent, useState } from 'react'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ApiError, type DiscordBot, newEpisodes, type NewEpisodeMessageSettings, type NewEpisodeText } from '@/api/client'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import { useDebounced } from '@/hooks/useDebounced'
import { DiscordMessage } from './DiscordPreview'

/** The texts, in the order the message shows them: one episode, several, then the link. */
const groups: { key: 'single' | 'batch' | 'link'; fields: NewEpisodeText[] }[] = [
  { key: 'single', fields: ['content', 'title'] },
  { key: 'batch', fields: ['batchContent', 'batchTitle', 'batchLine', 'batchMore'] },
  { key: 'link', fields: ['linkName', 'linkText'] },
]

/**
 * The wording of the new-episode direct message, and where its Plex link goes. An empty text uses the English default,
 * shown greyed in the field. The preview is built by the server with the same code as the messages, as the form is
 * typed; placeholders a text does not take are flagged under it and refused when saved.
 */
export function NewEpisodeMessageForm({ bot }: { bot: DiscordBot }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [draft, setDraft] = useState<NewEpisodeMessageSettings>(bot.message)
  const [shown, setShown] = useState<'single' | 'batch'>('single')
  const dirty = JSON.stringify(draft) !== JSON.stringify(bot.message)
  const key = useDebounced(JSON.stringify(draft), 300)
  const preview = useQuery({
    queryKey: ['notifications', 'new-episodes', 'preview', key],
    queryFn: () => newEpisodes.preview(JSON.parse(key) as NewEpisodeMessageSettings),
    placeholderData: keepPreviousData,
    staleTime: 60_000,
  })
  const save = useMutation({
    mutationFn: () => newEpisodes.updateMessage(draft),
    onSuccess: (saved) => {
      queryClient.setQueryData(['notifications', 'discord-bot'], saved)
      setDraft(saved.message)
      toast.success(t('notifications.bot.message.saved'))
    },
    onError: (e) => toast.error(e instanceof ApiError ? e.message : t('common.unexpectedError')),
  })
  const submit = (e: FormEvent) => {
    e.preventDefault()
    save.mutate()
  }
  const set = (patch: Partial<NewEpisodeMessageSettings>) => setDraft((d) => ({ ...d, ...patch }))
  const problems = preview.data?.problems ?? []
  const payload = preview.data?.[shown]

  return (
    <form className="grid gap-4" onSubmit={submit} data-testid="new-episode-message">
      <div className="grid gap-1">
        <h3 className="text-sm font-medium">{t('notifications.bot.message.title')}</h3>
        <p className="max-w-3xl text-xs text-muted-foreground">{t('notifications.bot.message.hint')}</p>
      </div>

      <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_minmax(0,1fr)]">
        <div className="grid content-start gap-5">
          {groups.map((group) => (
            <fieldset key={group.key} className="grid gap-3">
              <legend className="mb-2 text-xs font-medium tracking-wide text-muted-foreground uppercase">{t(`notifications.bot.message.group.${group.key}`)}</legend>
              {group.fields.map((field) => {
                const info = bot.texts.find((text) => text.field === field)
                const wrong = problems.filter((p) => p.field === field).map((p) => p.placeholder)
                return (
                  <div key={field} className="grid gap-1.5">
                    <Label htmlFor={`ne-${field}`}>{t(`notifications.bot.message.field.${field}`)}</Label>
                    <Input
                      id={`ne-${field}`}
                      value={draft[field] ?? ''}
                      placeholder={info?.default}
                      maxLength={512}
                      aria-invalid={wrong.length > 0 || undefined}
                      onChange={(e) => set({ [field]: e.target.value })}
                      onFocus={() => group.key !== 'link' && setShown(group.key)}
                      data-testid={`ne-${field}`}
                    />
                    <p className="flex flex-wrap items-center gap-1 text-xs text-muted-foreground">
                      {info?.placeholders.map((name) => (
                        <code key={name} className="rounded bg-muted px-1 py-px font-mono text-[11px]">{`{${name}}`}</code>
                      ))}
                    </p>
                    {wrong.length > 0 && (
                      <p className="text-xs text-destructive" data-testid={`ne-${field}-problem`}>
                        {t('notifications.bot.message.unknown', { placeholders: wrong.join(', ') })}
                      </p>
                    )}
                  </div>
                )
              })}
            </fieldset>
          ))}

          <fieldset className="grid gap-3">
            <legend className="mb-2 text-xs font-medium tracking-wide text-muted-foreground uppercase">{t('notifications.bot.message.group.target')}</legend>
            <div className="grid gap-1.5">
              <Label htmlFor="ne-link-target">{t('notifications.bot.message.linkTarget')}</Label>
              <Select value={draft.linkTarget} onValueChange={(value) => set({ linkTarget: value as NewEpisodeMessageSettings['linkTarget'] })}>
                <SelectTrigger id="ne-link-target" className="w-full" data-testid="ne-link-target">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="appPlexTv">{t('notifications.bot.message.target.appPlexTv')}</SelectItem>
                  <SelectItem value="serverAddress">{t('notifications.bot.message.target.serverAddress')}</SelectItem>
                </SelectContent>
              </Select>
            </div>
            {draft.linkTarget === 'serverAddress' && (
              <div className="grid gap-1.5">
                <Label htmlFor="ne-address">{t('notifications.bot.message.address')}</Label>
                <Input
                  id="ne-address"
                  type="url"
                  inputMode="url"
                  value={draft.plexWebAddress ?? ''}
                  placeholder="https://plex.example.com"
                  aria-invalid={preview.data?.addressInvalid || undefined}
                  onChange={(e) => set({ plexWebAddress: e.target.value })}
                  data-testid="ne-address"
                />
                <p className={preview.data?.addressInvalid ? 'text-xs text-destructive' : 'text-xs text-muted-foreground'}>
                  {preview.data?.addressInvalid ? t('notifications.bot.message.addressInvalid') : t('notifications.bot.message.addressHint')}
                </p>
              </div>
            )}
          </fieldset>
        </div>

        <section className="grid content-start gap-3 lg:sticky lg:top-4 lg:self-start" data-testid="new-episode-preview">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <h4 className="text-sm font-medium">{t('notifications.bot.message.preview')}</h4>
            <div className="inline-flex rounded-md border p-0.5">
              {(['single', 'batch'] as const).map((which) => (
                <Button
                  key={which}
                  type="button"
                  size="sm"
                  variant={shown === which ? 'secondary' : 'ghost'}
                  className="h-7"
                  aria-pressed={shown === which}
                  onClick={() => setShown(which)}
                >
                  {t(`notifications.bot.message.show.${which}`)}
                </Button>
              ))}
            </div>
          </div>
          {preview.isPending ? (
            <Skeleton className="h-56 w-full" />
          ) : preview.isError ? (
            <p className="text-sm text-destructive">{t('notifications.discordPreviewFailed')}</p>
          ) : payload ? (
            <DiscordMessage payload={payload} />
          ) : null}
        </section>
      </div>

      <div className="flex flex-wrap gap-2">
        <Button type="submit" disabled={save.isPending || !dirty || problems.length > 0} data-testid="ne-save">
          {t('common.save')}
        </Button>
        <Button type="button" variant="outline" disabled={!dirty} onClick={() => setDraft(bot.message)}>
          {t('notifications.bot.message.discard')}
        </Button>
      </div>
    </form>
  )
}
