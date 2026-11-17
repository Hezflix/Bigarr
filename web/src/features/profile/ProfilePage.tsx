import { type FormEvent, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ApiError, auth, profile, type MentionChoice, type MyNotifications, type UserNotifications } from '@/api/client'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Checkbox } from '@/components/ui/checkbox'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'

/** A Discord user id is a snowflake: 17 to 20 digits. A pasted mention (`<@123…>`) is accepted too, the API trims it. */
export const discordIdPattern = /^\s*(<@!?)?\d{17,20}>?\s*$/

/** The signed-in user's own page, whatever their role: for now, how Discord messages about their requests reach them. */
export function ProfilePage() {
  const { t } = useTranslation()
  const me = useQuery({ queryKey: ['me'], queryFn: auth.me, staleTime: 60_000 })
  const notifications = useQuery({ queryKey: ['profile', 'notifications'], queryFn: profile.notifications })

  return (
    <>
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="text-2xl font-semibold tracking-tight">{t('profile.title')}</h1>
        {me.data && <span className="text-sm text-muted-foreground">{me.data.username}</span>}
      </div>
      {notifications.isPending && <p className="text-sm text-muted-foreground">{t('common.loading')}</p>}
      {notifications.isError && <p className="text-sm text-destructive">{t('common.unexpectedError')}</p>}
      {notifications.data && <NotificationsCard initial={notifications.data} />}
    </>
  )
}

function NotificationsCard({ initial }: { initial: MyNotifications }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [value, setValue] = useState<UserNotifications>({ discordUserId: initial.discordUserId, mentions: initial.mentions })
  const save = useMutation({
    mutationFn: () => profile.updateNotifications(value),
    onSuccess: (saved) => {
      queryClient.setQueryData(['profile', 'notifications'], saved)
      setValue({ discordUserId: saved.discordUserId, mentions: saved.mentions })
      toast.success(t('profile.saved'))
    },
    onError: (e) => toast.error(e instanceof ApiError ? e.message : t('common.unexpectedError')),
  })
  const submit = (e: FormEvent) => {
    e.preventDefault()
    save.mutate()
  }
  const valid = !value.discordUserId?.trim() || discordIdPattern.test(value.discordUserId)

  return (
    <Card className="max-w-2xl" data-testid="profile-notifications">
      <CardHeader>
        <CardTitle>{t('profile.discordTitle')}</CardTitle>
        <CardDescription>{t('profile.discordHint')}</CardDescription>
      </CardHeader>
      <CardContent>
        <form className="grid gap-5" onSubmit={submit}>
          <DiscordMentionFields idPrefix="profile" value={value} onChange={setValue} choices={initial.choices} />
          <div>
            <Button type="submit" disabled={!valid || save.isPending} data-testid="profile-save">
              {t('common.save')}
            </Button>
          </div>
        </form>
      </CardContent>
    </Card>
  )
}

/** The Discord id and the events to be mentioned on: the user's own page and the admin's user dialog (`other`) share it. */
export function DiscordMentionFields({
  idPrefix,
  value,
  onChange,
  choices,
  other = false,
}: {
  idPrefix: string
  value: UserNotifications
  onChange: (value: UserNotifications) => void
  choices: MentionChoice[]
  other?: boolean
}) {
  const { t } = useTranslation()
  const id = value.discordUserId ?? ''
  const invalid = id.trim() !== '' && !discordIdPattern.test(id)
  const noId = id.trim() === ''
  const toggle = (type: string, on: boolean) =>
    onChange({ ...value, mentions: choices.map((c) => c.type).filter((c) => (c === type ? on : value.mentions.includes(c))) })

  return (
    <div className="grid gap-4">
      <div className="grid gap-2">
        <Label htmlFor={`${idPrefix}-discord-id`}>{t('profile.discordId')}</Label>
        <Input
          id={`${idPrefix}-discord-id`}
          inputMode="numeric"
          autoComplete="off"
          placeholder="123456789012345678"
          value={id}
          aria-invalid={invalid}
          onChange={(e) => onChange({ ...value, discordUserId: e.target.value })}
          data-testid={`${idPrefix}-discord-id`}
        />
        <p className={invalid ? 'text-xs text-destructive' : 'text-xs text-muted-foreground'}>{invalid ? t('profile.discordIdInvalid') : t('profile.discordIdHint')}</p>
      </div>
      <fieldset className="grid gap-2">
        <legend className="mb-1 text-sm font-medium">{other ? t('profile.mentionsTitleOther') : t('profile.mentionsTitle')}</legend>
        {noId && <p className="text-xs text-muted-foreground">{t('profile.mentionsNeedId')}</p>}
        {choices.map((choice) => (
          <label key={choice.type} className="flex items-start gap-2 text-sm font-normal">
            <Checkbox
              className="mt-0.5"
              checked={value.mentions.includes(choice.type)}
              onCheckedChange={(on) => toggle(choice.type, on === true)}
              data-testid={`${idPrefix}-mention-${choice.type}`}
            />
            <span>
              {t(`profile.${other ? 'mentionsOther' : 'mentions'}.${choice.type}`, { defaultValue: choice.description })}
              {choice.managersOnly && <span className="block text-xs text-muted-foreground">{t('profile.managersOnly')}</span>}
            </span>
          </label>
        ))}
      </fieldset>
    </div>
  )
}
