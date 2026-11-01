import { DownOutlined } from '@ant-design/icons'
import { ConfigProvider, Dropdown, Menu, Typography, type MenuProps } from 'antd'
import { createElement, useContext } from 'react'
import { createPortal } from 'react-dom'
import { selectedItemTint } from '../platform/antTheme'
import { navigationIcon } from '../platform/navigationIcons'
import { useShellSidebar } from '../platform/shellSidebar'
import { SiteMark } from '../platform/SiteMark'
import { studioApplicationTitle, type StudioApplication, type StudioResourceItem } from '../platform/studio'
import { useText } from '../platform/texts'
import { useDesignTokens, useThemeMode } from '../platform/themeMode'
import { PlatformContext } from './context'
import { StudioNewApplication } from './StudioNewApplication'
import { groupedKinds, studioKindGroups, studioViewKey, type StudioView } from './studioViews'

/** A thin rule between groups in the collapsed sidebar, in place of their titles. */
function Divider() {
  const { colour } = useDesignTokens()
  return <span aria-hidden style={{ display: 'block', height: 1, background: colour.divider }} />
}

function Icon({ name }: { name: string }) {
  return createElement(navigationIcon(name), { 'aria-hidden': true })
}

/**
 * Proposed: Studio's navigation, in the shell's sidebar. The open application heads it, as a menu that switches to
 * another application or creates one. Under it: Overview and Settings, the application's resources by group with how
 * many each kind has, the policy matrix with Access, and Releases. A kind with no resource is left out.
 */
export function StudioNavigation({
  application,
  applications,
  resources,
  view,
  canCreate,
  onPickApplication,
  onApplicationCreated,
  onView,
}: {
  application: string
  applications: readonly StudioApplication[]
  resources: readonly StudioResourceItem[]
  view: StudioView
  canCreate: boolean
  onPickApplication: (name: string) => void
  onApplicationCreated: (name: string) => void
  onView: (view: StudioView) => void
}) {
  const t = useText()
  const { slot, collapsed } = useShellSidebar()
  const { colour, spacing } = useDesignTokens()
  const { mode } = useThemeMode()
  const locale = useContext(PlatformContext)?.current.locale ?? 'en'
  const current = applications.find((app) => app.name === application)
  const currentTitle = current ? studioApplicationTitle(current, locale) : application

  const counts = new Map<string, number>()
  for (const item of resources) {
    counts.set(item.kind, (counts.get(item.kind) ?? 0) + 1)
  }

  // Proposed: an entry is its text while the sidebar is open and its icon, centred, while it is collapsed, where the
  // shell names it in a tooltip. No entry shows a count.
  const entry = (key: string, icon: string, label: string) => ({
    key,
    icon: collapsed ? <Icon name={icon} /> : undefined,
    label,
  })

  // Proposed: what concerns the whole application comes first: Overview, Releases and Settings.
  const items: NonNullable<MenuProps['items']> = [
    entry('overview', 'dashboard', t('shell.studio.nav.overview')),
    entry('releases', 'clock', t('shell.studio.nav.releases')),
    entry('settings', 'settings', t('shell.studio.nav.settings')),
  ]
  for (const group of studioKindGroups) {
    const children = group.kinds
      .filter((entryKind) => counts.has(entryKind.kind))
      .map((entryKind) => entry(`kind:${entryKind.kind}`, entryKind.icon, t(`shell.studio.kind.${entryKind.kind}`)))
    if (group.key === 'access') {
      children.push(entry('policies', 'list', t('shell.studio.view.policies')))
    }
    if (children.length > 0) {
      // Proposed: collapsed, a group's title gives way to a divider, as a strip of icons has no room for words.
      items.push(
        collapsed
          ? { type: 'group', key: `group:${group.key}`, label: <Divider />, children }
          : { type: 'group', key: `group:${group.key}`, label: t(`shell.studio.nav.${group.key}`), children },
      )
    }
  }
  const other = [...counts.keys()].filter((kind) => kind !== 'application' && !groupedKinds.has(kind)).sort()
  if (other.length > 0) {
    items.push({
      type: 'group',
      key: 'group:other',
      label: collapsed ? <Divider /> : t('shell.studio.nav.other'),
      children: other.map((kind) => entry(`kind:${kind}`, 'document', kind)),
    })
  }

  function pick({ key }: { key: string }) {
    if (key.startsWith('kind:')) {
      onView({ name: 'kind', kind: key.slice('kind:'.length) })
    } else {
      onView({ name: key } as StudioView)
    }
  }

  const switcher = (show: () => void) => (
    <Dropdown
      trigger={['click']}
      menu={{
        items: [
          // Proposed: the open application is marked as the language and theme menu marks its choices: primaryText on
          // the selected tint, which stays readable in dark mode.
          ...applications.map((app) => ({
            key: app.name,
            label: studioApplicationTitle(app, locale),
            style:
              app.name === application ? { color: colour.primaryText, background: selectedItemTint(mode) } : undefined,
            'aria-current': app.name === application ? ('true' as const) : undefined,
          })),
          // Proposed: the menu holds the applications and New application… only. Studio in the app switcher already
          // leads back to the list of applications, so the menu repeats no way back.
          ...(canCreate
            ? [{ type: 'divider' as const }, { key: 'new', label: t('shell.studio.newApplication.button') }]
            : []),
        ],
        onClick: ({ key }) => {
          if (key === 'new') {
            show()
          } else {
            onPickApplication(key)
          }
        },
      }}
    >
      <button
        type="button"
        aria-label={t('shell.studio.application')}
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: spacing.xs,
          width: '100%',
          height: '100%',
          padding: collapsed ? 0 : `0 ${spacing.xs}px`,
          justifyContent: collapsed ? 'center' : 'flex-start',
          border: 'none',
          background: 'transparent',
          color: colour.text,
          cursor: 'pointer',
        }}
      >
        <SiteMark title={currentTitle} size={28} />
        {!collapsed && (
          <>
            <Typography.Text strong ellipsis style={{ flex: 1, textAlign: 'start' }}>
              {currentTitle}
            </Typography.Text>
            <DownOutlined aria-hidden style={{ color: colour.textMuted, fontSize: 12 }} />
          </>
        )}
      </button>
    </Dropdown>
  )

  const navigation = (
    <nav aria-label={t('shell.studio.nav.label')}>
      {/* Proposed: the application's row is 48 px, as the header band beside it, so their rules line up. */}
      <div
        style={{
          height: 48,
          boxSizing: 'border-box',
          padding: `0 ${spacing.xs}px`,
          borderBottom: `1px solid ${colour.borderSubtle}`,
        }}
      >
        <StudioNewApplication onCreated={onApplicationCreated} trigger={switcher} />
      </div>
      {/* Proposed: a workspace's navigation is compact, with 32 px entries. */}
      <ConfigProvider
        theme={{ components: { Menu: { itemHeight: 32, itemMarginBlock: 2, groupTitleLineHeight: 1.4 } } }}
      >
        <Menu
          mode="inline"
          inlineCollapsed={collapsed}
          style={{ borderInlineEnd: 'none' }}
          selectedKeys={[studioViewKey(view)]}
          items={items}
          onClick={pick}
        />
      </ConfigProvider>
    </nav>
  )

  return slot ? createPortal(navigation, slot) : null
}
