import { useEffect, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { ArrowDownIcon, ArrowUpIcon, EyeIcon, PlusIcon, Trash2Icon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { discover, type DiscoverSliderKind, type DiscoverSliderUpdate } from '@/api/client'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Checkbox } from '@/components/ui/checkbox'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { customKinds, sliderTitle } from '@/features/discover/sliders'
import { useDebounced } from '@/hooks/useDebounced'

type Row = DiscoverSliderUpdate & { key: string; builtIn: boolean }

const NONE = '__none__'

/** Settings → Discover: which rows the Discover page shows and in what order, custom rows, and which version is "4K". */
export function DiscoverSettingsPage() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const settings = useQuery({ queryKey: ['discover', 'settings'], queryFn: discover.settings })
  const [rows, setRows] = useState<Row[]>([])
  const [fourK, setFourK] = useState<string>(NONE)

  useEffect(() => {
    if (!settings.data) return
    setRows(settings.data.sliders.map((s) => ({ id: s.id, kind: s.kind, title: s.title, value: s.value, enabled: s.enabled, builtIn: s.builtIn, key: `id-${s.id}` })))
    setFourK(settings.data.fourKVersion ?? NONE)
  }, [settings.data])

  const save = useMutation({
    mutationFn: () => discover.updateSettings(fourK === NONE ? null : fourK, rows.map(({ id, kind, title, value, enabled }) => ({ id, kind, title, value, enabled }))),
    onSuccess: (data) => {
      queryClient.setQueryData(['discover', 'settings'], data)
      void queryClient.invalidateQueries({ queryKey: ['discover'] })
      toast.success(t('discoverSettings.saved'))
    },
    onError: (error) => toast.error(error.message),
  })

  const move = (index: number, by: number) =>
    setRows((current) => {
      const next = [...current]
      const [row] = next.splice(index, 1)
      next.splice(index + by, 0, row)
      return next
    })
  const versionNames = settings.data?.versionNames ?? []
  const fourKChoices = fourK !== NONE && !versionNames.includes(fourK) ? [...versionNames, fourK] : versionNames

  return (
    <>
      <h1 className="text-2xl font-semibold tracking-tight">{t('discoverSettings.title')}</h1>
      <p className="max-w-3xl text-sm text-muted-foreground">{t('discoverSettings.hint')}</p>
      {settings.error && <p className="text-sm text-destructive">{settings.error.message}</p>}

      <Card>
        <CardHeader>
          <CardTitle>{t('discoverSettings.fourKTitle')}</CardTitle>
          <CardDescription>{t('discoverSettings.fourKHint')}</CardDescription>
        </CardHeader>
        <CardContent>
          <Select value={fourK} onValueChange={setFourK}>
            <SelectTrigger className="w-full sm:w-64" aria-label={t('discoverSettings.fourKTitle')} data-testid="fourk-version">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={NONE}>{t('discoverSettings.fourKNone')}</SelectItem>
              {fourKChoices.map((name) => (
                <SelectItem key={name} value={name}>
                  {name}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </CardContent>
      </Card>

      <Card data-testid="discover-rows">
        <CardHeader>
          <CardTitle>{t('discoverSettings.rowsTitle')}</CardTitle>
          <CardDescription>{t('discoverSettings.rowsHint')}</CardDescription>
        </CardHeader>
        <CardContent className="flex flex-col gap-4">
          <ol className="divide-y rounded-lg border">
            {rows.map((row, index) => (
              <li key={row.key} className="flex flex-wrap items-center gap-x-3 gap-y-2 px-3 py-2" data-testid="discover-row">
                <Checkbox
                  id={`row-${row.key}`}
                  checked={row.enabled}
                  onCheckedChange={(checked) => setRows((current) => current.map((r) => (r.key === row.key ? { ...r, enabled: checked === true } : r)))}
                />
                <Label htmlFor={`row-${row.key}`} className="min-w-0 flex-1 basis-48 cursor-pointer flex-col items-start gap-0.5 font-normal">
                  <span className="truncate font-medium">{sliderTitle(t, row.kind, row.title ?? null)}</span>
                  <span className="text-xs text-muted-foreground">
                    {row.builtIn ? t('discoverSettings.builtIn') : `${t(`discoverSettings.kind.${row.kind}`)} · ${row.value}`}
                  </span>
                </Label>
                {!row.builtIn && <Badge variant="outline">{t('discoverSettings.custom')}</Badge>}
                <div className="ml-auto flex items-center gap-0.5">
                  {row.id != null && !['movieGenres', 'seriesGenres', 'studios', 'networks'].includes(row.kind) && (
                    <Button asChild variant="ghost" size="icon-sm" aria-label={t('discoverSettings.preview')} title={t('discoverSettings.preview')}>
                      <Link to="/discover/rows/$sliderId" params={{ sliderId: String(row.id) }}>
                        <EyeIcon />
                      </Link>
                    </Button>
                  )}
                  <Button variant="ghost" size="icon-sm" disabled={index === 0} onClick={() => move(index, -1)} aria-label={t('discoverSettings.moveUp')}>
                    <ArrowUpIcon />
                  </Button>
                  <Button variant="ghost" size="icon-sm" disabled={index === rows.length - 1} onClick={() => move(index, 1)} aria-label={t('discoverSettings.moveDown')}>
                    <ArrowDownIcon />
                  </Button>
                  <Button
                    variant="ghost"
                    size="icon-sm"
                    disabled={row.builtIn}
                    onClick={() => setRows((current) => current.filter((r) => r.key !== row.key))}
                    aria-label={t('discoverSettings.remove')}
                    title={row.builtIn ? t('discoverSettings.builtInKept') : t('discoverSettings.remove')}
                  >
                    <Trash2Icon />
                  </Button>
                </div>
              </li>
            ))}
          </ol>
          <AddRow onAdd={(row) => setRows((current) => [...current, row])} />
          <div className="flex justify-end">
            <Button onClick={() => save.mutate()} disabled={save.isPending || !settings.data} data-testid="save-discover">
              {t('common.save')}
            </Button>
          </div>
        </CardContent>
      </Card>
    </>
  )
}

/** A custom row: its kind, its value (picked from TMDB, or the words searched for) and a title. */
function AddRow({ onAdd }: { onAdd: (row: Row) => void }) {
  const { t } = useTranslation()
  const [kind, setKind] = useState<DiscoverSliderKind>('movieGenre')
  const [title, setTitle] = useState('')
  const [value, setValue] = useState('')
  const [text, setText] = useState('')
  const valueType = customKinds.find((k) => k.kind === kind)?.value ?? 'words'
  const debounced = useDebounced(text.trim(), 350)
  const lookup = useQuery({
    queryKey: ['discover', 'lookup', kind, valueType === 'lookup' ? debounced : ''],
    queryFn: () => discover.lookup(kind, valueType === 'lookup' ? debounced : ''),
    enabled: valueType.startsWith('genre') || (valueType === 'lookup' && debounced.length > 0),
    staleTime: 10 * 60_000,
  })

  const reset = () => {
    setTitle('')
    setValue('')
    setText('')
  }
  const ready = title.trim().length > 0 && value.trim().length > 0

  return (
    <fieldset className="grid gap-3 rounded-lg border border-dashed p-3 sm:grid-cols-[12rem_1fr_1fr_auto] sm:items-end" data-testid="add-row">
      <legend className="px-1 text-sm font-medium">{t('discoverSettings.addTitle')}</legend>
      <div className="grid gap-1.5">
        <Label>{t('discoverSettings.kindLabel')}</Label>
        <Select
          value={kind}
          onValueChange={(next) => {
            setKind(next as DiscoverSliderKind)
            reset()
          }}
        >
          <SelectTrigger aria-label={t('discoverSettings.kindLabel')}>
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {customKinds.map((k) => (
              <SelectItem key={k.kind} value={k.kind}>
                {t(`discoverSettings.kind.${k.kind}`)}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>

      <div className="grid gap-1.5">
        <Label htmlFor="row-value">{t(`discoverSettings.value.${valueType === 'genre-movie' || valueType === 'genre-series' ? 'genre' : valueType}`)}</Label>
        {valueType === 'words' ? (
          <Input
            id="row-value"
            value={value}
            onChange={(e) => {
              setValue(e.target.value)
              if (!title || title === value) setTitle(e.target.value)
            }}
          />
        ) : valueType === 'lookup' ? (
          <div className="relative">
            <Input id="row-value" value={text} placeholder={t(`discoverSettings.lookupPlaceholder.${kind as 'movieKeyword' | 'seriesKeyword' | 'studio' | 'network'}`)} onChange={(e) => { setText(e.target.value); setValue('') }} autoComplete="off" />
            {text && !value && (lookup.data?.length ?? 0) > 0 && (
              <ul className="absolute z-20 mt-1 max-h-56 w-full overflow-auto rounded-md border bg-popover p-1 shadow-md">
                {lookup.data?.map((r) => (
                  <li key={r.id}>
                    <button
                      type="button"
                      className="w-full rounded px-2 py-1.5 text-left text-sm hover:bg-accent"
                      onClick={() => {
                        setValue(String(r.id))
                        setText(`${r.name} (${r.id})`)
                        if (!title) setTitle(r.name)
                      }}
                    >
                      {r.name} <span className="text-xs text-muted-foreground">#{r.id}</span>
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </div>
        ) : (
          <Select
            value={value}
            onValueChange={(id) => {
              setValue(id)
              const name = lookup.data?.find((g) => String(g.id) === id)?.name
              if (name && !title) setTitle(name)
            }}
          >
            <SelectTrigger id="row-value">
              <SelectValue placeholder={t('discoverSettings.pickGenre')} />
            </SelectTrigger>
            <SelectContent>
              {lookup.data?.map((g) => (
                <SelectItem key={g.id} value={String(g.id)}>
                  {g.name}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        )}
      </div>

      <div className="grid gap-1.5">
        <Label htmlFor="row-title">{t('discoverSettings.titleLabel')}</Label>
        <Input id="row-title" value={title} maxLength={100} onChange={(e) => setTitle(e.target.value)} />
      </div>

      <Button
        variant="outline"
        disabled={!ready}
        onClick={() => {
          onAdd({ id: null, kind, title: title.trim(), value: value.trim(), enabled: true, builtIn: false, key: `new-${Date.now()}` })
          reset()
        }}
      >
        <PlusIcon />
        {t('discoverSettings.add')}
      </Button>
    </fieldset>
  )
}
