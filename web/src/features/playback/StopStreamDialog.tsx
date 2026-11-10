import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { playback, type PlaybackSession } from '@/api/client'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { itemLabel } from './format'

/** The server takes at most this many characters. */
const maxMessage = 200

/**
 * Asks before stopping a stream on its player, with an optional message the viewer sees. The only place Bigarr writes
 * to Plex during playback: nothing stops a stream without this confirmation.
 */
export function StopStreamDialog({ session, onClose }: { session: PlaybackSession | null; onClose: () => void }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [message, setMessage] = useState('')
  const stop = useMutation({
    mutationFn: (s: PlaybackSession) => playback.stop(s.id, message.trim() || null),
    onSuccess: (_, s) => {
      toast.success(t('playback.stopped', { user: s.user.name }))
      void queryClient.invalidateQueries({ queryKey: ['playback', 'now'] })
      close()
    },
  })

  function close() {
    setMessage('')
    stop.reset()
    onClose()
  }

  const player = session ? [session.details.playerTitle ?? session.details.playerProduct, session.details.playerPlatform].filter(Boolean).join(' · ') : ''

  return (
    <Dialog open={session !== null} onOpenChange={(open) => !open && !stop.isPending && close()}>
      <DialogContent className="sm:max-w-md" data-testid="stop-stream">
        {session && (
          <form
            className="flex flex-col gap-4"
            onSubmit={(e) => {
              e.preventDefault()
              stop.mutate(session)
            }}
          >
            <DialogHeader>
              <DialogTitle>{t('playback.stopTitle')}</DialogTitle>
              <DialogDescription>{t('playback.stopDescription', { user: session.user.name, title: itemLabel(session.item), player: player || t('playback.unknownPlayer') })}</DialogDescription>
            </DialogHeader>
            <div className="flex flex-col gap-2">
              <Label htmlFor="stop-message">{t('playback.stopMessage')}</Label>
              <Input id="stop-message" value={message} maxLength={maxMessage} onChange={(e) => setMessage(e.target.value)} placeholder={t('playback.stopMessagePlaceholder')} autoComplete="off" />
            </div>
            {stop.error && (
              <Alert variant="destructive">
                <AlertDescription>{t('playback.stopFailed', { error: stop.error.message })}</AlertDescription>
              </Alert>
            )}
            <DialogFooter>
              <Button type="button" variant="outline" onClick={close} disabled={stop.isPending}>
                {t('common.cancel')}
              </Button>
              <Button type="submit" variant="destructive" disabled={stop.isPending}>
                {stop.isPending ? t('playback.stopping') : t('playback.stopConfirm')}
              </Button>
            </DialogFooter>
          </form>
        )}
      </DialogContent>
    </Dialog>
  )
}
