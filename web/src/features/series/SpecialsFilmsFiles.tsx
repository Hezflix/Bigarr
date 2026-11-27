import type { ReactNode } from 'react'
import { ClapperboardIcon, TvIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import type { FilmFileStep, SeriesFilmFileInfo, SeriesFilmReviewRow } from '@/api/client'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { formatBytes, formatDate } from '@/i18n'
import { cn } from '@/lib/utils'
import { channelLayout } from './episodeColumns'

/** Which file stays when the special and the film both have one. */
export type Keep = 'film' | 'special'

/** The plan linking runs for the chosen side: the special's file only when that choice exists. */
export function planFor(row: SeriesFilmReviewRow, keep: Keep): FilmFileStep[] {
  return keep === 'special' && row.keepSpecialPlan ? row.keepSpecialPlan : row.plan
}

function resolution(file: SeriesFilmFileInfo): string | null {
  return file.width && file.height ? `${file.width}×${file.height}` : null
}

function audio(file: SeriesFilmFileInfo): string | null {
  return [file.audioCodec, channelLayout(file.audioChannels)].filter(Boolean).join(' ') || null
}

/** "Bluray-1080p · 1920×1080 · HEVC · 4.2 GB": what tells two files apart at a glance. */
export function fileSummary(file: SeriesFilmFileInfo): string {
  return [file.quality, resolution(file), file.videoCodec?.toUpperCase(), file.dynamicRange, formatBytes(file.size)].filter(Boolean).join(' · ')
}

/** The table's files column: one line per side, and the choice when both have a file. */
export function FilesCell({ row, keep, onKeep }: { row: SeriesFilmReviewRow; keep: Keep; onKeep: (keep: Keep) => void }) {
  const { t } = useTranslation()
  const special = row.specialFiles[0]
  return (
    <span className="flex flex-col gap-1 text-xs" data-testid="specials-films-files">
      <span className="flex items-start gap-1.5">
        <TvIcon className="mt-0.5 size-3.5 shrink-0 text-muted-foreground" aria-label={t('specialsFilms.side.special')} />
        <span className={cn(!special && 'text-muted-foreground')}>
          {special ? (special.isFilmLink ? t('specialsFilms.facts.isFilmLink') : fileSummary(special)) : t('specialsFilms.noFile')}
          {row.specialFiles.length > 1 && ` ${t('specialsFilms.moreVersions', { count: row.specialFiles.length - 1 })}`}
        </span>
      </span>
      <span className="flex items-start gap-1.5">
        <ClapperboardIcon className="mt-0.5 size-3.5 shrink-0 text-muted-foreground" aria-label={t('specialsFilms.side.film')} />
        <span className={cn(!row.filmFile && 'text-muted-foreground')}>{row.filmFile ? fileSummary(row.filmFile) : t('specialsFilms.noFile')}</span>
      </span>
      {row.keepSpecialPlan && <KeepChoice keep={keep} onKeep={onKeep} />}
    </span>
  )
}

export function KeepChoice({ keep, onKeep }: { keep: Keep; onKeep: (keep: Keep) => void }) {
  const { t } = useTranslation()
  return (
    <span className="flex flex-wrap items-center gap-1 pt-0.5" data-testid="specials-films-keep">
      <span className="text-amber-700 dark:text-amber-300">{t('specialsFilms.keep.label')}</span>
      {(['film', 'special'] as const).map((side) => (
        <Button key={side} type="button" size="xs" variant={keep === side ? 'secondary' : 'ghost'} aria-pressed={keep === side} onClick={() => onKeep(side)} data-keep={side}>
          {side === 'film' ? <ClapperboardIcon /> : <TvIcon />}
          {t(`specialsFilms.keep.${side}`)}
        </Button>
      ))}
    </span>
  )
}

/** Both sides in full, side by side: what each file is and where it sits, to choose which one stays. */
export function FileCompare({ row, keep, onKeep }: { row: SeriesFilmReviewRow; keep: Keep; onKeep: (keep: Keep) => void }) {
  const { t } = useTranslation()
  const choice = row.keepSpecialPlan !== null
  return (
    <div className="grid gap-2 sm:grid-cols-2" data-testid="specials-films-compare">
      <Side
        name={`keep-${row.episodeId}`}
        icon={<TvIcon className="size-4" />}
        title={t('specialsFilms.side.special')}
        subtitle={`${row.seriesTitle} ${row.label}`}
        files={row.specialFiles}
        state={choice ? (keep === 'special' ? 'kept' : 'recycled') : null}
        onPick={choice ? () => onKeep('special') : undefined}
      />
      <Side
        name={`keep-${row.episodeId}`}
        icon={<ClapperboardIcon className="size-4" />}
        title={t('specialsFilms.side.film')}
        subtitle={row.filmTitle ? (row.filmYear ? `${row.filmTitle} (${row.filmYear})` : row.filmTitle) : `TMDB ${row.tmdbId ?? '—'}`}
        files={row.filmFile ? [row.filmFile] : []}
        state={choice ? (keep === 'film' ? 'kept' : 'recycled') : null}
        onPick={choice ? () => onKeep('film') : undefined}
      />
    </div>
  )
}

function Side({
  name,
  icon,
  title,
  subtitle,
  files,
  state,
  onPick,
}: {
  name: string
  icon: ReactNode
  title: string
  subtitle: string
  files: SeriesFilmFileInfo[]
  state: 'kept' | 'recycled' | null
  onPick?: () => void
}) {
  const { t } = useTranslation()
  const heading = (
    <>
      {icon}
      {title}
      {state && (
        <Badge variant={state === 'kept' ? 'secondary' : 'outline'} className={cn('ml-auto', state === 'recycled' && 'text-muted-foreground')}>
          {t(`specialsFilms.keep.${state}`)}
        </Badge>
      )}
    </>
  )
  return (
    <div className={cn('flex flex-col gap-0.5 rounded-md border p-2.5', state === 'kept' && 'border-primary ring-1 ring-primary', state === 'recycled' && 'opacity-80')} data-side-state={state ?? undefined}>
      {onPick ? (
        <label className="flex cursor-pointer items-center gap-1.5 text-sm font-medium">
          <input type="radio" name={name} checked={state === 'kept'} onChange={onPick} className="size-3.5 accent-primary" />
          {heading}
        </label>
      ) : (
        <span className="flex items-center gap-1.5 text-sm font-medium">{heading}</span>
      )}
      <span className="block truncate text-xs text-muted-foreground">{subtitle}</span>
      {files.length === 0 ? <span className="block pt-1 text-xs text-muted-foreground">{t('specialsFilms.noFile')}</span> : files.map((f) => <Facts key={f.path} file={f} />)}
    </div>
  )
}

function Facts({ file }: { file: SeriesFilmFileInfo }) {
  const { t } = useTranslation()
  const rows: [string, string | null][] = [
    [t('specialsFilms.facts.version'), file.version],
    [t('specialsFilms.facts.quality'), file.quality],
    [t('specialsFilms.facts.resolution'), resolution(file)],
    [t('specialsFilms.facts.video'), [file.videoCodec?.toUpperCase(), file.dynamicRange].filter(Boolean).join(' · ') || null],
    [t('specialsFilms.facts.audio'), audio(file)],
    [t('specialsFilms.facts.languages'), file.languages.filter((l) => l !== 'Unknown').join(', ') || null],
    [t('specialsFilms.facts.group'), file.releaseGroup],
    [t('specialsFilms.facts.runtime'), file.runTimeMinutes ? t('seriesFilms.minutes', { count: file.runTimeMinutes }) : null],
    [t('specialsFilms.facts.size'), formatBytes(file.size)],
    [t('specialsFilms.facts.added'), formatDate(file.dateAdded)],
  ]
  return (
    <span className="flex flex-col gap-1 pt-1.5">
      {file.isFilmLink && <span className="text-xs text-muted-foreground">{t('specialsFilms.facts.isFilmLink')}</span>}
      <dl className="grid grid-cols-[auto_1fr] gap-x-3 gap-y-0.5 text-xs">
        {rows
          .filter(([, value]) => value)
          .map(([label, value]) => (
            <div key={label} className="contents">
              <dt className="text-muted-foreground">{label}</dt>
              <dd className="tabular-nums">{value}</dd>
            </div>
          ))}
      </dl>
      <span className="break-all font-mono text-[11px] text-muted-foreground">{file.path}</span>
    </span>
  )
}

/** The file operations, one per line, in plain words, with where each file comes from and where it goes. */
export function Plan({ steps }: { steps: FilmFileStep[] }) {
  const { t } = useTranslation()
  return (
    <ol className="flex flex-col gap-1.5 text-xs" data-testid="specials-films-plan">
      {steps.map((step, i) => (
        <li
          key={`${step.kind}|${step.version ?? ''}|${step.from ?? ''}|${step.to ?? ''}`}
          className={cn('flex gap-2', step.kind === 'linkError' && 'text-destructive', (step.kind === 'filmFileWins' || step.kind === 'recycleFilmFile') && 'text-amber-700 dark:text-amber-300')}
          data-kind={step.kind}
        >
          <span className="w-4 shrink-0 text-right tabular-nums text-muted-foreground">{i + 1}.</span>
          <span className="flex min-w-0 flex-col gap-0.5">
            <span>{t(`specialsFilms.step.${step.kind}`, { version: step.version ?? '', note: step.note ?? '' })}</span>
            {step.from && <PathLine label={t('specialsFilms.plan.from')} path={step.from} />}
            {step.to && <PathLine label={t('specialsFilms.plan.to')} path={step.to} />}
          </span>
        </li>
      ))}
    </ol>
  )
}

function PathLine({ label, path }: { label: string; path: string }) {
  return (
    <span className="flex gap-1.5 text-[11px] text-muted-foreground">
      <span className="w-8 shrink-0">{label}</span>
      <span className="break-all font-mono">{path}</span>
    </span>
  )
}
