import { useState } from 'react'
import { cn } from 'cn'
import { userInitial } from '@/components/userInitial'

/**
 * The one user avatar: the picture when there is one and it loads, the name's initial otherwise. Pictures come from
 * another site (the media server's account), so they are fetched without a referrer. `className` sets another size.
 */
export function UserAvatar({ name, url, className }: { name: string; url?: string | null; className?: string }) {
  const [broken, setBroken] = useState(false)
  if (url && !broken) return <img src={url} alt="" aria-hidden="true" referrerPolicy="no-referrer" className={cn('size-7 shrink-0 rounded-full object-cover', className)} onError={() => setBroken(true)} />
  return (
    <span aria-hidden="true" className={cn('flex size-7 shrink-0 items-center justify-center rounded-full bg-muted text-xs font-semibold text-muted-foreground uppercase', className)}>
      {userInitial(name)}
    </span>
  )
}
