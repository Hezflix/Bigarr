import { type FormEvent, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ApiError, auth, profile, type FollowedShow, type MentionChoice, type MyNotifications, type UserNotifications } from '@/api/client'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Checkbox } from '@/components/ui/checkbox'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { hideBroken, sizedImage } from '@/features/playback/imageSize'
import { formatAge } from '@/i18n'

/** A Discord user id is a snowflake: 17 to 20 digits. A pasted mention (`<@123…>`) is accepted too, the API trims it. */
export const discordIdPattern = /^\s*(<@!?)?\d{17,20}>?\s*$/

/** The signed-in user's own page, whatever their role: how Discord messages reach them, and the shows whose new episodes they get. */
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
      <FollowedShowsCard />
    </>
  )
}

/** The shows the user watches (any episode played), each with the switch that stops its new-episode messages. */
function FollowedShowsCard() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const shows = useQuery({ queryKey: ['profile', 'shows'], queryFn: profile.shows })
  const follow = useMutation({
    mutationFn: ({ seriesId, notify }: { seriesId: number; notify: boolean }) => profile.follow(seriesId, notify),
    onMutate: ({ seriesId, notify }) =>
      queryClient.setQueryData(['profile', 'shows'], (data: typeof shows.data) =>
        data ? { ...data, shows: data.shows.map((s) => (s.seriesId === seriesId ? { ...s, notify } : s)) } : data,
      ),
    onError: (e) => {
      toast.error(e instanceof ApiError ? e.message : t('common.unexpectedError'))
      void queryClient.invalidateQueries({ queryKey: ['profile', 'shows'] })
    },
  })
  const data = shows.data

  return (
    <Card className="max-w-2xl" data-testid="profile-shows">
      <CardHeader>
        <CardTitle>{t('profile.shows.title')}</CardTitle>
        <CardDescription>{t('profile.shows.hint')}</CardDescription>
      </CardHeader>
      <CardContent className="grid gap-3">
        {shows.isPending && <p className="text-sm text-muted-foreground">{t('common.loading')}</p>}
        {shows.isError && <p className="text-sm text-destructive">{t('common.unexpectedError')}</p>}
        {data && !data.botConfigured && <p className="text-sm text-muted-foreground">{t('profile.shows.noBot')}</p>}
        {data?.botConfigured && !data.newEpisodes && <p className="text-sm text-muted-foreground">{t('profile.shows.off')}</p>}
        {data && !data.linked && <p className="text-sm text-muted-foreground">{t('profile.shows.notLinked')}</p>}
        {data?.linked && data.shows.length === 0 && <p className="text-sm text-muted-foreground">{t('profile.shows.empty')}</p>}
        {data && data.shows.length > 0 && (
          <ul className="grid divide-y rounded-lg border" data-testid="profile-shows-list">
            {data.shows.map((show) => (
              <ShowRow key={show.seriesId} show={show} onChange={(notify) => follow.mutate({ seriesId: show.seriesId, notify })} />
            ))}
          </ul>
        )}
      </CardContent>
    </Card>
  )
}

function ShowRow({ show, onChange }: { show: FollowedShow; onChange: (notify: boolean) => void }) {
  const { t } = useTranslation()
  const id = `profile-show-${show.seriesId}`
  return (
    <li className="flex items-center gap-3 px-3 py-2">
      {show.posterUrl ? (
        <img src={sizedImage(show.posterUrl, 92)} onError={hideBroken} alt="" aria-hidden="true" className="aspect-[2/3] w-8 shrink-0 rounded-sm object-cover" />
      ) : (
        <span className="aspect-[2/3] w-8 shrink-0 rounded-sm bg-muted" aria-hidden="true" />
      )}
      <label htmlFor={id} className="min-w-0 flex-1 cursor-pointer">
        <span className="block truncate text-sm font-medium">
          {show.title}
          {show.year ? <span className="font-normal text-muted-foreground"> ({show.year})</span> : null}
        </span>
        <span className="block text-xs text-muted-foreground">{t('profile.shows.lastWatched', { age: formatAge(show.lastWatchedAt) })}</span>
      </label>
      <span className="flex shrink-0 items-center gap-2 text-sm">
        <Checkbox id={id} checked={show.notify} onCheckedChange={(on) => onChange(on === true)} data-testid={`${id}-notify`} />
        <span className="hidden sm:inline" aria-hidden="true">
          {t('profile.shows.notify')}
        </span>
        <span className="sr-only">{t('profile.shows.notifyFor', { title: show.title })}</span>
      </span>
    </li>
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
