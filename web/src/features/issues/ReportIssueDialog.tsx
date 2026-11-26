import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useNavigate } from '@tanstack/react-router'
import { cn } from 'cn'
import { Loader2Icon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { type IssueScope, type IssueTitle, type IssueType, issues, type RequestMediaType } from '@/api/client'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Label } from '@/components/ui/label'
import { SeasonSelect } from '@/components/SeasonSelect'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { ISSUE_TYPES, IssueTypeIcon } from './issueBits'

const MAX_MESSAGE = 4000

const chip =
  'flex min-h-11 items-center gap-2 rounded-lg border px-3 py-2 text-left text-sm transition-colors hover:bg-accent aria-pressed:border-primary aria-pressed:bg-primary/10 aria-pressed:ring-1 aria-pressed:ring-primary'

type Props = {
  mediaType: RequestMediaType
  tmdbId: number
  tvdbId?: number | null
  title: string
  options: IssueTitle
  onClose: () => void
}

/**
 * Report what is wrong with a title of the library: the kind of problem, where (a film; for a series the whole of it, a
 * season or one episode), which version when there are several (the standard one to start with), and a message.
 */
export function ReportIssueDialog({ mediaType, tmdbId, tvdbId, title, options, onClose }: Props) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  const series = mediaType === 'series'
  const seasons = options.seasons
  const firstSeason = seasons.find((s) => s.seasonNumber > 0) ?? seasons[0]

  const [type, setType] = useState<IssueType | null>(null)
  const [scope, setScope] = useState<IssueScope>(series ? 'episode' : 'movie')
  const [season, setSeason] = useState<number | undefined>(firstSeason?.seasonNumber)
  const [episodeId, setEpisodeId] = useState<number | undefined>(undefined)
  const [version, setVersion] = useState<string | undefined>(options.versions.find((v) => v.isDefault)?.name ?? options.versions[0]?.name)
  const [message, setMessage] = useState('')

  const episodes = seasons.find((s) => s.seasonNumber === season)?.episodes ?? []
  const ready = type !== null && message.trim().length > 0 && (scope !== 'episode' || episodeId !== undefined) && (scope !== 'season' || season !== undefined)

  const report = useMutation({
    mutationFn: (picked: IssueType) =>
      issues.create({
        mediaType,
        tmdbId,
        tvdbId,
        type: picked,
        scope,
        seasonNumber: scope === 'season' ? season : null,
        episodeId: scope === 'episode' ? episodeId : null,
        versionName: version ?? null,
        message: message.trim(),
      }),
    onSuccess: async (created) => {
      toast.success(t('issues.dialog.sent'))
      await queryClient.invalidateQueries({ queryKey: ['issues'] })
      onClose()
      await navigate({ to: '/issues/$issueId', params: { issueId: String(created.id) } })
    },
  })

  const versionLabel = (v: IssueTitle['versions'][number]) =>
    v.fourK ? t('issues.dialog.versionFourK', { name: v.name }) : v.isDefault ? t('issues.dialog.versionStandard', { name: v.name }) : v.name

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="max-h-[92svh] overflow-y-auto sm:max-w-lg" data-testid="report-issue-dialog">
        <form
          className="grid gap-5"
          onSubmit={(e) => {
            e.preventDefault()
            if (ready && type) report.mutate(type)
          }}
        >
          <DialogHeader>
            <DialogTitle>{t('issues.dialog.title')}</DialogTitle>
            <DialogDescription>{t('issues.dialog.description', { title })}</DialogDescription>
          </DialogHeader>

          <fieldset className="grid gap-2">
            <legend className="mb-2 text-sm font-medium">{t('issues.dialog.type')}</legend>
            <div className="grid grid-cols-2 gap-2">
              {ISSUE_TYPES.map((value) => (
                <button key={value} type="button" className={chip} aria-pressed={type === value} onClick={() => setType(value)} data-testid={`issue-type-${value}`}>
                  <IssueTypeIcon type={value} className="shrink-0" />
                  <span className="flex min-w-0 flex-col">
                    <span className="font-medium">{t(`issues.type.${value}`)}</span>
                    <span className="text-xs text-muted-foreground max-sm:hidden">{t(`issues.typeHint.${value}`)}</span>
                  </span>
                </button>
              ))}
            </div>
          </fieldset>

          {series && (
            <fieldset className="grid gap-2">
              <legend className="mb-2 text-sm font-medium">{t('issues.dialog.scope')}</legend>
              <div className="grid grid-cols-3 gap-2">
                {(['series', 'season', 'episode'] as const).map((value) => (
                  <button key={value} type="button" className={cn(chip, 'justify-center text-center')} aria-pressed={scope === value} onClick={() => setScope(value)} data-testid={`issue-scope-${value}`}>
                    {t(value === 'series' ? 'issues.dialog.scopeSeries' : value === 'season' ? 'issues.dialog.scopeSeason' : 'issues.dialog.scopeEpisode')}
                  </button>
                ))}
              </div>
              {scope !== 'series' && (
                <div className="grid gap-2 sm:grid-cols-[minmax(0,10rem)_minmax(0,1fr)]">
                  <div className="grid gap-1.5">
                    <Label htmlFor="issue-season">{t('issues.dialog.season')}</Label>
                    <SeasonSelect
                      id="issue-season"
                      className="w-full"
                      testId="issue-season"
                      seasons={seasons.map((s) => s.seasonNumber)}
                      value={season ?? null}
                      onChange={(n) => {
                        setSeason(n)
                        setEpisodeId(undefined)
                      }}
                    />
                  </div>
                  {scope === 'episode' && (
                    <div className="grid min-w-0 gap-1.5">
                      <Label htmlFor="issue-episode">{t('issues.dialog.episode')}</Label>
                      <Select value={episodeId === undefined ? undefined : String(episodeId)} onValueChange={(v) => setEpisodeId(Number(v))}>
                        <SelectTrigger id="issue-episode" className="w-full min-w-0" data-testid="issue-episode">
                          <SelectValue placeholder={t('issues.dialog.pickEpisode')} />
                        </SelectTrigger>
                        <SelectContent>
                          {episodes.map((e) => (
                            <SelectItem key={e.id} value={String(e.id)} className={cn(!e.hasFile && 'text-muted-foreground')}>
                              <span className="truncate">
                                {`E${String(e.episodeNumber).padStart(2, '0')} · ${e.title}`}
                                {!e.hasFile && ` (${t('issues.dialog.noFile')})`}
                              </span>
                            </SelectItem>
                          ))}
                        </SelectContent>
                      </Select>
                    </div>
                  )}
                </div>
              )}
            </fieldset>
          )}

          {options.versions.length > 1 && (
            <div className="grid gap-1.5">
              <Label htmlFor="issue-version">{t('issues.dialog.version')}</Label>
              <Select value={version} onValueChange={setVersion}>
                <SelectTrigger id="issue-version" className="w-full" data-testid="issue-version">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {options.versions.map((v) => (
                    <SelectItem key={v.name} value={v.name}>
                      {versionLabel(v)}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          )}

          <div className="grid gap-1.5">
            <Label htmlFor="issue-message">{t('issues.dialog.message')}</Label>
            <textarea
              id="issue-message"
              className="min-h-24 w-full rounded-md border border-input bg-transparent px-3 py-2 text-base shadow-xs outline-none placeholder:text-muted-foreground focus-visible:border-ring focus-visible:ring-[3px] focus-visible:ring-ring/50 md:text-sm"
              value={message}
              onChange={(e) => setMessage(e.target.value)}
              maxLength={MAX_MESSAGE}
              placeholder={t('issues.dialog.messagePlaceholder')}
              data-testid="issue-message"
            />
          </div>

          {report.error && (
            <Alert variant="destructive">
              <AlertDescription>{report.error.message}</AlertDescription>
            </Alert>
          )}

          <DialogFooter>
            <Button type="button" variant="ghost" onClick={onClose}>
              {t('common.cancel')}
            </Button>
            <Button type="submit" disabled={!ready || report.isPending} data-testid="issue-submit">
              {report.isPending && <Loader2Icon className="animate-spin" />}
              {t('issues.dialog.submit')}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
