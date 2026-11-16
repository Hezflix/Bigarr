import {
  BanIcon,
  CalendarDaysIcon,
  ChartColumnIcon,
  CirclePlayIcon,
  CompassIcon,
  FilmIcon,
  FlagIcon,
  FolderCogIcon,
  FolderOpenIcon,
  GitCompareArrowsIcon,
  GitCompareIcon,
  HandIcon,
  HardDriveDownloadIcon,
  HeartPulseIcon,
  HistoryIcon,
  ImportIcon,
  InboxIcon,
  ListOrderedIcon,
  ListTodoIcon,
  ListVideoIcon,
  MonitorPlayIcon,
  PlugIcon,
  RadioTowerIcon,
  RssIcon,
  ShieldCheckIcon,
  SlidersHorizontalIcon,
  TagsIcon,
  TvIcon,
  UsersIcon,
  WebhookIcon,
} from 'lucide-react'
import { useMemo } from 'react'
import { useTranslation } from 'react-i18next'

/** Every page the sidebar links to. */
export type NavPath =
  | '/'
  | '/discover'
  | '/requests'
  | '/issues'
  | '/settings/discover'
  | '/wanted'
  | '/series'
  | '/series/wanted'
  | '/library/specials-films'
  | '/calendar'
  | '/activity/held'
  | '/activity/queue'
  | '/activity/releases'
  | '/activity/history'
  | '/activity/blocklist'
  | '/activity/compare'
  | '/activity/compare-sonarr'
  | '/playback/now'
  | '/playback/plays'
  | '/playback/stats'
  | '/playback/users'
  | '/settings/library'
  | '/settings/mediamanagement'
  | '/settings/indexers'
  | '/settings/downloadclients'
  | '/settings/notifications'
  | '/settings/quality'
  | '/settings/customformats'
  | '/settings/connect'
  | '/settings/security'
  | '/settings/users'
  | '/settings/mediaservers'
  | '/settings/import-radarr'
  | '/settings/import-tautulli'
  | '/settings/followed-radarrs'
  | '/settings/import-sonarr'
  | '/settings/followed-sonarrs'
  | '/system/status'

export type NavItem = {
  to: NavPath
  label: string
  icon: React.ComponentType<{ className?: string }>
  exact?: boolean
  /** The health badge sits on this entry. */
  health?: boolean
  /** The count of pending requests sits on this entry. */
  pendingRequests?: boolean
  /** The count of open issues sits on this entry. */
  openIssues?: boolean
  /** Reached from the palette only: a page linked from elsewhere (the library) that the sidebar does not list. */
  paletteOnly?: boolean
}

export type NavSection = { title: string; items: NavItem[] }

/**
 * Adding a page = one entry here. Sections keep the sidebar readable as slices land. The sidebar and the command
 * palette's "Go to" groups both read this list, so a new page shows up in both.
 */
export function useNavSections(): NavSection[] {
  const { t } = useTranslation()
  return useMemo(() => [
    {
      title: t('nav.requests'),
      items: [
        { to: '/discover', label: t('nav.discover'), icon: CompassIcon },
        { to: '/requests', label: t('nav.requests'), icon: InboxIcon, pendingRequests: true },
        { to: '/issues', label: t('nav.issues'), icon: FlagIcon, openIssues: true },
      ],
    },
    {
      title: t('nav.movies'),
      items: [
        { to: '/', label: t('nav.library'), icon: FilmIcon, exact: true },
        { to: '/wanted', label: t('nav.wanted'), icon: ListTodoIcon },
      ],
    },
    {
      title: t('nav.series'),
      items: [
        { to: '/series', label: t('nav.seriesLibrary'), icon: TvIcon, exact: true },
        { to: '/series/wanted', label: t('nav.seriesWanted'), icon: ListTodoIcon },
        { to: '/library/specials-films', label: t('specialsFilms.title'), icon: FilmIcon, paletteOnly: true },
      ],
    },
    {
      title: t('nav.activity'),
      items: [
        { to: '/calendar', label: t('nav.calendar'), icon: CalendarDaysIcon },
        { to: '/activity/queue', label: t('nav.queue'), icon: ListOrderedIcon },
        { to: '/activity/releases', label: t('nav.releases'), icon: RssIcon },
        { to: '/activity/history', label: t('nav.history'), icon: HistoryIcon },
        { to: '/activity/blocklist', label: t('nav.blocklist'), icon: BanIcon },
        { to: '/activity/held', label: t('nav.held'), icon: HandIcon },
        { to: '/activity/compare', label: t('nav.compare'), icon: GitCompareIcon },
        { to: '/activity/compare-sonarr', label: t('nav.compareSonarr'), icon: GitCompareIcon },
      ],
    },
    {
      title: t('nav.playback'),
      items: [
        { to: '/playback/now', label: t('nav.nowPlaying'), icon: CirclePlayIcon },
        { to: '/playback/plays', label: t('nav.plays'), icon: ListVideoIcon },
        { to: '/playback/stats', label: t('nav.playbackStats'), icon: ChartColumnIcon },
        { to: '/playback/users', label: t('nav.playbackUsers'), icon: UsersIcon },
      ],
    },
    {
      title: t('nav.settings'),
      items: [
        { to: '/settings/library', label: t('nav.settingsLibrary'), icon: FolderOpenIcon },
        { to: '/settings/mediamanagement', label: t('nav.settingsMediaManagement'), icon: FolderCogIcon },
        { to: '/settings/indexers', label: t('nav.settingsIndexers'), icon: RadioTowerIcon },
        { to: '/settings/downloadclients', label: t('nav.settingsDownloadClients'), icon: HardDriveDownloadIcon },
        { to: '/settings/notifications', label: t('nav.settingsNotifications'), icon: WebhookIcon },
        { to: '/settings/quality', label: t('nav.settingsQuality'), icon: SlidersHorizontalIcon },
        { to: '/settings/customformats', label: t('nav.settingsCustomFormats'), icon: TagsIcon },
        { to: '/settings/mediaservers', label: t('nav.settingsMediaServers'), icon: MonitorPlayIcon },
        { to: '/settings/connect', label: t('nav.settingsConnect'), icon: PlugIcon },
        { to: '/settings/users', label: t('nav.settingsUsers'), icon: UsersIcon },
        { to: '/settings/discover', label: t('nav.settingsDiscover'), icon: CompassIcon },
        { to: '/settings/security', label: t('nav.settingsSecurity'), icon: ShieldCheckIcon },
        { to: '/settings/import-radarr', label: t('nav.settingsImportRadarr'), icon: ImportIcon },
        { to: '/settings/import-tautulli', label: t('nav.settingsImportTautulli'), icon: HistoryIcon },
        { to: '/settings/followed-radarrs', label: t('nav.settingsFollowedRadarrs'), icon: GitCompareArrowsIcon },
        { to: '/settings/import-sonarr', label: t('nav.settingsImportSonarr'), icon: ImportIcon },
        { to: '/settings/followed-sonarrs', label: t('nav.settingsFollowedSonarrs'), icon: GitCompareArrowsIcon },
      ],
    },
    {
      title: t('nav.system'),
      items: [{ to: '/system/status', label: t('nav.systemStatus'), icon: HeartPulseIcon, health: true }],
    },
  ], [t])
}
