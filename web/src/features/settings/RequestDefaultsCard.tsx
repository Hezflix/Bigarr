import { useEffect, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { profilesFor, quality, type RequestDefaults, requests, rootFolders, rootsOfKind } from '@/api/client'
import { NullableIdSelect } from '@/components/IdSelect'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'

type Key = keyof RequestDefaults

const ROWS: { kind: 'movies' | 'series'; fourK: boolean; profile: Key; root: Key }[] = [
  { kind: 'movies', fourK: false, profile: 'movieProfileId', root: 'movieRootFolderId' },
  { kind: 'movies', fourK: true, profile: 'movieFourKProfileId', root: 'movieFourKRootFolderId' },
  { kind: 'series', fourK: false, profile: 'seriesProfileId', root: 'seriesRootFolderId' },
  { kind: 'series', fourK: true, profile: 'seriesFourKProfileId', root: 'seriesFourKRootFolderId' },
]

const EMPTY: RequestDefaults = {
  movieProfileId: null,
  movieRootFolderId: null,
  movieFourKProfileId: null,
  movieFourKRootFolderId: null,
  seriesProfileId: null,
  seriesRootFolderId: null,
  seriesFourKProfileId: null,
  seriesFourKRootFolderId: null,
}

/** Settings → Discover, requests: the profile and root folder an approval takes per type and slot, unless the approver picks another. */
export function RequestDefaultsCard() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const settings = useQuery({ queryKey: ['requests', 'settings'], queryFn: requests.settings })
  const profiles = useQuery({ queryKey: ['qualityProfiles'], queryFn: quality.profiles })
  const roots = useQuery({ queryKey: ['rootFolders'], queryFn: rootFolders.list })
  const [draft, setDraft] = useState<RequestDefaults>(EMPTY)
  useEffect(() => {
    if (settings.data) setDraft(settings.data)
  }, [settings.data])
  const save = useMutation({
    mutationFn: () => requests.updateSettings(draft),
    onSuccess: (data) => {
      queryClient.setQueryData(['requests', 'settings'], data)
      toast.success(t('requests.defaults.saved'))
    },
    onError: (error) => toast.error(error.message),
  })

  const field = (key: Key, label: string, options: { id: number; label: string }[]) => (
    <NullableIdSelect value={draft[key]} onChange={(id) => setDraft({ ...draft, [key]: id })} noneLabel={t('requests.defaults.auto')} options={options} ariaLabel={label} testId={`request-default-${key}`} />
  )

  return (
    <Card data-testid="request-defaults">
      <CardHeader>
        <CardTitle>{t('requests.defaults.title')}</CardTitle>
        <CardDescription>{t('requests.defaults.hint')}</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        {ROWS.map((row) => {
          const name = `${t(row.kind === 'movies' ? 'discover.movies' : 'discover.series')} · ${t(row.fourK ? 'discover.version.fourK' : 'discover.version.standard')}`
          const profileOptions = (profiles.data ? profilesFor(row.kind)(profiles.data) : []).map((p) => ({ id: p.id, label: p.name }))
          const rootOptions = (roots.data ? rootsOfKind(row.kind)(roots.data) : []).map((r) => ({ id: r.id, label: r.path }))
          return (
            <div key={row.profile} className="grid gap-2 sm:grid-cols-[10rem_1fr_1fr] sm:items-center">
              <span className="text-sm font-medium">{name}</span>
              {field(row.profile, `${name} · ${t('addMovie.qualityProfile')}`, profileOptions)}
              {field(row.root, `${name} · ${t('addMovie.rootFolder')}`, rootOptions)}
            </div>
          )
        })}
        <div>
          <Button onClick={() => save.mutate()} disabled={save.isPending || !settings.data} data-testid="request-defaults-save">
            {t('common.save')}
          </Button>
        </div>
      </CardContent>
    </Card>
  )
}
