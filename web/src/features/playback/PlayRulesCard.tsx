import { useEffect, useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ApiError, playback, type MinimumPlayUnit, type PlaybackSettings } from '@/api/client'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'

/** What counts as a play and when it is watched: applies to the streams that end from now on. */
export function PlayRulesCard() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const settings = useQuery({ queryKey: ['playback', 'settings'], queryFn: playback.settings })
  const [form, setForm] = useState<PlaybackSettings | null>(null)
  useEffect(() => {
    if (settings.data) setForm(settings.data)
  }, [settings.data])

  const save = useMutation({
    mutationFn: (body: PlaybackSettings) => playback.updateSettings(body),
    onSuccess: async () => {
      toast.success(t('playback.rulesSaved'))
      await queryClient.invalidateQueries({ queryKey: ['playback', 'settings'] })
    },
    onError: (e) => toast.error(e instanceof ApiError ? e.message : t('playback.rulesFailed')),
  })

  if (!form) return null
  const submit = (event: FormEvent) => {
    event.preventDefault()
    save.mutate(form)
  }

  return (
    <section className="flex flex-col gap-3" data-testid="play-rules">
      <h2 className="text-xl font-semibold tracking-tight">{t('playback.rulesTitle')}</h2>
      <p className="max-w-3xl text-sm text-muted-foreground">{t('playback.rulesHint')}</p>
      <form onSubmit={submit} className="flex flex-wrap items-end gap-4">
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="minimum-play">{t('playback.minimumPlay')}</Label>
          <div className="flex gap-2">
            <Input id="minimum-play" type="number" min={0} className="w-24" value={form.minimumPlay} onChange={(e) => setForm({ ...form, minimumPlay: Number(e.target.value) })} />
            <Select value={form.minimumPlayUnit} onValueChange={(v) => setForm({ ...form, minimumPlayUnit: v as MinimumPlayUnit })}>
              <SelectTrigger className="w-36" aria-label={t('playback.minimumPlayUnit')}>
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="seconds">{t('playback.units.seconds')}</SelectItem>
                <SelectItem value="percent">{t('playback.units.percent')}</SelectItem>
              </SelectContent>
            </Select>
          </div>
        </div>
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="watched-percent">{t('playback.watchedPercent')}</Label>
          <Input id="watched-percent" type="number" min={1} max={100} className="w-24" value={form.watchedPercent} onChange={(e) => setForm({ ...form, watchedPercent: Number(e.target.value) })} />
        </div>
        <Button type="submit" disabled={save.isPending}>
          {t('common.save')}
        </Button>
      </form>
    </section>
  )
}
