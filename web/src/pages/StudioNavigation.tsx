import { DownOutlined } from '@ant-design/icons'
import { ConfigProvider, Dropdown, Flex, Menu, Typography, type MenuProps } from 'antd'
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

  const entry = (key: string, icon: string, label: string, count?: number) => ({
    key,
    icon: <Icon name={icon} />,
    label: (
      <Flex justify="space-between" gap={spacing.xs}>
        <span>{label}</span>
        {count !== undefined && <span style={{ color: colour.textMuted }}>{count}</span>}
      </Flex>
    ),
  })

  const items: NonNullable<MenuProps['items']> = [
    entry('overview', 'dashboard', t('shell.studio.nav.overview')),
    entry('settings', 'settings', t('shell.studio.nav.settings')),
  ]
  for (const group of studioKindGroups) {
    const children = group.kinds
      .filter((entryKind) => counts.has(entryKind.kind))
      .map((entryKind) =>
        entry(
          `kind:${entryKind.kind}`,
          entryKind.icon,
          t(`shell.studio.kind.${entryKind.kind}`),
          counts.get(entryKind.kind),
        ),
      )
    if (group.key === 'access') {
      children.push(entry('policies', 'list', t('shell.studio.view.policies')))
    }
    if (children.length > 0) {
      items.push({ type: 'group', key: `group:${group.key}`, label: t(`shell.studio.nav.${group.key}`), children })
    }
  }
  const other = [...counts.keys()].filter((kind) => kind !== 'application' && !groupedKinds.has(kind)).sort()
  if (other.length > 0) {
    items.push({
      type: 'group',
      key: 'group:other',
      label: t('shell.studio.nav.other'),
      children: other.map((kind) => entry(`kind:${kind}`, 'document', kind, counts.get(kind))),
    })
  }
  items.push({ type: 'divider' }, entry('releases', 'clock', t('shell.studio.nav.releases')))

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
          ...(canCreate
            ? [{ type: 'divider' as const }, { key: 'new', label: t('shell.studio.newApplication.button') }]
            : []),
        ],
        onClick: ({ key }) => (key === 'new' ? show() : onPickApplication(key)),
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
