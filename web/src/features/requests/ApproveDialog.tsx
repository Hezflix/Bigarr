import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Loader2Icon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { discover, type MediaRequest, requests } from '@/api/client'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { TitleForm } from '@/features/library/TitleForm'
import { seasonChoices } from './RequestDialog'

/**
 * Approve with edits, for an admin or a request manager: the same form as the request and the library add, with the
 * slot, the seasons, and the profile and root folder (by name) the approval takes, the defaults picked to start with.
 */
export function ApproveDialog({ request, onClose }: { request: MediaRequest; onClose: () => void }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const options = useQuery({ queryKey: ['requests', 'options', request.mediaType], queryFn: () => requests.options(request.mediaType) })
  const show = useQuery({ queryKey: ['discover', 'tv', String(request.tmdbId)], queryFn: () => discover.show(request.tmdbId), enabled: request.mediaType === 'series' })
  const [fourK, setFourK] = useState(request.fourK)
  const [seasons, setSeasons] = useState<number[]>(request.seasons)
  const [profileId, setProfileId] = useState<number | ''>('')
  const [rootId, setRootId] = useState<number | ''>('')

  const o = options.data
  const defaultProfile = (fourK ? o?.fourKProfileId : o?.standardProfileId) ?? ''
  const defaultRoot = (fourK ? o?.fourKRootFolderId : o?.standardRootFolderId) ?? ''
  const profile = profileId === '' ? defaultProfile : profileId
  const root = rootId === '' ? defaultRoot : rootId

  // The approver may give seasons someone asked for; a season already in the library only says so.
  const choices = show.data
    ? seasonChoices(t, show.data.seasons, fourK ? 'fourK' : 'standard').map((c) => (request.seasons.includes(c.seasonNumber) ? { ...c, taken: null } : c))
    : request.seasons.map((n) => ({ seasonNumber: n, name: t('requests.season', { number: n }), episodeCount: null, taken: null }))

  const approve = useMutation({
    mutationFn: () =>
      requests.approve(request.id, {
        fourK,
        seasons: request.mediaType === 'series' ? seasons : null,
        qualityProfileId: profile === '' ? null : profile,
        rootFolderId: root === '' ? null : root,
      }),
    onSuccess: async (done) => {
      toast[done.state === 'failed' ? 'error' : 'success'](t(done.state === 'failed' ? 'requests.approve.failed' : 'requests.approve.done', { title: done.title }))
      await Promise.all([queryClient.invalidateQueries({ queryKey: ['requests'] }), queryClient.invalidateQueries({ queryKey: ['discover'] })])
      onClose()
    },
  })

  return (
    <TitleForm
      kind={request.mediaType === 'movie' ? 'movie' : 'series'}
      posterUrl={request.posterUrl}
      title={request.title}
      year={request.year}
      overview={request.requestedBy ? t('requests.approve.askedBy', { user: request.requestedBy.username }) : null}
      slot={{ fourK, onChange: setFourK, standardAllowed: true, fourKAllowed: o?.fourKVersion != null }}
      seasons={request.mediaType === 'series' ? { choices, value: seasons, onChange: setSeasons, allowTaken: true } : undefined}
      targets={{
        profiles: o?.profiles,
        roots: o?.rootFolders,
        profileId: profile,
        onProfile: setProfileId,
        rootId: root,
        onRoot: setRootId,
      }}
      onCancel={onClose}
      onSubmit={() => approve.mutate()}
      submitLabel={t('requests.actions.approve')}
      submitTestId="approve-submit"
      submitDisabled={!o}
      pending={approve.isPending}
      error={approve.error}
    />
  )
}

export function DeclineDialog({ request, onClose }: { request: MediaRequest; onClose: () => void }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [reason, setReason] = useState('')
  const decline = useMutation({
    mutationFn: () => requests.decline(request.id, reason.trim() || null),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['requests'] })
      onClose()
    },
  })
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="sm:max-w-md" data-testid="decline-dialog">
        <form
          className="grid gap-4"
          onSubmit={(e) => {
            e.preventDefault()
            decline.mutate()
          }}
        >
          <DialogHeader>
            <DialogTitle>{t('requests.decline.title', { title: request.title })}</DialogTitle>
            <DialogDescription>{t('requests.decline.hint')}</DialogDescription>
          </DialogHeader>
          <div className="grid gap-2">
            <Label htmlFor="decline-reason">{t('requests.decline.reason')}</Label>
            <Input id="decline-reason" value={reason} onChange={(e) => setReason(e.target.value)} maxLength={1024} placeholder={t('requests.decline.reasonPlaceholder')} />
          </div>
          <DialogFooter>
            <Button type="button" variant="ghost" onClick={onClose}>
              {t('common.cancel')}
            </Button>
            <Button type="submit" variant="destructive" disabled={decline.isPending} data-testid="decline-submit">
              {decline.isPending && <Loader2Icon className="animate-spin" />}
              {t('requests.actions.decline')}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
