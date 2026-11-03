import { Menu } from 'antd'
import { Link } from 'react-router'
import { navigationIcon } from './navigationIcons'

/** One entry of the shell's navigation. The label is the text the person reads. */
export interface ShellNavigationEntry {
  key: string
  path: string
  label: string
  /** An Axis icon name, see navigationIcons. */
  icon?: string | null
}

/**
 * The navigation menu of the shell, for the site's entries and for the entries a page puts in the sidebar. An open
 * entry shows its label alone. A collapsed entry shows its icon alone, and Ant Design gives it the label as a tooltip. The collapsed state is passed on, because a page's entries
 * render through a portal and so do not see the Sider's context.
 * No entry shows a count.
 */
export function ShellNavigation({
  entries,
  activeKey,
  collapsed = false,
}: {
  entries: readonly ShellNavigationEntry[]
  activeKey?: string
  collapsed?: boolean
}) {
  const items = entries.map((entry) => {
    const Icon = navigationIcon(entry.icon)
    return {
      key: entry.key,
      icon: collapsed ? <Icon aria-hidden /> : undefined,
      label: (
        <Link to={entry.path} aria-current={entry.key === activeKey ? 'page' : undefined}>
          {entry.label}
        </Link>
      ),
    }
  })
  return (
    <Menu
      mode="inline"
      inlineCollapsed={collapsed}
      style={{ borderInlineEnd: 'none' }}
      selectedKeys={activeKey ? [activeKey] : []}
      items={items}
    />
  )
}
