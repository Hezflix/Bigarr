import { useMemo, useState, type ReactNode } from 'react'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { notifications, type DiscordEmbed, type DiscordPayload, type DiscordSettings, type NotificationEventType } from '@/api/client'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import { useDebounced } from '@/hooks/useDebounced'

/** Which of Radarr's field lists a message follows; null for a message with no optional field. */
export type DiscordFieldKind = 'grab' | 'import' | 'manualInteraction'

const fieldKinds: Record<string, DiscordFieldKind> = {
  grabbed: 'grab',
  downloadFailed: 'grab',
  episodeGrabbed: 'grab',
  episodeDownloadFailed: 'grab',
  imported: 'import',
  upgraded: 'import',
  episodeImported: 'import',
  importFailed: 'manualInteraction',
  episodeImportFailed: 'manualInteraction',
}

/** Added and deleted titles show the poster and fanart when the import fields have them, as in Radarr and Sonarr. */
const imagesFollowImport = new Set(['movieAdded', 'movieDeleted', 'seriesAdded', 'seriesDeleted'])

/**
 * What the Discord channel will show: pick an event, see its message as Discord lays it out, with the form's settings,
 * and tune the fields of that kind of message right under it. The server builds the message with the delivery's own
 * code, around the film and the episode last put on disk; nothing is posted.
 */
export function DiscordPreviewPanel({
  settings,
  events,
  types,
  fields,
}: {
  settings: DiscordSettings
  /** The endpoint's events; empty means every event. */
  events: string[]
  types: NotificationEventType[] | undefined
  fields: (kind: DiscordFieldKind) => ReactNode
}) {
  const { t } = useTranslation()
  // A string, so a re-render with equal settings does not restart the pause.
  const key = useDebounced(JSON.stringify(settings), 300)
  const previews = useQuery({
    queryKey: ['notifications', 'discord-preview', key],
    queryFn: () => notifications.discordPreview(JSON.parse(key) as DiscordSettings),
    placeholderData: keepPreviousData,
    staleTime: 60_000,
  })
  const offered = useMemo(() => (previews.data ?? []).filter((p) => events.length === 0 || events.includes(p.eventType)), [previews.data, events])
  const [chosen, setChosen] = useState('grabbed')
  const current = offered.find((p) => p.eventType === chosen) ?? offered[0]
  const kind = current ? fieldKinds[current.eventType] : undefined
  const describe = (type: string) => types?.find((candidate) => candidate.type === type)?.description

  return (
    <section className="grid gap-3 sm:col-span-2" data-testid="notification-discord-preview">
      <div className="grid gap-1">
        <h3 className="text-sm font-medium">{t('notifications.discordPreview')}</h3>
        <p className="text-xs text-muted-foreground">{t('notifications.discordPreviewHint')}</p>
      </div>
      <div className="grid gap-2">
        <Label htmlFor="nt-discord-preview-event">{t('notifications.discordPreviewEvent')}</Label>
        <Select value={current?.eventType ?? ''} onValueChange={setChosen} disabled={offered.length === 0}>
          <SelectTrigger id="nt-discord-preview-event" className="w-full" data-testid="notification-discord-preview-event">
            <SelectValue placeholder={t('notifications.discordPreviewNoEvent')} />
          </SelectTrigger>
          <SelectContent>
            {offered.map((p) => (
              <SelectItem key={p.eventType} value={p.eventType}>
                <span className="font-medium">{p.eventType}</span>
                {describe(p.eventType) && <span className="text-muted-foreground"> · {describe(p.eventType)}</span>}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>

      {previews.isPending ? (
        <Skeleton className="h-48 w-full" />
      ) : previews.isError ? (
        <p className="text-sm text-destructive">{t('notifications.discordPreviewFailed')}</p>
      ) : current ? (
        <DiscordMessage payload={current.payload} />
      ) : (
        <p className="text-sm text-muted-foreground">{t('notifications.discordPreviewNoEvent')}</p>
      )}

      {current && (
        <div className="grid gap-1" data-testid="notification-discord-fields">
          {kind ? (
            <>
              <p className="text-xs text-muted-foreground">{t(`notifications.discordFieldsOf.${kind}`)}</p>
              {fields(kind)}
            </>
          ) : (
            <p className="text-xs text-muted-foreground">
              {imagesFollowImport.has(current.eventType) ? t('notifications.discordImagesFollowImport') : t('notifications.discordNoFields')}
            </p>
          )}
        </div>
      )}
    </section>
  )
}

const hex = (color: number | null | undefined) => (color == null ? '#4e5058' : `#${color.toString(16).padStart(6, '0')}`)

/** Discord's own dark look, whatever the app's theme: this is a picture of the channel. */
export function DiscordMessage({ payload }: { payload: DiscordPayload }) {
  const { t } = useTranslation()
  const name = payload.username ?? 'Bigarr'
  return (
    <div className="overflow-hidden rounded-md bg-[#313338] px-4 py-3 font-sans text-[15px] leading-[1.375] text-[#dbdee1]" data-testid="discord-message">
      <div className="flex gap-4">
        {payload.avatar_url ? (
          <img src={payload.avatar_url} alt="" className="size-10 shrink-0 rounded-full object-cover" />
        ) : (
          <div className="flex size-10 shrink-0 items-center justify-center rounded-full bg-[#5865f2] text-lg font-semibold text-white" aria-hidden>
            {name.slice(0, 1).toUpperCase()}
          </div>
        )}
        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap items-baseline gap-x-1.5">
            <span className="font-medium text-[#f2f3f5]">{name}</span>
            <span className="rounded-[3px] bg-[#5865f2] px-1 text-[10px] font-semibold leading-[15px] text-white">{t('notifications.discordApp')}</span>
            <span className="text-xs text-[#949ba4]">{t('notifications.discordToday')}</span>
          </div>
          {payload.content && (
            <div className="break-words whitespace-pre-wrap">
              <Markdown text={payload.content} />
            </div>
          )}
          {payload.embeds?.map((embed) => (
            <Embed key={`${embed.title}|${embed.description}|${embed.timestamp}`} embed={embed} />
          ))}
        </div>
      </div>
    </div>
  )
}

function Embed({ embed }: { embed: DiscordEmbed }) {
  const fields = embed.fields ?? []
  return (
    <div className="mt-1 grid max-w-[520px] overflow-hidden rounded-[4px] border-l-4 bg-[#2b2d31]" style={{ borderLeftColor: hex(embed.color) }} data-testid="discord-embed">
      <div className="flex gap-4 py-2 pr-4 pl-3">
        <div className="grid min-w-0 flex-1 content-start gap-2">
          {embed.author && (
            <div className="flex items-center gap-2 text-sm font-semibold text-[#f2f3f5]">
              {embed.author.icon_url && <img src={embed.author.icon_url} alt="" className="size-6 rounded-full" />}
              <span>{embed.author.name}</span>
            </div>
          )}
          {embed.title && (
            <div className="font-semibold break-words text-[#f2f3f5]">{embed.url ? <span className="text-[#00a8fc]">{embed.title}</span> : embed.title}</div>
          )}
          {embed.description && (
            <div className="text-sm break-words whitespace-pre-wrap">
              <Markdown text={embed.description} />
            </div>
          )}
          {fields.length > 0 && (
            <div className="grid grid-cols-1 gap-2 sm:grid-cols-3">
              {fields.map((field) => (
                <div key={`${field.name}|${field.value}`} className={field.inline ? 'min-w-0' : 'min-w-0 sm:col-span-3'}>
                  <div className="text-sm font-semibold text-[#f2f3f5]">{field.name}</div>
                  <div className="text-sm break-words whitespace-pre-wrap">
                    <Markdown text={field.value} />
                  </div>
                </div>
              ))}
            </div>
          )}
          {embed.image && <img src={embed.image.url} alt="" className="mt-2 max-h-[300px] w-full rounded object-cover" />}
          {embed.timestamp && <div className="text-xs text-[#949ba4]">{new Date(embed.timestamp).toLocaleString()}</div>}
        </div>
        {embed.thumbnail && <img src={embed.thumbnail.url} alt="" className="size-20 shrink-0 rounded object-cover" />}
      </div>
    </div>
  )
}

/** The part of Discord's markdown the messages use: code blocks, inline code, bold, links and user mentions. */
function Markdown({ text }: { text: string }) {
  const parts: ReactNode[] = []
  const pattern = /```(?:\w*\n)?([\s\S]*?)```|`([^`]+)`|\*\*([^*]+)\*\*|\[([^\]]+)\]\((https?:\/\/[^)\s]+)\)|<@!?(\d+)>/g
  let last = 0
  for (const match of text.matchAll(pattern)) {
    const index = match.index ?? 0
    if (index > last) parts.push(text.slice(last, index))
    const key = index
    if (match[1] !== undefined) {
      parts.push(
        <code key={key} className="my-1 block rounded border border-[#1e1f22] bg-[#1e1f22] p-2 font-mono text-xs whitespace-pre-wrap">
          {match[1].replace(/\n$/, '')}
        </code>,
      )
    } else if (match[2] !== undefined) {
      parts.push(
        <code key={key} className="rounded bg-[#1e1f22] px-1 font-mono text-[0.85em]">
          {match[2]}
        </code>,
      )
    } else if (match[3] !== undefined) {
      parts.push(<strong key={key}>{match[3]}</strong>)
    } else if (match[4] !== undefined) {
      parts.push(
        <a key={key} href={match[5]} target="_blank" rel="noreferrer noopener" className="text-[#00a8fc] hover:underline">
          {match[4]}
        </a>,
      )
    } else if (match[6] !== undefined) {
      parts.push(
        <span key={key} className="rounded bg-[#5865f2]/30 px-0.5 font-medium text-[#c9cdfb]">
          @{match[6]}
        </span>,
      )
    }
    last = index + match[0].length
  }
  if (last < text.length) parts.push(text.slice(last))
  return <>{parts}</>
}
