import { cn } from 'cn'

/** A key cap, as in the palette's footer and on its trigger. */
export function Kbd({ className, ...props }: React.ComponentProps<'kbd'>) {
  return (
    <kbd
      className={cn(
        'pointer-events-none inline-flex h-5 min-w-5 select-none items-center justify-center rounded border bg-muted px-1 font-sans text-[0.6875rem] font-medium text-muted-foreground',
        className,
      )}
      {...props}
    />
  )
}
