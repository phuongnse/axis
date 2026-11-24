import { Dropdown } from 'antd'
import { useContext, useState } from 'react'
import { AxisButton } from '../platform/AxisButton'
import { Chevron } from '../platform/Chevron'
import { useShellSidebar } from '../platform/shellSidebarContext'
import { menuOf, type AxisAction } from '../platform/menuActions'
import { ShellNavigation, type ShellNavigationNode } from '../platform/ShellNavigation'
import { SiteMark } from '../platform/SiteMark'
import { type StudioApplication, studioAppTitle } from '../platform/studio'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { PlatformContext } from './context'
import { StudioNewApplication } from './StudioNewApplication'

/** The height of the application row, the `bar` size of the shell's header. */
const rowHeight = 48

/**
 * The sidebar of an open application in Studio. Its row stays at the top: a button with the application's mark and
 * title, which opens a menu of every application with the open one marked. A creator also gets New application… at the
 * end of that menu, which has no All applications entry. The row stays in place with the page's band, and the application's
 * entries sit below it and scroll under it. Collapsed, the row shows the mark alone.
 */
export function StudioSidebar({
  apps,
  application,
  canCreate,
  entries,
  activeKey,
  onPick,
  onCreated,
}: {
  apps: StudioApplication[]
  application: string
  canCreate: boolean
  entries: readonly ShellNavigationNode[]
  activeKey: string
  onPick: (name: string) => void
  onCreated: (name: string) => void
}) {
  const t = useText()
  const locale = useContext(PlatformContext)?.current.locale ?? 'en'
  const { collapsed } = useShellSidebar()
  const { colour, spacing } = useDesignTokens()
  const [creating, setCreating] = useState(false)
  const current = apps.find((app) => app.name === application)
  const title = current ? studioAppTitle(current, locale) : application
  // Each application is a choice, and the open one is marked. New application… is an action after a divider.
  const actions: AxisAction[] = apps.map((app) => ({
    key: `app:${app.name}`,
    label: studioAppTitle(app, locale),
    selected: app.name === application,
    onClick: () => onPick(app.name),
  }))
  if (canCreate) {
    actions.push({ key: 'new', labelKey: 'shell.studio.newApplication.button', onClick: () => setCreating(true) })
  }
  const menu = menuOf(actions, t)

  return (
    <>
      <div
        style={{
          position: 'sticky',
          top: 0,
          zIndex: 1,
          height: rowHeight,
          background: colour.surface,
          borderBottom: `1px solid ${colour.borderSubtle}`,
        }}
      >
        <Dropdown trigger={['click']} menu={menu}>
          <AxisButton
            role="subtle"
            block
            data-testid="studio-application-row"
            aria-label={collapsed ? title : undefined}
            style={{
              height: '100%',
              display: 'flex',
              alignItems: 'center',
              justifyContent: collapsed ? 'center' : 'flex-start',
              gap: spacing.sm,
              minWidth: 0,
            }}
          >
            <SiteMark title={title} />
            {!collapsed && (
              <>
                <span
                  style={{ flex: 1, minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}
                >
                  {title}
                </span>
                <Chevron />
              </>
            )}
          </AxisButton>
        </Dropdown>
      </div>
      {canCreate && <StudioNewApplication open={creating} onOpenChange={setCreating} onCreated={onCreated} />}
      <ShellNavigation entries={entries} activeKey={activeKey} collapsed={collapsed} />
    </>
  )
}
