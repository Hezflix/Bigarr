import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Checkbox } from '@/components/ui/checkbox'
import { Label } from '@/components/ui/label'
import { AddDialog, AddError, IdSelectField } from './addParts'

/**
 * The one form that puts a film or a series into the library, whoever fills it, always from Discover: a user's request
 * (version slot, seasons), an approver's "approve with edits" (slot, seasons, profile, root folder) and the admin's add
 * (all of that plus what the library add offers: version name, monitoring, search on add, series type…, passed in as
 * `children`).
 */

export type TitleKind = 'movie' | 'series'

/** A season in the picker; `taken` when it cannot be asked for (already in the library or already requested). */
export type SeasonChoice = { seasonNumber: number; name: string; episodeCount: number | null; taken: string | null }

export type SlotPart = { fourK: boolean; onChange: (fourK: boolean) => void; standardAllowed: boolean; fourKAllowed: boolean; standardTaken?: string | null; fourKTaken?: string | null }

export type SeasonsPart = { choices: SeasonChoice[]; value: number[]; onChange: (seasons: number[]) => void; /** The approver may pick seasons someone else asked for. */ allowTaken?: boolean }

export type TargetsPart = {
  profiles: { id: number; name: string }[] | undefined
  roots: { id: number; name: string }[] | undefined
  profileId: number | ''
  onProfile: (id: number) => void
  rootId: number | ''
  onRoot: (id: number) => void
  /** A notice above the fields (no root folder yet). */
  notice?: ReactNode
}

export function TitleForm({
  kind,
  posterUrl,
  title,
  year,
  overview,
  slot,
  seasons,
  targets,
  children,
  onCancel,
  onSubmit,
  submitLabel,
  submitTestId,
  submitDisabled,
  pending,
  error,
}: {
  kind: TitleKind
  posterUrl: string | null
  title: string
  year: number | null
  overview: string | null
  slot?: SlotPart
  seasons?: SeasonsPart
  targets?: TargetsPart
  children?: ReactNode
  onCancel: () => void
  onSubmit: () => void
  submitLabel: string
  submitTestId: string
  submitDisabled?: boolean
  pending: boolean
  error: unknown
}) {
  const { t } = useTranslation()
  const noSeason = kind === 'series' && seasons !== undefined && seasons.value.length === 0
  return (
    <AddDialog
      posterUrl={posterUrl}
      title={title}
      year={year}
      overview={overview}
      onCancel={onCancel}
      onSubmit={onSubmit}
      submitLabel={submitLabel}
      submitTestId={submitTestId}
      submitDisabled={(submitDisabled ?? false) || noSeason}
      pending={pending}
    >
      {targets?.notice}
      {slot && <SlotField slot={slot} />}
      {kind === 'series' && seasons && <SeasonsField seasons={seasons} />}
      {targets && (
        <div className="grid gap-4 sm:grid-cols-2">
          <IdSelectField id="title-profile" label={t('addMovie.qualityProfile')} value={targets.profileId} onChange={targets.onProfile} options={targets.profiles?.map((p) => ({ id: p.id, label: p.name }))} />
          <IdSelectField id="title-root" label={t('addMovie.rootFolder')} value={targets.rootId} onChange={targets.onRoot} options={targets.roots?.map((r) => ({ id: r.id, label: r.name }))} />
        </div>
      )}
      {children}
      <AddError error={error} />
    </AddDialog>
  )
}

/** Standard or 4K, as two buttons; one that is not allowed or already taken is greyed with the reason under it. */
function SlotField({ slot }: { slot: SlotPart }) {
  const { t } = useTranslation()
  const options = [
    { fourK: false, label: t('discover.version.standard'), allowed: slot.standardAllowed, taken: slot.standardTaken },
    ...(slot.fourKAllowed || slot.fourK ? [{ fourK: true, label: t('discover.version.fourK'), allowed: slot.fourKAllowed, taken: slot.fourKTaken }] : []),
  ]
  if (options.length < 2) return null
  return (
    <fieldset className="grid gap-2">
      <legend className="mb-2 text-sm font-medium">{t('requests.form.version')}</legend>
      <div className="grid grid-cols-2 gap-2">
        {options.map((option) => {
          const disabled = !option.allowed || !!option.taken
          const id = option.fourK ? 'slot-4k' : 'slot-standard'
          return (
            <label
              key={id}
              htmlFor={id}
              className="flex min-h-11 cursor-pointer flex-col items-start rounded-md border px-3 py-2 text-sm transition-colors hover:bg-accent has-[:checked]:border-primary has-[:checked]:bg-primary/5 has-[:checked]:ring-1 has-[:checked]:ring-primary has-[:disabled]:cursor-not-allowed has-[:disabled]:opacity-50 has-[:focus-visible]:ring-2 has-[:focus-visible]:ring-ring"
            >
              <input
                id={id}
                type="radio"
                name="title-slot"
                className="sr-only"
                checked={slot.fourK === option.fourK}
                disabled={disabled}
                onChange={() => slot.onChange(option.fourK)}
                data-testid={id}
              />
              <span className="font-medium">{option.label}</span>
              {option.taken && <span className="text-xs text-muted-foreground">{option.taken}</span>}
            </label>
          )
        })}
      </div>
    </fieldset>
  )
}

/** Every season with a check box, "All seasons" on top; a taken season shows why and cannot be picked. */
function SeasonsField({ seasons }: { seasons: SeasonsPart }) {
  const { t } = useTranslation()
  const open = seasons.choices.filter((s) => seasons.allowTaken || !s.taken)
  const all = open.length > 0 && open.every((s) => seasons.value.includes(s.seasonNumber))
  const toggle = (n: number, on: boolean) =>
    seasons.onChange(on ? [...new Set([...seasons.value, n])].sort((a, b) => a - b) : seasons.value.filter((v) => v !== n))
  return (
    <div className="flex flex-col gap-2">
      <div className="flex items-center justify-between gap-2">
        <Label>{t('requests.form.seasons')}</Label>
        <div className="flex items-center gap-2">
          <Checkbox
            id="season-all"
            checked={all}
            disabled={open.length === 0}
            onCheckedChange={(checked) => seasons.onChange(checked === true ? open.filter((s) => s.seasonNumber > 0).map((s) => s.seasonNumber) : [])}
            data-testid="season-all"
          />
          <Label htmlFor="season-all" className="font-normal">
            {t('requests.form.allSeasons')}
          </Label>
        </div>
      </div>
      <ul className="max-h-72 divide-y overflow-y-auto rounded-md border" data-testid="season-picker">
        {seasons.choices.map((season) => {
          const disabled = !seasons.allowTaken && !!season.taken
          const id = `season-${season.seasonNumber}`
          return (
            <li key={season.seasonNumber} className="flex min-h-11 items-center gap-3 px-3 py-1.5">
              <Checkbox id={id} checked={seasons.value.includes(season.seasonNumber)} disabled={disabled} onCheckedChange={(checked) => toggle(season.seasonNumber, checked === true)} />
              <Label htmlFor={id} className="flex min-w-0 flex-1 flex-wrap items-baseline gap-x-2 font-normal">
                <span className="truncate">{season.name}</span>
                {season.episodeCount != null && <span className="text-xs text-muted-foreground">{t('discover.episodes', { count: season.episodeCount })}</span>}
              </Label>
              {season.taken && <span className="shrink-0 text-xs text-muted-foreground">{season.taken}</span>}
            </li>
          )
        })}
      </ul>
    </div>
  )
}
