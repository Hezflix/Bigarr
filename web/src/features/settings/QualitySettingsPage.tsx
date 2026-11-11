import { useEffect, useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ArrowDownIcon, ArrowUpIcon, PencilIcon, PlusIcon, Trash2Icon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import {
  ApiError,
  customFormats,
  quality,
  type CustomFormat,
  type QualityDefinition,
  type QualityProfile,
  type QualityProfileDetail,
  type QualityProfileItem,
  type QualityProfileRequest,
  type ProfileKind,
} from '@/api/client'
import { Alert, AlertDescription } from '@/components/ui/alert'
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'

/** Quality profiles and definitions (blueprint §3.6): ranks live inside a profile, sizes on the definitions. */
export function QualitySettingsPage() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const profiles = useQuery({ queryKey: ['qualityProfiles'], queryFn: quality.profiles })
  const definitions = useQuery({ queryKey: ['qualityDefinitions'], queryFn: quality.definitions })
  const [editing, setEditing] = useState<number | 'new' | null>(null)
  const [pendingRemove, setPendingRemove] = useState<QualityProfile | null>(null)
  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['qualityProfiles'] })

  const remove = useMutation({
    mutationFn: (id: number) => quality.removeProfile(id),
    onSuccess: async () => {
      setPendingRemove(null)
      await invalidate()
    },
    onError: (e) => {
      setPendingRemove(null)
      toast.error(e instanceof ApiError ? e.message : t('quality.saveFailed'))
    },
  })

  return (
    <>
      <h1 className="text-2xl font-semibold tracking-tight">{t('quality.title')}</h1>

      <section className="flex flex-col gap-3">
        <div className="flex flex-wrap items-center gap-3">
          <h2 className="text-xl font-semibold tracking-tight">{t('quality.profiles')}</h2>
          <Button type="button" size="sm" className="ml-auto" onClick={() => setEditing('new')} data-testid="add-profile">
            <PlusIcon />
            {t('quality.addProfile')}
          </Button>
        </div>
        <p className="max-w-3xl text-sm text-muted-foreground">{t('quality.profilesHint')}</p>

        <div className="overflow-x-auto rounded-lg border">
          <Table data-testid="profiles-table">
            <TableHeader>
              <TableRow>
                <TableHead>{t('quality.name')}</TableHead>
                <TableHead>{t('quality.kind')}</TableHead>
                <TableHead>{t('quality.cutoff')}</TableHead>
                <TableHead>{t('quality.upgrades')}</TableHead>
                <TableHead>{t('quality.language')}</TableHead>
                <TableHead className="text-right">{t('versions.actions')}</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {profiles.data?.map((p) => (
                <TableRow key={p.id}>
                  <TableCell className="align-top">
                    <span className="flex flex-wrap items-center gap-2 font-medium">
                      {p.name}
                      {p.isSystem && <Badge variant="secondary">{t('quality.system')}</Badge>}
                    </span>
                  </TableCell>
                  <TableCell className="align-top">
                    <Badge variant="outline">{t(`quality.kinds.${p.kind}`)}</Badge>
                  </TableCell>
                  <TableCell className="align-top">{cutoffLabel(p.cutoff, definitions.data)}</TableCell>
                  <TableCell className="align-top">{p.upgradeAllowed ? t('quality.upgradesOn') : t('quality.upgradesOff')}</TableCell>
                  <TableCell className="align-top">{p.preferredLanguage}</TableCell>
                  <TableCell className="align-top">
                    <div className="flex items-center justify-end gap-1">
                      <Tooltip>
                        <TooltipTrigger asChild>
                          <Button type="button" variant="ghost" size="icon" onClick={() => setEditing(p.id)} aria-label={t('quality.editProfile', { name: p.name })}>
                            <PencilIcon />
                          </Button>
                        </TooltipTrigger>
                        <TooltipContent>{t('quality.editProfile', { name: p.name })}</TooltipContent>
                      </Tooltip>
                      {!p.isSystem && (
                        <Tooltip>
                          <TooltipTrigger asChild>
                            <Button
                              type="button"
                              variant="ghost"
                              size="icon"
                              className="text-destructive hover:text-destructive"
                              onClick={() => setPendingRemove(p)}
                              aria-label={t('quality.delete')}
                            >
                              <Trash2Icon />
                            </Button>
                          </TooltipTrigger>
                          <TooltipContent>{t('quality.delete')}</TooltipContent>
                        </Tooltip>
                      )}
                    </div>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      </section>

      <DefinitionsSection definitions={definitions.data} />

      {editing !== null && (
        <ProfileDialog key={editing === 'new' ? 'new' : editing} profileId={editing === 'new' ? null : editing} onClose={() => setEditing(null)} onSaved={() => void invalidate()} />
      )}

      <AlertDialog open={pendingRemove !== null} onOpenChange={(open) => !open && setPendingRemove(null)}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>{t('quality.deleteTitle')}</AlertDialogTitle>
            <AlertDialogDescription>{t('quality.confirmDelete', { name: pendingRemove?.name ?? '' })}</AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>{t('common.cancel')}</AlertDialogCancel>
            <AlertDialogAction
              onClick={(event) => {
                event.preventDefault()
                if (pendingRemove) remove.mutate(pendingRemove.id)
              }}
              disabled={remove.isPending}
            >
              {t('quality.delete')}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </>
  )
}

function cutoffLabel(cutoff: number, definitions: QualityDefinition[] | undefined): string {
  if (cutoff >= 1000) return `group ${cutoff}`
  const definition = definitions?.find((d) => d.id === cutoff)
  return definition ? (definition.title ?? definition.name) : String(cutoff)
}

function itemLabel(item: QualityProfileItem, definitions: QualityDefinition[] | undefined): string {
  if (item.groupName) return item.groupName
  const definition = definitions?.find((d) => d.id === item.qualityIds[0])
  return definition ? (definition.title ?? definition.name) : String(item.qualityIds[0])
}

function itemKey(item: QualityProfileItem): number {
  return item.groupId ?? item.qualityIds[0]
}

function ProfileDialog({ profileId, onClose, onSaved }: { profileId: number | null; onClose: () => void; onSaved: () => void }) {
  const { t } = useTranslation()
  const source = useQuery({
    queryKey: profileId === null ? ['qualityProfiles', 'schema'] : ['qualityProfiles', profileId],
    queryFn: () => (profileId === null ? quality.schema() : quality.profile(profileId)),
  })
  const definitions = useQuery({ queryKey: ['qualityDefinitions'], queryFn: quality.definitions })
  const formats = useQuery({ queryKey: ['customFormats'], queryFn: customFormats.list })
  const [form, setForm] = useState<QualityProfileRequest | null>(null)

  useEffect(() => {
    if (source.data && form === null) setForm(toRequest(source.data))
  }, [source.data, form])

  const save = useMutation({
    mutationFn: (body: QualityProfileRequest) => (profileId === null ? quality.addProfile(body) : quality.updateProfile(profileId, body)),
    onSuccess: () => {
      onSaved()
      onClose()
    },
  })

  const submit = (e: FormEvent) => {
    e.preventDefault()
    if (form) save.mutate(form)
  }

  const set = <K extends keyof QualityProfileRequest>(key: K, value: QualityProfileRequest[K]) => setForm((f) => (f ? { ...f, [key]: value } : f))
  const number = (value: string, fallback: number) => (value === '' ? fallback : Number(value))

  const allowedItems = form?.items.filter((i) => i.allowed) ?? []
  const cutoffValid = allowedItems.some((i) => itemKey(i) === form?.cutoff)

  const move = (index: number, delta: number) => {
    if (!form) return
    const target = index + delta
    if (target < 0 || target >= form.items.length) return
    const items = [...form.items]
    const [item] = items.splice(index, 1)
    items.splice(target, 0, item)
    set('items', items)
  }

  const setAllowed = (index: number, allowed: boolean) => {
    if (!form) return
    const items = form.items.map((item, i) => (i === index ? { ...item, allowed } : item))
    let cutoff = form.cutoff
    if (!items.filter((i) => i.allowed).some((i) => itemKey(i) === cutoff)) {
      // Cutoff falls back to the best allowed item, which is the last one.
      const best = [...items].reverse().find((i) => i.allowed)
      cutoff = best ? itemKey(best) : 0
    }
    setForm({ ...form, items, cutoff })
  }

  const setScore = (customFormatId: number, score: number) => {
    if (!form) return
    const others = form.formatScores.filter((s) => s.customFormatId !== customFormatId)
    set('formatScores', score === 0 ? others : [...others, { customFormatId, score }])
  }

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="max-h-[90vh] overflow-y-auto sm:max-w-3xl">
        <form onSubmit={submit} data-testid="profile-form">
          <DialogHeader>
            <DialogTitle>{profileId === null ? t('quality.newProfile') : t('quality.editProfile', { name: source.data?.name ?? '' })}</DialogTitle>
          </DialogHeader>

          {!form && (
            <div className="my-6 flex flex-col gap-2">
              <Skeleton className="h-8 w-full" />
              <Skeleton className="h-8 w-2/3" />
            </div>
          )}

          {form && (
            <div className="my-6 grid gap-4 sm:grid-cols-2">
              <div className="grid grid-cols-1 gap-2">
                <Label htmlFor="profile-name">{t('quality.name')}</Label>
                <Input id="profile-name" value={form.name} onChange={(e) => set('name', e.target.value)} autoFocus />
              </div>
              <div className="grid grid-cols-1 gap-2">
                <Label htmlFor="profile-kind">{t('quality.kind')}</Label>
                <Select value={form.kind} onValueChange={(value) => set('kind', value as ProfileKind)}>
                  <SelectTrigger id="profile-kind" className="w-full" data-testid="profile-kind">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {(['movies', 'series', 'both'] as const).map((kind) => (
                      <SelectItem key={kind} value={kind}>
                        {t(`quality.kinds.${kind}`)}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
                <p className="text-xs text-muted-foreground">
                  {source.data && source.data.id > 0 ? t('quality.kindInUse', { films: source.data.versionCount, series: source.data.seriesVersionCount }) : t('quality.kindHint')}
                </p>
              </div>
              <div className="grid grid-cols-1 gap-2">
                <Label htmlFor="profile-language">{t('quality.language')}</Label>
                <Input id="profile-language" value={form.preferredLanguage} onChange={(e) => set('preferredLanguage', e.target.value)} placeholder="en" maxLength={8} />
                <p className="text-xs text-muted-foreground">{t('quality.languageHint')}</p>
              </div>

              <div className="grid grid-cols-1 gap-2 sm:col-span-2">
                <Label>{t('quality.qualities')}</Label>
                <p className="text-xs text-muted-foreground">{t('quality.qualitiesHint')}</p>
                <ul className="divide-y rounded-md border" data-testid="profile-items">
                  {[...form.items].reverse().map((item, reversedIndex) => {
                    const index = form.items.length - 1 - reversedIndex
                    const label = itemLabel(item, definitions.data)
                    return (
                      <li key={itemKey(item)} className="flex items-center gap-3 px-3 py-1.5">
                        <Checkbox id={`item-${itemKey(item)}`} checked={item.allowed} onCheckedChange={(v) => setAllowed(index, v === true)} />
                        <Label htmlFor={`item-${itemKey(item)}`} className="flex-1 font-normal">
                          {label}
                          {item.groupName && (
                            <span className="ml-2 text-xs text-muted-foreground">
                              {item.qualityIds.map((id) => definitions.data?.find((d) => d.id === id)?.name ?? id).join(' · ')}
                            </span>
                          )}
                        </Label>
                        <Button type="button" variant="ghost" size="icon-xs" onClick={() => move(index, 1)} disabled={index === form.items.length - 1} aria-label={t('quality.moveUp')}>
                          <ArrowUpIcon />
                        </Button>
                        <Button type="button" variant="ghost" size="icon-xs" onClick={() => move(index, -1)} disabled={index === 0} aria-label={t('quality.moveDown')}>
                          <ArrowDownIcon />
                        </Button>
                      </li>
                    )
                  })}
                </ul>
              </div>

              <div className="grid grid-cols-1 gap-2">
                <Label htmlFor="profile-cutoff">{t('quality.cutoff')}</Label>
                <Select value={cutoffValid ? String(form.cutoff) : ''} onValueChange={(value) => set('cutoff', Number(value))}>
                  <SelectTrigger id="profile-cutoff" className="w-full">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {[...allowedItems].reverse().map((item) => (
                      <SelectItem key={itemKey(item)} value={String(itemKey(item))}>
                        {itemLabel(item, definitions.data)}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
                <p className="text-xs text-muted-foreground">{t('quality.cutoffHint')}</p>
              </div>

              <div className="flex flex-col gap-3">
                <div className="flex items-center gap-2 pt-6">
                  <Checkbox id="profile-upgrade" checked={form.upgradeAllowed} onCheckedChange={(v) => set('upgradeAllowed', v === true)} />
                  <Label htmlFor="profile-upgrade" className="font-normal">
                    {t('quality.upgradeAllowed')}
                  </Label>
                </div>
              </div>

              <div className="grid grid-cols-1 gap-2">
                <Label htmlFor="profile-min-score">{t('quality.minFormatScore')}</Label>
                <Input id="profile-min-score" type="number" value={form.minFormatScore} onChange={(e) => set('minFormatScore', number(e.target.value, 0))} />
                <p className="text-xs text-muted-foreground">{t('quality.minFormatScoreHint')}</p>
              </div>
              <div className="grid grid-cols-1 gap-2">
                <Label htmlFor="profile-cutoff-score">{t('quality.cutoffFormatScore')}</Label>
                <Input id="profile-cutoff-score" type="number" value={form.cutoffFormatScore} onChange={(e) => set('cutoffFormatScore', number(e.target.value, 0))} />
                <p className="text-xs text-muted-foreground">{t('quality.cutoffFormatScoreHint')}</p>
              </div>
              <div className="grid grid-cols-1 gap-2">
                <Label htmlFor="profile-upgrade-score">{t('quality.minUpgradeFormatScore')}</Label>
                <Input id="profile-upgrade-score" type="number" min={0} value={form.minUpgradeFormatScore} onChange={(e) => set('minUpgradeFormatScore', number(e.target.value, 0))} />
                <p className="text-xs text-muted-foreground">{t('quality.minUpgradeFormatScoreHint')}</p>
              </div>

              <div className="grid grid-cols-1 gap-2 sm:col-span-2">
                <Label>{t('quality.formatScores')}</Label>
                <p className="text-xs text-muted-foreground">{t('quality.formatScoresHint')}</p>
                {formats.data?.length === 0 && <p className="text-sm text-muted-foreground">{t('quality.noFormats')}</p>}
                {formats.data && formats.data.length > 0 && (
                  <ul className="divide-y rounded-md border" data-testid="profile-format-scores">
                    {formats.data.map((f) => (
                      <FormatScoreRow key={f.id} format={f} value={form.formatScores.find((s) => s.customFormatId === f.id)?.score ?? 0} onChange={(v) => setScore(f.id, v)} />
                    ))}
                  </ul>
                )}
              </div>
            </div>
          )}

          {save.error && (
            <Alert variant="destructive" className="mb-4">
              <AlertDescription>{save.error instanceof ApiError ? save.error.message : t('common.unexpectedError')}</AlertDescription>
            </Alert>
          )}

          <DialogFooter>
            <Button type="button" variant="ghost" onClick={onClose}>
              {t('common.cancel')}
            </Button>
            <Button type="submit" disabled={!form || save.isPending || !form.name.trim() || !cutoffValid} data-testid="profile-save">
              {t('quality.save')}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}

function FormatScoreRow({ format, value, onChange }: { format: CustomFormat; value: number; onChange: (value: number) => void }) {
  const { t } = useTranslation()
  const guide = format.trashScores?.default
  return (
    <li className="flex items-center gap-3 px-3 py-1.5">
      <span className="flex-1 text-sm">
        {format.name}
        {guide !== undefined && <span className="ml-2 text-xs text-muted-foreground">{t('quality.suggested', { score: guide })}</span>}
      </span>
      <Input type="number" className="w-28" value={value} onChange={(e) => onChange(e.target.value === '' ? 0 : Number(e.target.value))} aria-label={format.name} />
    </li>
  )
}

function toRequest(detail: QualityProfileDetail): QualityProfileRequest {
  return {
    name: detail.name,
    items: detail.items,
    cutoff: detail.cutoff,
    upgradeAllowed: detail.upgradeAllowed,
    minFormatScore: detail.minFormatScore,
    cutoffFormatScore: detail.cutoffFormatScore,
    minUpgradeFormatScore: detail.minUpgradeFormatScore,
    preferredLanguage: detail.preferredLanguage,
    formatScores: detail.formatScores.map((s) => ({ customFormatId: s.customFormatId, score: s.score })),
    kind: detail.kind,
  }
}

function DefinitionsSection({ definitions }: { definitions: QualityDefinition[] | undefined }) {
  const { t } = useTranslation()
  const [kind, setKind] = useState<'movies' | 'series'>('movies')
  return (
    <section className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center gap-3">
        <h2 className="text-xl font-semibold tracking-tight">{t('quality.definitions')}</h2>
        <div className="flex rounded-md border p-0.5" role="tablist" aria-label={t('quality.definitions')} data-testid="definitions-kind">
          {(['movies', 'series'] as const).map((value) => (
            <Button key={value} type="button" role="tab" aria-selected={kind === value} variant={kind === value ? 'secondary' : 'ghost'} size="sm" onClick={() => setKind(value)}>
              {value === 'movies' ? t('quality.sizesFilms') : t('quality.sizesSeries')}
            </Button>
          ))}
        </div>
      </div>
      <p className="max-w-3xl text-sm text-muted-foreground">{kind === 'movies' ? t('quality.definitionsHint') : t('quality.seriesDefinitionsHint')}</p>
      <div className="overflow-x-auto rounded-lg border">
        <Table data-testid="definitions-table">
          <TableHeader>
            <TableRow>
              <TableHead>{t('quality.quality')}</TableHead>
              <TableHead>{t('quality.definitionTitle')}</TableHead>
              <TableHead>{t('quality.minSize')}</TableHead>
              <TableHead>{t('quality.preferredSize')}</TableHead>
              <TableHead>{t('quality.maxSize')}</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>{definitions?.map((d) => <DefinitionRow key={`${kind}-${d.id}`} definition={d} kind={kind} />)}</TableBody>
        </Table>
      </div>
    </section>
  )
}

function DefinitionRow({ definition, kind }: { definition: QualityDefinition; kind: 'movies' | 'series' }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const sizes =
    kind === 'movies'
      ? { min: definition.minSize, preferred: definition.preferredSize, max: definition.maxSize }
      : { min: definition.seriesMinSize, preferred: definition.seriesPreferredSize, max: definition.seriesMaxSize }
  const [title, setTitle] = useState(definition.title ?? '')
  const [min, setMin] = useState(sizes.min?.toString() ?? '')
  const [preferred, setPreferred] = useState(sizes.preferred?.toString() ?? '')
  const [max, setMax] = useState(sizes.max?.toString() ?? '')

  const save = useMutation({
    mutationFn: () =>
      quality.updateDefinition(definition.id, {
        title: title.trim() || null,
        minSize: min === '' ? null : Number(min),
        preferredSize: preferred === '' ? null : Number(preferred),
        maxSize: max === '' ? null : Number(max),
        kind,
      }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['qualityDefinitions'] }),
    onError: (e) => toast.error(e instanceof ApiError ? e.message : t('quality.saveFailed')),
  })

  const changed =
    title.trim() !== (definition.title ?? '') ||
    (min === '' ? null : Number(min)) !== sizes.min ||
    (preferred === '' ? null : Number(preferred)) !== sizes.preferred ||
    (max === '' ? null : Number(max)) !== sizes.max
  const commit = () => {
    if (changed && !save.isPending) save.mutate()
  }

  return (
    <TableRow>
      <TableCell className="whitespace-nowrap font-medium">{definition.name}</TableCell>
      <TableCell>
        <Input className="h-8 min-w-32" value={title} onChange={(e) => setTitle(e.target.value)} onBlur={commit} aria-label={`${definition.name} title`} />
      </TableCell>
      <TableCell>
        <Input className="h-8 w-24" type="number" min={0} step={0.1} value={min} onChange={(e) => setMin(e.target.value)} onBlur={commit} aria-label={`${definition.name} min`} />
      </TableCell>
      <TableCell>
        <Input className="h-8 w-24" type="number" min={0} step={0.1} value={preferred} onChange={(e) => setPreferred(e.target.value)} onBlur={commit} aria-label={`${definition.name} preferred`} />
      </TableCell>
      <TableCell>
        <Input className="h-8 w-24" type="number" min={0} step={0.1} value={max} onChange={(e) => setMax(e.target.value)} onBlur={commit} aria-label={`${definition.name} max`} />
      </TableCell>
    </TableRow>
  )
}
