import { useQuery } from '@tanstack/react-query'
import { CheckIcon, ChevronsUpDownIcon, XIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { quality } from '@/api/client'
import { QualitySelect } from '@/components/QualitySelect'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Command, CommandEmpty, CommandInput, CommandItem, CommandList } from '@/components/ui/command'
import { Label } from '@/components/ui/label'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'

/** The placeholders and the clear button's text, when the fields describe a file on disk rather than a release title. */
export type OverrideTexts = { qualityPlaceholder: string; languagesPlaceholder: string; clearLanguages: string }

/**
 * The quality and the languages of a release as the user says they are, each started from the parse. The parse is shown
 * beside the label, so what changed is plain. Without a parse (a file's edit), no hint is shown and <c>texts</c> words it.
 */
export function OverrideFields({
  parsedQuality,
  parsedLanguages,
  qualityId,
  languages,
  onQuality,
  onLanguages,
  idPrefix,
  texts,
}: {
  parsedQuality?: string
  parsedLanguages?: string[]
  qualityId: number
  languages: string[]
  onQuality: (id: number) => void
  onLanguages: (names: string[]) => void
  idPrefix: string
  texts?: OverrideTexts
}) {
  const { t } = useTranslation()
  const words: OverrideTexts = texts ?? {
    qualityPlaceholder: t('grabOverride.qualityPlaceholder'),
    languagesPlaceholder: t('grabOverride.languagesPlaceholder'),
    clearLanguages: t('grabOverride.clearLanguages'),
  }
  const languageList = useQuery({ queryKey: ['languages'], queryFn: quality.languages, staleTime: Number.POSITIVE_INFINITY })
  const toggle = (name: string) => onLanguages(languages.includes(name) ? languages.filter((l) => l !== name) : [...languages, name])

  return (
    <div className="grid gap-4 sm:grid-cols-2">
      <div className="grid content-start gap-2">
        <Label htmlFor={`${idPrefix}-quality`}>
          {t('grabOverride.quality')}
          {parsedQuality !== undefined && <span className="font-normal text-muted-foreground">{t('grabOverride.parsed', { value: parsedQuality })}</span>}
        </Label>
        <QualitySelect
          id={`${idPrefix}-quality`}
          value={qualityId}
          onChange={onQuality}
          placeholder={words.qualityPlaceholder}
          className="w-full"
          contentClassName="max-h-72"
          testId={`${idPrefix}-quality`}
        />
      </div>
      <div className="grid content-start gap-2">
        <Label htmlFor={`${idPrefix}-languages`}>
          {t('grabOverride.languages')}
          {parsedLanguages !== undefined && (
            <span className="font-normal text-muted-foreground">{t('grabOverride.parsed', { value: parsedLanguages.length > 0 ? parsedLanguages.join(', ') : t('grabOverride.none') })}</span>
          )}
        </Label>
        <Popover>
          <PopoverTrigger asChild>
            <Button id={`${idPrefix}-languages`} type="button" variant="outline" className="h-auto min-h-9 w-full justify-between font-normal" data-testid={`${idPrefix}-languages`}>
              <span className="flex min-w-0 flex-wrap gap-1">
                {languages.length === 0 ? (
                  <span className="text-muted-foreground">{words.languagesPlaceholder}</span>
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
            {words.clearLanguages}
          </Button>
        )}
      </div>
    </div>
  )
}
