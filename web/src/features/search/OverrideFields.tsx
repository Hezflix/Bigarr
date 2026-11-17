import { useQuery } from '@tanstack/react-query'
import { CheckIcon, ChevronsUpDownIcon, XIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { quality } from '@/api/client'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Command, CommandEmpty, CommandInput, CommandItem, CommandList } from '@/components/ui/command'
import { Label } from '@/components/ui/label'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'

/**
 * The quality and the languages of a release as the user says they are, each started from the parse. The parse is shown
 * beside the label, so what changed is plain.
 */
export function OverrideFields({
  parsedQuality,
  parsedLanguages,
  qualityId,
  languages,
  onQuality,
  onLanguages,
  idPrefix,
}: {
  parsedQuality: string
  parsedLanguages: string[]
  qualityId: number
  languages: string[]
  onQuality: (id: number) => void
  onLanguages: (names: string[]) => void
  idPrefix: string
}) {
  const { t } = useTranslation()
  const definitions = useQuery({ queryKey: ['qualityDefinitions'], queryFn: quality.definitions, staleTime: 5 * 60_000 })
  const languageList = useQuery({ queryKey: ['languages'], queryFn: quality.languages, staleTime: Number.POSITIVE_INFINITY })
  // Unknown is the parser's placeholder: it cannot be chosen.
  const qualities = (definitions.data ?? []).filter((q) => q.id !== 0)
  const toggle = (name: string) => onLanguages(languages.includes(name) ? languages.filter((l) => l !== name) : [...languages, name])

  return (
    <div className="grid gap-4 sm:grid-cols-2">
      <div className="grid content-start gap-2">
        <Label htmlFor={`${idPrefix}-quality`}>
          {t('grabOverride.quality')}
          <span className="font-normal text-muted-foreground">{t('grabOverride.parsed', { value: parsedQuality })}</span>
        </Label>
        <Select value={qualityId > 0 ? String(qualityId) : undefined} onValueChange={(v) => onQuality(Number(v))}>
          <SelectTrigger id={`${idPrefix}-quality`} className="w-full" data-testid={`${idPrefix}-quality`}>
            <SelectValue placeholder={t('grabOverride.qualityPlaceholder')} />
          </SelectTrigger>
          <SelectContent className="max-h-72">
            {qualities.map((q) => (
              <SelectItem key={q.id} value={String(q.id)}>
                {q.title || q.name}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>
      <div className="grid content-start gap-2">
        <Label htmlFor={`${idPrefix}-languages`}>
          {t('grabOverride.languages')}
          <span className="font-normal text-muted-foreground">{t('grabOverride.parsed', { value: parsedLanguages.length > 0 ? parsedLanguages.join(', ') : t('grabOverride.none') })}</span>
        </Label>
        <Popover>
          <PopoverTrigger asChild>
            <Button id={`${idPrefix}-languages`} type="button" variant="outline" className="h-auto min-h-9 w-full justify-between font-normal" data-testid={`${idPrefix}-languages`}>
              <span className="flex min-w-0 flex-wrap gap-1">
                {languages.length === 0 ? (
                  <span className="text-muted-foreground">{t('grabOverride.languagesPlaceholder')}</span>
                ) : (
                  languages.map((l) => (
                    <Badge key={l} variant="secondary">
                      {l}
                    </Badge>
                  ))
                )}
              </span>
              <ChevronsUpDownIcon className="opacity-50" />
            </Button>
          </PopoverTrigger>
          <PopoverContent className="w-64 p-0" align="start">
            <Command>
              <CommandInput placeholder={t('grabOverride.findLanguage')} />
              <CommandList className="max-h-64">
                <CommandEmpty>{t('grabOverride.noLanguage')}</CommandEmpty>
                {(languageList.data ?? []).map((l) => (
                  <CommandItem key={l.id} value={l.name} onSelect={() => toggle(l.name)} data-testid={`${idPrefix}-language`}>
                    <CheckIcon className={languages.includes(l.name) ? 'opacity-100' : 'opacity-0'} />
                    {l.name}
                  </CommandItem>
                ))}
              </CommandList>
            </Command>
          </PopoverContent>
        </Popover>
        {languages.length > 0 && (
          <Button type="button" variant="link" size="sm" className="h-auto justify-start p-0 text-xs" onClick={() => onLanguages([])}>
            <XIcon />
            {t('grabOverride.clearLanguages')}
          </Button>
        )}
      </div>
    </div>
  )
}
