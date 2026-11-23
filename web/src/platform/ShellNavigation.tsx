import { Menu, type MenuProps } from 'antd'
import { useState } from 'react'
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

/** A titled group of entries in the sidebar. */
export interface ShellNavigationGroup {
  key: string
  label: string
  entries: readonly ShellNavigationEntry[]
}

/** An entry whose entries sit under it, drawn as an inline submenu, see docs/reference/shell.md#layout. */
export interface ShellNavigationParent {
  key: string
  label: string
  /** An Axis icon name, see navigationIcons. */
  icon?: string | null
  parent: true
  entries: readonly ShellNavigationEntry[]
}

/**
 * The navigation menu of the shell, for the site's entries and for the entries a page puts in the sidebar. An open
 * entry shows its label alone, and an open group shows its label above its entries. A collapsed entry shows its icon
 * alone, and Ant Design gives it the label as a tooltip. A collapsed group gives way to a thin rule above its entries.
 * The collapsed state is passed on, because a page's entries render through a portal and so do not see the Sider's
 * context. No entry shows a count. A parent entry is an inline submenu that a person opens and folds in place. It
 * opens by itself while it holds the current page, and the person's choice holds until the current page changes. A
 * collapsed parent shows its icon, and Ant Design draws its entries in a menu beside it, see
 * docs/reference/shell.md#layout.
 */
export function ShellNavigation({
  entries,
  activeKey,
  collapsed = false,
}: {
  entries: readonly (ShellNavigationEntry | ShellNavigationGroup | ShellNavigationParent)[]
  activeKey?: string
  collapsed?: boolean
}) {
  const parentOfActive = () =>
    entries.flatMap((entry) =>
      'parent' in entry && entry.entries.some((child) => child.key === activeKey) ? [entry.key] : [],
    )
  const [openKeys, setOpenKeys] = useState(parentOfActive)
  const [openFor, setOpenFor] = useState(activeKey)
  if (openFor !== activeKey) {
    setOpenFor(activeKey)
    setOpenKeys(parentOfActive())
  }
  const item = (entry: ShellNavigationEntry) => {
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
  }
  const items = entries.flatMap((entry): NonNullable<MenuProps['items']> => {
    if (!('entries' in entry)) return [item(entry)]
    if ('parent' in entry) {
      const Icon = navigationIcon(entry.icon)
      return [
        {
          key: entry.key,
          label: entry.label,
          icon: collapsed ? <Icon aria-hidden /> : undefined,
          children: entry.entries.map(item),
        },
      ]
    }
    if (collapsed) return [{ key: `${entry.key}-divider`, type: 'divider' as const }, ...entry.entries.map(item)]
    return [{ key: entry.key, type: 'group' as const, label: entry.label, children: entry.entries.map(item) }]
  })
  return (
    <Menu
      mode="inline"
      inlineCollapsed={collapsed}
      style={{ borderInlineEnd: 'none' }}
      selectedKeys={activeKey ? [activeKey] : []}
      openKeys={collapsed ? undefined : openKeys}
      onOpenChange={collapsed ? undefined : setOpenKeys}
      items={items}
    />
  )
}
