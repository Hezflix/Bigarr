import { useSyncExternalStore, type ReactNode } from 'react'
import { cn } from 'cn'
import { CheckIcon, ChevronDownIcon, ClockIcon, InfoIcon, PanelBottomOpenIcon, XIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Button } from '@/components/ui/button'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import { TableCell, TableRow } from '@/components/ui/table'
import { bestStatus, type FitStatus, parseReasonsVariant, type ReasonsVariant, reasonsPicked, reasonsVariants, type VersionFit } from '@/features/search/rejectionReasons'

/*
 * TEMPORARY: the six rejection-reason displays compared in the interactive searches (`?reasons=1..6`). Once the owner
 * picks one, the switch, the picker and the other five go, and the chosen one becomes the fit cell.
 */

// ---- the switch (the URL is the source; the picker rewrites it) ------------------------------------------------------

const listeners = new Set<() => void>()
const subscribe = (listener: () => void) => {
  listeners.add(listener)
  window.addEventListener('popstate', listener)
  return () => {
    listeners.delete(listener)
    window.removeEventListener('popstate', listener)
  }
}

export function useReasonsVariant(): ReasonsVariant {
  return useSyncExternalStore(subscribe, () => parseReasonsVariant(window.location.search))
}

function setReasonsVariant(variant: ReasonsVariant) {
  const url = new URL(window.location.href)
  url.searchParams.set('reasons', String(variant))
  window.history.replaceState(window.history.state, '', url)
  for (const listener of listeners) listener()
}

/** Shown only when the URL picks a display, so screenshots of one display can be taken without it. */
export function ReasonsVariantPicker() {
  const { t } = useTranslation()
  const variant = useReasonsVariant()
  if (!reasonsPicked(window.location.search)) return null
  return (
    <fieldset className="flex flex-wrap items-center gap-1 rounded-md border border-dashed px-2 py-1 text-xs" data-testid="reasons-picker">
      <legend className="sr-only">{t('search.reasons.picker')}</legend>
      <span aria-hidden="true" className="text-muted-foreground">
        {t('search.reasons.picker')}
      </span>
      {reasonsVariants.map((v) => (
        <Button key={v} type="button" size="xs" variant={v === variant ? 'default' : 'ghost'} aria-pressed={v === variant} title={t(`search.reasons.variant${v}`)} onClick={() => setReasonsVariant(v)}>
          {v}
        </Button>
      ))}
      <span className="text-muted-foreground">{t(`search.reasons.variant${variant}`)}</span>
    </fieldset>
  )
}

// ---- shared pieces ----------------------------------------------------------------------------------------------------

const statusText: Record<FitStatus, string> = {
  accepted: 'text-emerald-700 dark:text-emerald-300',
  temporary: 'text-amber-700 dark:text-amber-300',
  rejected: 'text-red-700/80 dark:text-red-300/80',
}

/** 6: the bar on the fit cell's left edge, the best verdict any version gives. On the fit cell rather than the row's first cell so it shows wherever the verdicts do, a phone scrolled to them included. */
const fitAccent: Record<FitStatus, string> = {
  accepted: 'shadow-[inset_3px_0_0_var(--color-emerald-500)]',
  temporary: 'shadow-[inset_3px_0_0_var(--color-amber-500)]',
  rejected: 'shadow-[inset_3px_0_0_var(--color-red-500)]',
}

function StatusIcon({ status, className }: { status: FitStatus; className?: string }) {
  const Icon = status === 'accepted' ? CheckIcon : status === 'temporary' ? ClockIcon : XIcon
  return <Icon className={cn('size-3.5 shrink-0', statusText[status], className)} aria-hidden="true" />
}

/** "2160p · Score 40", what every display starts a version's line with. */
function VersionName({ fit, strong }: { fit: VersionFit; strong: boolean }) {
  const { t } = useTranslation()
  return (
    <span className={cn('shrink-0 text-xs font-medium whitespace-nowrap', strong && fit.status === 'accepted' && 'text-foreground')}>
      {fit.versionName}
      {fit.score !== 0 && <span className="font-normal text-muted-foreground"> · {t('search.score', { score: fit.score })}</span>}
    </span>
  )
}

/** Every reason of one version, wrapped in full: what the popovers, the expanded row and the side panel show. */
function FitDetails({ fit }: { fit: VersionFit }) {
  const { t } = useTranslation()
  return (
    <div className="flex flex-col gap-1">
      <span className="flex items-center gap-1">
        <StatusIcon status={fit.status} />
        <VersionName fit={fit} strong />
      </span>
      {fit.status === 'accepted' ? (
        <span className={cn('pl-[1.125rem] text-xs', statusText.accepted)}>{t('search.approved')}</span>
      ) : (
        <ul className="flex flex-col gap-0.5 pl-[1.125rem] text-xs">
          {fit.reasons.map((r) => (
            <li key={r.reason} className={cn('[overflow-wrap:anywhere]', r.temporary ? statusText.temporary : statusText.rejected)}>
              {r.reason}
              {r.temporary && <span className="text-muted-foreground"> · {t('search.reasons.temporary')}</span>}
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

function AllFitDetails({ fits }: { fits: VersionFit[] }) {
  return (
    <div className="flex flex-col gap-3">
      {fits.map((f) => (
        <FitDetails key={f.versionId} fit={f} />
      ))}
    </div>
  )
}

/** A popover that opens on click, tap, Enter or Space, and closes on Escape or a click outside. */
function ReasonsPopover({ trigger, children, label }: { trigger: ReactNode; children: ReactNode; label: string }) {
  return (
    <Popover>
      <PopoverTrigger asChild>{trigger}</PopoverTrigger>
      <PopoverContent align="start" className="w-[min(22rem,calc(100vw-2rem))] p-3" aria-label={label} data-testid="reasons-popover">
        {children}
      </PopoverContent>
    </Popover>
  )
}

const lineClass = 'flex h-5 min-w-0 items-center gap-1'
const triggerClass = 'rounded-sm outline-none focus-visible:ring-[3px] focus-visible:ring-ring/50'

// ---- the six displays ---------------------------------------------------------------------------------------------------

/** 1 (recommended): the first reason as light text, "+N" for the others; the whole line opens every reason. */
function FirstReasonLine({ fit, strong }: { fit: VersionFit; strong: boolean }) {
  const { t } = useTranslation()
  if (fit.status === 'accepted')
    return (
      <span className={lineClass}>
        <StatusIcon status="accepted" />
        <VersionName fit={fit} strong={strong} />
        <span className={cn('truncate text-xs', statusText.accepted)}>{t('search.approved')}</span>
      </span>
    )
  const [first, ...rest] = fit.reasons
  return (
    <ReasonsPopover
      label={t('search.reasons.whyFor', { version: fit.versionName })}
      trigger={
        <button type="button" className={cn(lineClass, triggerClass, 'w-full text-left hover:bg-muted/60')} aria-label={t('search.reasons.whyFor', { version: fit.versionName })} data-testid="reasons-trigger">
          <StatusIcon status={fit.status} />
          <VersionName fit={fit} strong={strong} />
          <span className={cn('min-w-0 truncate text-xs', first?.temporary ? statusText.temporary : statusText.rejected)}>{first?.reason}</span>
          {rest.length > 0 && <span className="shrink-0 rounded-full bg-muted px-1.5 text-[0.6875rem] leading-4 font-medium text-muted-foreground tabular-nums">+{rest.length}</span>}
        </button>
      }
    >
      <FitDetails fit={fit} />
    </ReasonsPopover>
  )
}

/** The verdict and the name, nothing else: what 2, 5 and 6 list per version. */
function PlainLine({ fit, strong, children }: { fit: VersionFit; strong: boolean; children?: ReactNode }) {
  return (
    <span className={lineClass}>
      <StatusIcon status={fit.status} />
      <VersionName fit={fit} strong={strong} />
      {children}
    </span>
  )
}

/** 3: a light red line under the name and score, one line, truncated; hover shows it all, a tap opens it. */
function SubtleTextBlock({ fit, strong }: { fit: VersionFit; strong: boolean }) {
  const { t } = useTranslation()
  const text = fit.reasons.map((r) => r.reason).join(' · ')
  return (
    <span className="flex min-w-0 flex-col">
      <PlainLine fit={fit} strong={strong} />
      {fit.status === 'accepted' ? (
        <span className={cn('h-4 truncate pl-[1.125rem] text-[0.6875rem] leading-4', statusText.accepted)}>{t('search.approved')}</span>
      ) : (
        <ReasonsPopover
          label={t('search.reasons.whyFor', { version: fit.versionName })}
          trigger={
            <button
              type="button"
              title={text}
              className={cn(triggerClass, 'block h-4 max-w-full truncate pl-[1.125rem] text-left text-[0.6875rem] leading-4 hover:underline', statusText[fit.status])}
              aria-label={t('search.reasons.whyFor', { version: fit.versionName })}
              data-testid="reasons-trigger"
            >
              {text}
            </button>
          }
        >
          <FitDetails fit={fit} />
        </ReasonsPopover>
      )}
    </span>
  )
}

/** 4: a fixed-width "Rejected · N" chip before the name; the chip opens the reasons. */
function CountChipLine({ fit, strong }: { fit: VersionFit; strong: boolean }) {
  const { t } = useTranslation()
  const chip = cn(
    'inline-flex h-5 w-[6.75rem] shrink-0 items-center gap-1 rounded-full border px-2 text-[0.6875rem] font-medium whitespace-nowrap',
    fit.status === 'accepted' && 'border-emerald-600/30 bg-emerald-500/10',
    fit.status === 'temporary' && 'border-amber-600/30 bg-amber-500/10',
    fit.status === 'rejected' && 'border-red-600/30 bg-red-500/10',
    statusText[fit.status],
  )
  const label = fit.status === 'accepted' ? t('search.approved') : t(fit.status === 'temporary' ? 'search.reasons.waitingCount' : 'search.reasons.rejectedCount', { count: fit.reasons.length })
  return (
    <span className={lineClass}>
      {fit.status === 'accepted' ? (
        <span className={chip}>
          <StatusIcon status="accepted" className="size-3" />
          {label}
        </span>
      ) : (
        <ReasonsPopover
          label={t('search.reasons.whyFor', { version: fit.versionName })}
          trigger={
            <button type="button" className={cn(chip, triggerClass, 'hover:brightness-95')} aria-label={`${label}: ${t('search.reasons.whyFor', { version: fit.versionName })}`} data-testid="reasons-trigger">
              <StatusIcon status={fit.status} className="size-3" />
              {label}
            </button>
          }
        >
          <FitDetails fit={fit} />
        </ReasonsPopover>
      )}
      <VersionName fit={fit} strong={strong} />
    </span>
  )
}

// ---- the cell, the expanded row and the side panel ---------------------------------------------------------------------

/**
 * The "fit per version" cell, one line per version in every display (two short ones in 3), so a rejected row is as tall
 * as an accepted one. 5 and 6 put the reasons outside the cell: the caller renders FitExpandedRow or FitSidePanel.
 */
export function FitCell({
  variant,
  fits,
  matches,
  expanded,
  onToggle,
  detailsId,
  testId,
}: {
  variant: ReasonsVariant
  fits: VersionFit[]
  /** The release is this film or show: an accepted verdict is then worth bold. */
  matches: boolean
  /** 5: the row's detail line is open; 6: the row is the one the side panel shows. */
  expanded: boolean
  onToggle: () => void
  /** The id of the expanded row or the side panel, for aria-controls. */
  detailsId: string
  testId: string
}) {
  const { t } = useTranslation()
  const anyReason = fits.some((f) => f.status !== 'accepted')

  const lines = fits.map((fit) => {
    const props = { fit, strong: matches }
    switch (variant) {
      case 1:
        return <FirstReasonLine key={fit.versionId} {...props} />
      case 3:
        return <SubtleTextBlock key={fit.versionId} {...props} />
      case 4:
        return <CountChipLine key={fit.versionId} {...props} />
      default:
        return <PlainLine key={fit.versionId} {...props} />
    }
  })

  let action: ReactNode = null
  if (variant === 2 && anyReason)
    action = (
      <ReasonsPopover
        label={t('search.reasons.why')}
        trigger={
          <Button type="button" variant="ghost" size="icon-xs" className="-my-0.5 text-muted-foreground" aria-label={t('search.reasons.why')} data-testid="reasons-trigger">
            <InfoIcon />
          </Button>
        }
      >
        <AllFitDetails fits={fits} />
      </ReasonsPopover>
    )
  if (variant === 5 && anyReason)
    action = (
      <Button
        type="button"
        variant="ghost"
        size="icon-xs"
        className="-my-0.5 text-muted-foreground"
        aria-expanded={expanded}
        aria-controls={expanded ? detailsId : undefined}
        aria-label={t(expanded ? 'search.reasons.hideDetails' : 'search.reasons.showDetails')}
        onClick={onToggle}
        data-testid="reasons-trigger"
      >
        <ChevronDownIcon className={cn('transition-transform', expanded && 'rotate-180')} />
      </Button>
    )
  if (variant === 6 && anyReason)
    action = (
      <Button
        type="button"
        variant={expanded ? 'secondary' : 'ghost'}
        size="xs"
        className="-my-0.5 text-muted-foreground"
        aria-pressed={expanded}
        aria-controls={detailsId}
        aria-label={t('search.reasons.showInPanel')}
        onClick={onToggle}
        data-testid="reasons-trigger"
      >
        <PanelBottomOpenIcon />
        {t('search.reasons.whyShort')}
      </Button>
    )

  return (
    <TableCell className={cn('align-top', variant === 6 && fits.length > 0 && cn('pl-3', fitAccent[bestStatus(fits)]))} data-testid={testId} data-reasons-variant={variant}>
      <span className="flex items-start gap-1">
        <span className={cn('flex min-w-0 flex-col gap-1', (variant === 1 || variant === 3) && 'w-[14rem]')}>{lines}</span>
        {action}
      </span>
    </TableCell>
  )
}

/** 5: the line under an expanded row, every version's verdict with its reasons in full. */
export function FitExpandedRow({ id, fits, colSpan }: { id: string; fits: VersionFit[]; colSpan: number }) {
  const { t } = useTranslation()
  return (
    <TableRow className="bg-muted/30 hover:bg-muted/30" data-testid="reasons-expanded">
      <TableCell colSpan={colSpan} className="whitespace-normal">
        <section id={id} aria-label={t('search.reasons.panelTitle')} className="sticky left-2 grid max-w-[min(56rem,calc(100vw-3rem))] gap-3 sm:grid-cols-2">
          {fits.map((f) => (
            <FitDetails key={f.versionId} fit={f} />
          ))}
        </section>
      </TableCell>
    </TableRow>
  )
}

/** 6: the area under the table showing the picked row's verdicts; the row itself only carries a colour. */
export function FitSidePanel({ id, title, fits, onClose }: { id: string; title: string | undefined; fits: VersionFit[] | undefined; onClose: () => void }) {
  const { t } = useTranslation()
  return (
    <section id={id} aria-live="polite" aria-label={t('search.reasons.panelTitle')} className="rounded-lg border bg-muted/30 p-3 text-sm" data-testid="reasons-panel">
      {title && fits ? (
        <div className="flex flex-col gap-2">
          <div className="flex items-start gap-2">
            <p className="min-w-0 flex-1 text-xs font-medium [overflow-wrap:anywhere]">{title}</p>
            <Button type="button" variant="ghost" size="icon-xs" aria-label={t('common.close')} onClick={onClose}>
              <XIcon />
            </Button>
          </div>
          <div className="grid gap-3 sm:grid-cols-2">
            {fits.map((f) => (
              <FitDetails key={f.versionId} fit={f} />
            ))}
          </div>
        </div>
      ) : (
        <p className="text-xs text-muted-foreground">{t('search.reasons.panelEmpty')}</p>
      )}
    </section>
  )
}
