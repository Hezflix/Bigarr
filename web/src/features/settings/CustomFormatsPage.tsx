import { useId, useState, type FormEvent, type KeyboardEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { CheckIcon, CodeIcon, PencilIcon, PlusIcon, SearchIcon, Trash2Icon, UploadIcon, XIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import {
  ApiError,
  customFormats,
  quality,
  type CustomFormat,
  type CustomFormatRequest,
  type CustomFormatSpecification,
  type CustomFormatTest,
  type QualityProfile,
  type SpecificationFieldSchema,
  type SpecificationSchema,
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
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'

const formatStyle = 'border-violet-600/30 bg-violet-500/10 text-violet-700 dark:border-violet-400/30 dark:text-violet-300'
const matchStyle = 'border-emerald-600/30 bg-emerald-500/10 text-emerald-700 dark:border-emerald-400/30 dark:text-emerald-300'
const missStyle = 'border-destructive/30 bg-destructive/10 text-destructive'
const textareaStyle =
  'min-h-56 w-full rounded-md border border-input bg-transparent px-3 py-2 font-mono text-xs shadow-xs outline-none focus-visible:border-ring focus-visible:ring-[3px] focus-visible:ring-ring/50 dark:bg-input/30'

/**
 * Custom formats (blueprint §3.6): conditions built by hand or pasted as JSON, one score per profile set right here,
 * everything evaluated at read time. Guide files import unchanged because the JSON shape is Radarr's.
 */
export function CustomFormatsPage() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const list = useQuery({ queryKey: ['customFormats'], queryFn: customFormats.list })
  const profiles = useQuery({ queryKey: ['qualityProfiles'], queryFn: quality.profiles })
  const [editing, setEditing] = useState<CustomFormat | 'new' | null>(null)
  const [importOpen, setImportOpen] = useState(false)
  const [viewing, setViewing] = useState<CustomFormat | null>(null)
  const [pendingRemove, setPendingRemove] = useState<CustomFormat | null>(null)
  const invalidate = () => {
    void queryClient.invalidateQueries({ queryKey: ['customFormats'] })
    void queryClient.invalidateQueries({ queryKey: ['qualityProfiles'] })
  }

  const remove = useMutation({
    mutationFn: (id: number) => customFormats.remove(id),
    onSuccess: () => {
      setPendingRemove(null)
      invalidate()
    },
    onError: (e) => {
      setPendingRemove(null)
      toast.error(e instanceof ApiError ? e.message : t('customFormats.deleteFailed'))
    },
  })

  const profileList = profiles.data ?? []

  return (
    <>
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="text-2xl font-semibold tracking-tight">{t('customFormats.title')}</h1>
        <div className="ml-auto flex flex-wrap items-center gap-2">
          <Button type="button" variant="outline" size="sm" onClick={() => setImportOpen(true)} data-testid="import-formats">
            <UploadIcon />
            {t('customFormats.import')}
          </Button>
          <Button type="button" size="sm" onClick={() => setEditing('new')} data-testid="new-format">
            <PlusIcon />
            {t('customFormats.new')}
          </Button>
        </div>
      </div>
      <p className="max-w-3xl text-sm text-muted-foreground">{t('customFormats.hint')}</p>

      <TestBox />

      <div className="overflow-x-auto rounded-lg border">
        <Table data-testid="formats-table">
          <TableHeader>
            <TableRow>
              <TableHead>{t('customFormats.name')}</TableHead>
              <TableHead>{t('customFormats.conditions')}</TableHead>
              {profileList.map((p) => (
                <TableHead key={p.id} className="w-36">
                  {p.name}
                </TableHead>
              ))}
              <TableHead className="text-right">{t('versions.actions')}</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {list.data?.map((f) => (
              <TableRow key={f.id} data-testid={`format-${f.id}`}>
                <TableCell className="align-top font-medium">{f.name}</TableCell>
                <TableCell className="align-top">
                  <ConditionsSummary format={f} />
                </TableCell>
                {profileList.map((p) => {
                  const score = f.scores.find((s) => s.qualityProfileId === p.id)?.score ?? 0
                  return (
                    <TableCell key={p.id} className="align-top">
                      <ScoreCell key={`${f.id}-${p.id}-${score}`} format={f} profile={p} profiles={profileList} current={score} onSaved={invalidate} />
                    </TableCell>
                  )
                })}
                <TableCell className="align-top">
                  <div className="flex items-center justify-end gap-1">
                    <Tooltip>
                      <TooltipTrigger asChild>
                        <Button type="button" variant="ghost" size="icon" onClick={() => setEditing(f)} aria-label={t('customFormats.edit')} data-testid={`edit-format-${f.id}`}>
                          <PencilIcon />
                        </Button>
                      </TooltipTrigger>
                      <TooltipContent>{t('customFormats.edit')}</TooltipContent>
                    </Tooltip>
                    <Tooltip>
                      <TooltipTrigger asChild>
                        <Button type="button" variant="ghost" size="icon" onClick={() => setViewing(f)} aria-label={t('customFormats.view')}>
                          <CodeIcon />
                        </Button>
                      </TooltipTrigger>
                      <TooltipContent>{t('customFormats.view')}</TooltipContent>
                    </Tooltip>
                    <Tooltip>
                      <TooltipTrigger asChild>
                        <Button
                          type="button"
                          variant="ghost"
                          size="icon"
                          className="text-destructive hover:text-destructive"
                          onClick={() => setPendingRemove(f)}
                          aria-label={t('customFormats.delete')}
                        >
                          <Trash2Icon />
                        </Button>
                      </TooltipTrigger>
                      <TooltipContent>{t('customFormats.delete')}</TooltipContent>
                    </Tooltip>
                  </div>
                </TableCell>
              </TableRow>
            ))}
            {list.data?.length === 0 && (
              <TableRow>
                <TableCell colSpan={3 + profileList.length} className="text-muted-foreground">
                  {t('customFormats.empty')}
                </TableCell>
              </TableRow>
            )}
          </TableBody>
        </Table>
      </div>

      <p className="text-xs text-muted-foreground">
        {t('customFormats.thresholdsHint')}{' '}
        <Link to="/settings/quality" className="underline underline-offset-4 hover:text-foreground">
          {t('quality.profiles')}
        </Link>
      </p>

      {editing !== null && (
        <FormatDialog key={editing === 'new' ? 'new' : editing.id} format={editing === 'new' ? null : editing} profiles={profileList} onClose={() => setEditing(null)} onSaved={invalidate} />
      )}
      {importOpen && <ImportDialog onClose={() => setImportOpen(false)} onImported={invalidate} />}

      <Dialog open={viewing !== null} onOpenChange={(open) => !open && setViewing(null)}>
        <DialogContent className="max-h-[90vh] overflow-y-auto sm:max-w-3xl">
          <DialogHeader>
            <DialogTitle>{viewing?.name}</DialogTitle>
          </DialogHeader>
          <pre className="max-h-[60vh] overflow-auto rounded-md bg-muted p-3 text-xs">
            {viewing &&
              JSON.stringify(
                { name: viewing.name, includeCustomFormatWhenRenaming: viewing.includeWhenRenaming, trash_id: viewing.trashId ?? undefined, trash_scores: viewing.trashScores ?? undefined, specifications: viewing.specifications },
                null,
                2,
              )}
          </pre>
          <DialogFooter>
            <Button type="button" variant="ghost" onClick={() => setViewing(null)}>
              {t('customFormats.close')}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <AlertDialog open={pendingRemove !== null} onOpenChange={(open) => !open && setPendingRemove(null)}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>{t('customFormats.deleteTitle')}</AlertDialogTitle>
            <AlertDialogDescription>{t('customFormats.confirmDelete', { name: pendingRemove?.name ?? '' })}</AlertDialogDescription>
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
              {t('customFormats.delete')}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </>
  )
}

/** The whole format as it stands, with one profile's score replaced: an inline score cell saves the format entire. */
function toRequest(format: CustomFormat, profiles: QualityProfile[], override?: { qualityProfileId: number; score: number }): CustomFormatRequest {
  return {
    name: format.name,
    includeWhenRenaming: format.includeWhenRenaming,
    specifications: format.specifications,
    scores: profiles.map((p) => ({
      qualityProfileId: p.id,
      score: override && override.qualityProfileId === p.id ? override.score : (format.scores.find((s) => s.qualityProfileId === p.id)?.score ?? 0),
    })),
  }
}

function ConditionsSummary({ format }: { format: CustomFormat }) {
  const { t } = useTranslation()
  const labels = useSpecLabels()
  return (
    <Tooltip>
      <TooltipTrigger asChild>
        <span className="cursor-help underline decoration-dotted underline-offset-4">{t('customFormats.conditions', { count: format.specifications.length })}</span>
      </TooltipTrigger>
      <TooltipContent className="max-w-sm">
        <ul className="flex flex-col gap-0.5">
          {format.specifications.slice(0, 12).map((s, i) => (
            <li key={i}>
              {s.negate ? '¬ ' : ''}
              {s.name}
              {s.required ? ' *' : ''}
              <span className="opacity-70"> · {labels(s.implementation)}</span>
            </li>
          ))}
          {format.specifications.length > 12 && <li>…</li>}
        </ul>
      </TooltipContent>
    </Tooltip>
  )
}

/** One profile's score, saved on blur or Enter. A guide file's suggestion sits underneath until it is applied or matched. */
function ScoreCell({ format, profile, profiles, current, onSaved }: { format: CustomFormat; profile: QualityProfile; profiles: QualityProfile[]; current: number; onSaved: () => void }) {
  const { t } = useTranslation()
  const [value, setValue] = useState(String(current))
  const save = useMutation({
    mutationFn: (score: number) => customFormats.update(format.id, toRequest(format, profiles, { qualityProfileId: profile.id, score })),
    onSuccess: onSaved,
    onError: (e) => {
      toast.error(e instanceof ApiError ? e.message : t('customFormats.scoreFailed'))
      setValue(String(current))
    },
  })

  const commit = () => {
    const next = value.trim() === '' || value.trim() === '-' ? 0 : Number(value)
    if (Number.isNaN(next)) {
      setValue(String(current))
      return
    }
    if (next !== current) save.mutate(next)
  }
  const onKeyDown = (e: KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'Enter') {
      e.preventDefault()
      e.currentTarget.blur()
    }
  }

  const suggested = format.trashScores?.default
  return (
    <div className="flex flex-col items-start gap-1">
      <Input
        type="number"
        className="h-8 w-28"
        value={value}
        onChange={(e) => setValue(e.target.value)}
        onBlur={commit}
        onKeyDown={onKeyDown}
        disabled={save.isPending}
        aria-label={t('customFormats.scoreInput', { format: format.name, profile: profile.name })}
        data-testid={`score-${format.id}-${profile.id}`}
      />
      {suggested !== undefined && suggested !== current && (
        <button
          type="button"
          className="text-xs text-muted-foreground underline decoration-dotted underline-offset-4 hover:text-foreground"
          title={t('customFormats.applySuggested', { score: suggested })}
          onClick={() => {
            setValue(String(suggested))
            save.mutate(suggested)
          }}
        >
          {t('customFormats.suggested', { score: suggested })}
        </button>
      )}
    </div>
  )
}

// ---- editor ----------------------------------------------------------------

type DraftCondition = { key: number; name: string; implementation: string; negate: boolean; required: boolean; fields: Record<string, unknown> }

let nextKey = 1
const newKey = () => nextKey++

function toDraft(spec: CustomFormatSpecification): DraftCondition {
  return { key: newKey(), name: spec.name, implementation: spec.implementation, negate: spec.negate, required: spec.required, fields: { ...spec.fields } }
}

function useSpecLabels(): (implementation: string) => string {
  const { t } = useTranslation()
  const labels = t('customFormats.spec', { returnObjects: true }) as Record<string, string>
  return (implementation) => labels[implementation] ?? implementation
}

/** Reads a format in Radarr's shape; fields as an object (guides) or as an array of {name, value} (Radarr exports). */
function parseFormatJson(text: string, fallbackName: string): { name: string; includeWhenRenaming: boolean; specifications: CustomFormatSpecification[] } {
  const raw: unknown = JSON.parse(text)
  if (!raw || typeof raw !== 'object' || Array.isArray(raw)) throw new Error('expected one object')
  const obj = raw as Record<string, unknown>
  if (!Array.isArray(obj.specifications)) throw new Error('"specifications" must be an array')
  const specifications = obj.specifications.map((s, i): CustomFormatSpecification => {
    if (!s || typeof s !== 'object') throw new Error(`specification ${i + 1} is not an object`)
    const spec = s as Record<string, unknown>
    if (typeof spec.implementation !== 'string') throw new Error(`specification ${i + 1} has no "implementation"`)
    let fields: Record<string, unknown> = {}
    if (Array.isArray(spec.fields)) {
      for (const f of spec.fields) {
        const field = f as Record<string, unknown> | null
        if (field && typeof field.name === 'string') fields[field.name] = field.value
      }
    } else if (spec.fields && typeof spec.fields === 'object') {
      fields = { ...(spec.fields as Record<string, unknown>) }
    }
    return { name: typeof spec.name === 'string' ? spec.name : spec.implementation, implementation: spec.implementation, negate: spec.negate === true, required: spec.required === true, fields }
  })
  return {
    name: typeof obj.name === 'string' ? obj.name : fallbackName,
    includeWhenRenaming: obj.includeCustomFormatWhenRenaming === true || obj.includeWhenRenaming === true,
    specifications,
  }
}

function FormatDialog({ format, profiles, onClose, onSaved }: { format: CustomFormat | null; profiles: QualityProfile[]; onClose: () => void; onSaved: () => void }) {
  const { t } = useTranslation()
  const label = useSpecLabels()
  const schema = useQuery({ queryKey: ['customFormats', 'schema'], queryFn: customFormats.schema, staleTime: Infinity })
  const [name, setName] = useState(format?.name ?? '')
  const [includeWhenRenaming, setIncludeWhenRenaming] = useState(format?.includeWhenRenaming ?? false)
  const [conditions, setConditions] = useState<DraftCondition[]>(() => (format ? format.specifications.map(toDraft) : []))
  const [scores, setScores] = useState<Record<number, string>>(() =>
    Object.fromEntries(profiles.map((p) => [p.id, String(format?.scores.find((s) => s.qualityProfileId === p.id)?.score ?? 0)])),
  )
  const [jsonMode, setJsonMode] = useState(false)
  const [jsonText, setJsonText] = useState('')
  const [jsonError, setJsonError] = useState<string | null>(null)
  const [testTitle, setTestTitle] = useState('')
  const [testLanguage, setTestLanguage] = useState('en')

  const specifications = (): CustomFormatSpecification[] =>
    conditions.map((c) => ({ name: c.name.trim() || label(c.implementation), implementation: c.implementation, negate: c.negate, required: c.required, fields: c.fields }))

  /** The draft as the form or the JSON describes it; throws with a readable message when the JSON is off. */
  const draft = () => (jsonMode ? parseFormatJson(jsonText, name) : { name, includeWhenRenaming, specifications: specifications() })

  const enterJson = () => {
    setJsonText(JSON.stringify({ name, includeCustomFormatWhenRenaming: includeWhenRenaming, specifications: specifications() }, null, 2))
    setJsonError(null)
    setJsonMode(true)
  }
  const leaveJson = () => {
    try {
      const parsed = parseFormatJson(jsonText, name)
      setName(parsed.name)
      setIncludeWhenRenaming(parsed.includeWhenRenaming)
      setConditions(parsed.specifications.map(toDraft))
      setJsonError(null)
      setJsonMode(false)
    } catch (e) {
      setJsonError(e instanceof Error ? e.message : String(e))
    }
  }

  const save = useMutation({
    mutationFn: (body: CustomFormatRequest) => (format ? customFormats.update(format.id, body) : customFormats.add(body)),
    onSuccess: () => {
      onSaved()
      onClose()
    },
  })

  const submit = (e: FormEvent) => {
    e.preventDefault()
    let body: ReturnType<typeof draft>
    try {
      body = draft()
    } catch (err) {
      setJsonError(err instanceof Error ? err.message : String(err))
      return
    }
    save.mutate({
      name: body.name.trim(),
      includeWhenRenaming: body.includeWhenRenaming,
      specifications: body.specifications,
      scores: profiles.map((p) => ({ qualityProfileId: p.id, score: Number(scores[p.id]) || 0 })),
    })
  }

  const test = useMutation({
    mutationFn: () => customFormats.test({ title: testTitle.trim(), specifications: draft().specifications, originalLanguage: testLanguage.trim() || undefined }),
    onError: (e) => toast.error(e instanceof ApiError ? e.message : e instanceof Error ? e.message : t('customFormats.testFailed')),
  })

  const addCondition = () => {
    const implementation = schema.data?.specifications[0]?.implementation ?? 'ReleaseTitleSpecification'
    setConditions([...conditions, { key: newKey(), name: '', implementation, negate: false, required: false, fields: {} }])
  }
  const updateCondition = (key: number, patch: Partial<DraftCondition>) => setConditions(conditions.map((c) => (c.key === key ? { ...c, ...patch } : c)))
  const removeCondition = (key: number) => setConditions(conditions.filter((c) => c.key !== key))

  const canSave = jsonMode ? jsonText.trim().length > 0 : name.trim().length > 0 && conditions.length > 0
  const canTest = testTitle.trim().length > 0 && (jsonMode ? jsonText.trim().length > 0 : conditions.length > 0)

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="max-h-[90vh] overflow-y-auto sm:max-w-3xl">
        <form onSubmit={submit} data-testid="format-form">
          <DialogHeader>
            <DialogTitle>{format ? t('customFormats.editor.editTitle', { name: format.name }) : t('customFormats.editor.newTitle')}</DialogTitle>
          </DialogHeader>

          <div className="my-6 flex flex-col gap-5">
            {jsonMode ? (
              <div className="flex flex-col gap-2">
                <p className="text-xs text-muted-foreground">{t('customFormats.editor.jsonHint')}</p>
                <textarea className={textareaStyle} value={jsonText} onChange={(e) => setJsonText(e.target.value)} spellCheck={false} data-testid="format-json" />
                {jsonError && (
                  <Alert variant="destructive">
                    <AlertDescription>{t('customFormats.editor.jsonInvalid', { message: jsonError })}</AlertDescription>
                  </Alert>
                )}
              </div>
            ) : (
              <>
                <div className="grid gap-4 sm:grid-cols-[1fr_auto]">
                  <div className="grid gap-2">
                    <Label htmlFor="format-name">{t('customFormats.editor.name')}</Label>
                    <Input id="format-name" value={name} onChange={(e) => setName(e.target.value)} autoFocus data-testid="format-name" />
                  </div>
                  <div className="flex items-center gap-2 sm:pt-7">
                    <Checkbox id="format-rename" checked={includeWhenRenaming} onCheckedChange={(v) => setIncludeWhenRenaming(v === true)} />
                    <Label htmlFor="format-rename" className="font-normal">
                      {t('customFormats.editor.includeWhenRenaming')}
                    </Label>
                  </div>
                </div>

                <div className="flex flex-col gap-2">
                  <div className="flex items-center gap-3">
                    <Label>{t('customFormats.editor.conditions')}</Label>
                    <Button type="button" variant="outline" size="xs" className="ml-auto" onClick={addCondition} data-testid="add-condition">
                      <PlusIcon />
                      {t('customFormats.editor.addCondition')}
                    </Button>
                  </div>
                  <p className="text-xs text-muted-foreground">{t('customFormats.editor.conditionsHint')}</p>
                  {conditions.length === 0 && <p className="rounded-md border border-dashed px-3 py-4 text-center text-sm text-muted-foreground">{t('customFormats.editor.noCondition')}</p>}
                  <ul className="flex flex-col gap-2">
                    {conditions.map((c) => (
                      <ConditionRow key={c.key} condition={c} schema={schema.data?.specifications} label={label} onChange={(patch) => updateCondition(c.key, patch)} onRemove={() => removeCondition(c.key)} />
                    ))}
                  </ul>
                </div>
              </>
            )}

            <div className="flex flex-col gap-2">
              <Label>{t('customFormats.editor.scores')}</Label>
              <p className="text-xs text-muted-foreground">{t('customFormats.editor.scoresHint')}</p>
              <div className="grid gap-2 sm:grid-cols-2">
                {profiles.map((p) => (
                  <div key={p.id} className="flex items-center gap-2">
                    <Label htmlFor={`format-score-${p.id}`} className="flex-1 font-normal">
                      {p.name}
                    </Label>
                    <Input
                      id={`format-score-${p.id}`}
                      type="number"
                      className="w-28"
                      value={scores[p.id] ?? '0'}
                      onChange={(e) => setScores({ ...scores, [p.id]: e.target.value })}
                      data-testid={`format-score-${p.id}`}
                    />
                  </div>
                ))}
              </div>
            </div>

            <section className="flex flex-col gap-2 rounded-lg border px-3 py-3" data-testid="format-draft-test">
              <div className="flex flex-wrap items-center gap-2">
                <Label htmlFor="format-test-title" className="basis-full sm:basis-auto">
                  {t('customFormats.editor.test')}
                </Label>
                <Input id="format-test-title" className="min-w-56 flex-1 font-mono text-xs" value={testTitle} onChange={(e) => setTestTitle(e.target.value)} placeholder={t('customFormats.testPlaceholder')} />
                <Tooltip>
                  <TooltipTrigger asChild>
                    <Input className="w-16" value={testLanguage} onChange={(e) => setTestLanguage(e.target.value)} maxLength={8} aria-label={t('customFormats.testLanguage')} />
                  </TooltipTrigger>
                  <TooltipContent>{t('customFormats.testLanguageHint')}</TooltipContent>
                </Tooltip>
                <Button type="button" variant="outline" size="sm" onClick={() => test.mutate()} disabled={test.isPending || !canTest}>
                  <SearchIcon />
                  {t('customFormats.editor.testAction')}
                </Button>
              </div>
              {test.data && <TestResult result={test.data} label={label} />}
            </section>

            {save.error && (
              <Alert variant="destructive">
                <AlertDescription>{save.error instanceof ApiError ? save.error.message : t('common.unexpectedError')}</AlertDescription>
              </Alert>
            )}
          </div>

          <DialogFooter className="gap-2 sm:justify-between">
            <Button type="button" variant="ghost" size="sm" onClick={jsonMode ? leaveJson : enterJson} data-testid="toggle-json">
              <CodeIcon />
              {jsonMode ? t('customFormats.editor.editForm') : t('customFormats.editor.editJson')}
            </Button>
            <div className="flex gap-2">
              <Button type="button" variant="ghost" onClick={onClose}>
                {t('common.cancel')}
              </Button>
              <Button type="submit" disabled={!canSave || save.isPending} data-testid="format-save">
                {save.isPending ? t('customFormats.editor.saving') : t('customFormats.editor.save')}
              </Button>
            </div>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}

function ConditionRow({
  condition,
  schema,
  label,
  onChange,
  onRemove,
}: {
  condition: DraftCondition
  schema: SpecificationSchema[] | undefined
  label: (implementation: string) => string
  onChange: (patch: Partial<DraftCondition>) => void
  onRemove: () => void
}) {
  const { t } = useTranslation()
  const type = schema?.find((s) => s.implementation === condition.implementation)
  const setField = (field: string, value: unknown) => onChange({ fields: { ...condition.fields, [field]: value } })

  return (
    <li className="flex flex-col gap-2 rounded-md border p-3" data-testid="condition">
      <div className="grid gap-2 sm:grid-cols-[minmax(10rem,1fr)_2fr_auto]">
        {/* Fields do not carry over between types: a regex means nothing to a resolution. */}
        <Select value={condition.implementation} onValueChange={(implementation) => onChange({ implementation, fields: {} })}>
          <SelectTrigger className="w-full" aria-label={t('customFormats.editor.type')} data-testid="condition-type">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {(schema ?? []).map((s) => (
              <SelectItem key={s.implementation} value={s.implementation}>
                {label(s.implementation)}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
        <Input value={condition.name} onChange={(e) => onChange({ name: e.target.value })} placeholder={label(condition.implementation)} aria-label={t('customFormats.editor.conditionName')} />
        <Button type="button" variant="ghost" size="icon" onClick={onRemove} aria-label={t('customFormats.editor.removeCondition')}>
          <XIcon />
        </Button>
      </div>
      {type && (
        <div className="grid gap-2 sm:grid-cols-2">
          {type.fields.map((f) => (
            <FieldInput key={f.name} field={f} implementation={condition.implementation} value={condition.fields[f.name]} onChange={(v) => setField(f.name, v)} />
          ))}
        </div>
      )}
      <div className="flex flex-wrap items-center gap-4 text-sm">
        <label className="flex items-center gap-2">
          <Checkbox checked={condition.negate} onCheckedChange={(v) => onChange({ negate: v === true })} />
          {t('customFormats.editor.negate')}
        </label>
        <label className="flex items-center gap-2">
          <Checkbox checked={condition.required} onCheckedChange={(v) => onChange({ required: v === true })} />
          {t('customFormats.editor.required')}
        </label>
      </div>
    </li>
  )
}

function FieldInput({ field, implementation, value, onChange }: { field: SpecificationFieldSchema; implementation: string; value: unknown; onChange: (value: unknown) => void }) {
  const { t } = useTranslation()
  const id = useId()
  const labels = t('customFormats.field', { returnObjects: true }) as Record<string, string>
  const text = value === undefined || value === null ? '' : String(value)

  switch (field.type) {
    case 'regex':
      return (
        <div className="grid gap-1 sm:col-span-2">
          <Label htmlFor={id} className="text-xs text-muted-foreground">
            {labels.regex}
          </Label>
          <Input id={id} className="font-mono text-xs" value={text} onChange={(e) => onChange(e.target.value)} placeholder="\bSPARKS\b" spellCheck={false} data-testid={`field-${field.name}`} />
        </div>
      )
    case 'select':
      return (
        <div className="grid gap-1">
          <Label htmlFor={id} className="text-xs text-muted-foreground">
            {labels[field.name] ?? field.name}
          </Label>
          <Select value={text} onValueChange={(v) => onChange(Number(v))}>
            <SelectTrigger id={id} className="w-full" data-testid={`field-${field.name}`}>
              <SelectValue placeholder="…" />
            </SelectTrigger>
            <SelectContent>
              {field.options?.map((o) => (
                <SelectItem key={o.value} value={String(o.value)}>
                  {o.label}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
      )
    case 'number': {
      const size = implementation === 'SizeSpecification'
      return (
        <div className="grid gap-1">
          <Label htmlFor={id} className="text-xs text-muted-foreground">
            {labels[field.name] ?? field.name}
            {size && ` (${labels.gb})`}
          </Label>
          <Input id={id} type="number" step={size ? 0.1 : 1} value={text} onChange={(e) => onChange(e.target.value === '' ? undefined : Number(e.target.value))} data-testid={`field-${field.name}`} />
        </div>
      )
    }
    case 'boolean':
      return (
        <label className="flex items-center gap-2 self-end pb-2 text-sm">
          <Checkbox checked={value === true || value === 'true'} onCheckedChange={(v) => onChange(v === true)} />
          {labels[field.name] ?? field.name}
        </label>
      )
  }
}

function TestResult({ result, label }: { result: CustomFormatTest; label: (implementation: string) => string }) {
  const { t } = useTranslation()
  return (
    <div className="flex flex-col gap-1 text-sm" data-testid="draft-test-result">
      <span className="text-xs text-muted-foreground">
        {[result.parsedTitle, result.year, result.quality, result.edition, result.releaseGroup, result.languages.join(', ') || null].filter(Boolean).join(' · ')}
      </span>
      <span>
        <Badge variant="outline" className={result.matches ? matchStyle : missStyle}>
          {result.matches ? t('customFormats.editor.matches') : t('customFormats.editor.noMatch')}
        </Badge>
      </span>
      <ul className="flex flex-col gap-0.5">
        {result.specifications.map((s, i) => (
          <li key={i} className="flex items-center gap-2">
            {s.satisfied ? <CheckIcon className="size-3.5 text-emerald-600 dark:text-emerald-400" /> : <XIcon className="size-3.5 text-destructive" />}
            <span>{s.name}</span>
            <span className="text-xs text-muted-foreground">{label(s.implementation)}</span>
          </li>
        ))}
      </ul>
    </div>
  )
}

// ---- import ----------------------------------------------------------------

function ImportDialog({ onClose, onImported }: { onClose: () => void; onImported: () => void }) {
  const { t } = useTranslation()
  const [json, setJson] = useState('')

  const run = useMutation({
    mutationFn: () => customFormats.import(json),
    onSuccess: (result) => {
      toast.success(t('customFormats.imported', { added: result.added.length, updated: result.updated.length }))
      onImported()
      onClose()
    },
  })

  const submit = (e: FormEvent) => {
    e.preventDefault()
    run.mutate()
  }

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="sm:max-w-2xl">
        <form onSubmit={submit} data-testid="import-form">
          <DialogHeader>
            <DialogTitle>{t('customFormats.importTitle')}</DialogTitle>
          </DialogHeader>
          <div className="my-6 flex flex-col gap-4">
            <p className="text-sm text-muted-foreground">{t('customFormats.importHint')}</p>
            <textarea
              className={textareaStyle}
              value={json}
              onChange={(e) => setJson(e.target.value)}
              placeholder='{ "name": "Remux Tier 01", "specifications": [ … ] }'
              spellCheck={false}
              data-testid="import-json"
            />
            {run.error && (
              <Alert variant="destructive">
                <AlertDescription>{run.error instanceof ApiError ? run.error.message : t('customFormats.importFailed')}</AlertDescription>
              </Alert>
            )}
          </div>
          <DialogFooter>
            <Button type="button" variant="ghost" onClick={onClose}>
              {t('common.cancel')}
            </Button>
            <Button type="submit" disabled={run.isPending || !json.trim()} data-testid="import-submit">
              {run.isPending ? t('customFormats.importing') : t('customFormats.importAction')}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}

// ---- test box --------------------------------------------------------------

/** What the saved formats make of a title, and the score every profile would give. */
function TestBox() {
  const { t } = useTranslation()
  const [title, setTitle] = useState('')
  const [language, setLanguage] = useState('en')
  const evaluate = useMutation({
    mutationFn: (value: string) => customFormats.evaluate(value, { originalLanguage: language.trim() || undefined }),
    onError: (e) => toast.error(e instanceof ApiError ? e.message : t('customFormats.testFailed')),
  })

  const submit = (e: FormEvent) => {
    e.preventDefault()
    if (title.trim()) evaluate.mutate(title.trim())
  }

  const result = evaluate.data
  return (
    <section className="flex flex-col gap-2 rounded-lg border px-4 py-3" data-testid="format-test">
      <form className="flex flex-wrap items-center gap-2" onSubmit={submit}>
        <Label htmlFor="format-test-title" className="basis-full sm:basis-auto">
          {t('customFormats.test')}
        </Label>
        <Input id="format-test-title" className="min-w-64 flex-1 font-mono text-xs" value={title} onChange={(e) => setTitle(e.target.value)} placeholder={t('customFormats.testPlaceholder')} />
        <Tooltip>
          <TooltipTrigger asChild>
            <Input className="w-16" value={language} onChange={(e) => setLanguage(e.target.value)} maxLength={8} aria-label={t('customFormats.testLanguage')} />
          </TooltipTrigger>
          <TooltipContent>{t('customFormats.testLanguageHint')}</TooltipContent>
        </Tooltip>
        <Button type="submit" variant="outline" size="sm" disabled={evaluate.isPending || !title.trim()}>
          <SearchIcon />
          {t('customFormats.testAction')}
        </Button>
      </form>
      {result && (
        <div className="flex flex-col gap-1 text-sm">
          <span className="text-xs text-muted-foreground">
            {[result.parsedTitle, result.year, result.quality, result.edition, result.releaseGroup, result.languages.join(', ') || null].filter(Boolean).join(' · ')}
          </span>
          <span className="flex flex-wrap items-center gap-1">
            {result.formats.length === 0 && <span className="text-muted-foreground">{t('customFormats.noneMatched')}</span>}
            {result.formats.map((f) => (
              <Badge key={f.id} variant="outline" className={formatStyle}>
                {f.name}
              </Badge>
            ))}
          </span>
          <span className="flex flex-wrap gap-1">
            {result.scores.map((s) => (
              <Badge key={s.qualityProfileId} variant="secondary">
                {t('customFormats.scoreFor', { profile: s.qualityProfile, score: s.score })}
              </Badge>
            ))}
          </span>
        </div>
      )}
    </section>
  )
}
