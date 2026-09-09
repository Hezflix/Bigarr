import { MonitorIcon, MoonIcon, SunIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Button } from '@/components/ui/button'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { themeModes, useTheme, type ThemeMode } from '@/components/theme'

const icons: Record<ThemeMode, typeof SunIcon> = {
  light: SunIcon,
  dark: MoonIcon,
  system: MonitorIcon,
}

export function ThemeToggle() {
  const { t } = useTranslation()
  const { theme, resolvedTheme, setTheme } = useTheme()
  const TriggerIcon = resolvedTheme === 'dark' ? MoonIcon : SunIcon

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="ghost" size="icon-sm" aria-label={t('theme.label')} data-testid="theme-toggle">
          <TriggerIcon />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" side="top">
        {themeModes.map((mode) => {
          const Icon = icons[mode]
          return (
            <DropdownMenuItem key={mode} onSelect={() => setTheme(mode)} data-active={mode === theme}>
              <Icon />
              {t(`theme.${mode}`)}
            </DropdownMenuItem>
          )
        })}
      </DropdownMenuContent>
    </DropdownMenu>
  )
}
