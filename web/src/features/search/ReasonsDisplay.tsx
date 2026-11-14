import { cn } from 'cn'
import { CheckIcon, ClockIcon, InfoIcon, XIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Button } from '@/components/ui/button'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import { TableCell } from '@/components/ui/table'
import type { FitStatus, VersionFit } from '@/features/search/rejectionReasons'

const statusText: Record<FitStatus, string> = {
  accepted: 'text-emerald-700 dark:text-emerald-300',
  temporary: 'text-amber-700 dark:text-amber-300',
  rejected: 'text-red-700/80 dark:text-red-300/80',
}

function StatusIcon({ status }: { status: FitStatus }) {
  const Icon = status === 'accepted' ? CheckIcon : status === 'temporary' ? ClockIcon : XIcon
  return <Icon className={cn('size-3.5 shrink-0', statusText[status])} aria-hidden="true" />
}

/** "2160p · Score 40": a version's verdict icon, name and score, the line the cell shows per version. */
function VersionLine({ fit, strong }: { fit: VersionFit; strong: boolean }) {
  const { t } = useTranslation()
  return (
    <span className="flex h-5 min-w-0 items-center gap-1">
      <StatusIcon status={fit.status} />
      <span className={cn('shrink-0 text-xs font-medium whitespace-nowrap', strong && fit.status === 'accepted' && 'text-foreground')}>
        {fit.versionName}
        {fit.score !== 0 && <span className="font-normal text-muted-foreground"> · {t('search.score', { score: fit.score })}</span>}
      </span>
    </span>
  )
}

/** Every reason of one version, wrapped in full, as the popover lists them. */
function FitDetails({ fit }: { fit: VersionFit }) {
  const { t } = useTranslation()
  return (
    <div className="flex flex-col gap-1">
      <VersionLine fit={fit} strong />
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

/**
 * The "fit per version" cell: one line per version (verdict icon, name, score), so a rejected row is as tall as an
 * accepted one. When any version has a reason, an info button opens every version's reasons in a popover; it opens on
 * click, tap, Enter or Space and closes on Escape or a click outside.
 */
export function FitCell({
  fits,
  matches,
  testId,
}: {
  fits: VersionFit[]
  /** The release is this film or show: an accepted verdict is then worth bold. */
  matches: boolean
  testId: string
}) {
  const { t } = useTranslation()
  const anyReason = fits.some((f) => f.status !== 'accepted')
  return (
    <TableCell className="align-top" data-testid={testId}>
      <span className="flex items-start gap-1">
        <span className="flex min-w-0 flex-col gap-1">
          {fits.map((fit) => (
            <VersionLine key={fit.versionId} fit={fit} strong={matches} />
          ))}
        </span>
        {anyReason && (
          <Popover>
            <PopoverTrigger asChild>
              {/* 24 px to look at; on a touch screen an invisible 40 px area takes the tap, without making the row taller. */}
              <Button
                type="button"
                variant="ghost"
                size="icon-xs"
                className="relative -my-0.5 text-muted-foreground after:absolute after:-inset-2 pointer-fine:after:hidden"
                aria-label={t('search.reasons.why')}
                data-testid="reasons-trigger"
              >
                <InfoIcon />
              </Button>
            </PopoverTrigger>
            <PopoverContent align="start" className="w-[min(22rem,calc(100vw-2rem))] p-3" aria-label={t('search.reasons.why')} data-testid="reasons-popover">
              <div className="flex flex-col gap-3">
                {fits.map((f) => (
                  <FitDetails key={f.versionId} fit={f} />
                ))}
              </div>
            </PopoverContent>
          </Popover>
        )}
      </span>
    </TableCell>
  )
}
