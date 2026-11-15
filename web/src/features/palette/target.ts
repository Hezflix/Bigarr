import { useNavigate } from '@tanstack/react-router'
import { useCallback } from 'react'
import type { NavPath } from '@/components/nav'

/** Where a palette entry sends. */
export type PaletteTarget =
  | { kind: 'page'; to: NavPath }
  | { kind: 'movie'; id: number }
  | { kind: 'series'; id: number }
  | { kind: 'add'; media: 'movie' | 'series'; term: string }

export function usePaletteGo() {
  const navigate = useNavigate()
  return useCallback(
    (target: PaletteTarget) => {
      switch (target.kind) {
        case 'page':
          void navigate({ to: target.to })
          return
        case 'movie':
          void navigate({ to: '/movies/$movieId', params: { movieId: String(target.id) } })
          return
        case 'series':
          void navigate({ to: '/series/$seriesId', params: { seriesId: String(target.id) } })
          return
        case 'add':
          void navigate({ to: target.media === 'movie' ? '/add' : '/series/add', search: { term: target.term } })
      }
    },
    [navigate],
  )
}
