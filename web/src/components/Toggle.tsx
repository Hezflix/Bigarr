import { Checkbox } from '@/components/ui/checkbox'
import { Label } from '@/components/ui/label'

/** A checkbox with its label beside it and, when there is one, a line of help under the label. */
export function Toggle({ id, label, checked, onChange, disabled, hint }: { id: string; label: string; checked: boolean; onChange: (value: boolean) => void; disabled?: boolean; hint?: string }) {
  return (
    <div className="flex items-start gap-2">
      <Checkbox id={id} className="mt-0.5" checked={checked} disabled={disabled} onCheckedChange={(next) => onChange(next === true)} />
      <div className="grid gap-0.5">
        <Label htmlFor={id} className="font-normal">
          {label}
        </Label>
        {hint && <p className="text-xs text-muted-foreground">{hint}</p>}
      </div>
    </div>
  )
}
