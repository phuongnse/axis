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

/** An entry that holds entries, drawn as Ant Design's inline submenu, see docs/reference/shell.md#layout. */
export interface ShellNavigationParent {
  key: string
  label: string
  /** An Axis icon name, see navigationIcons. */
  icon?: string | null
  children: readonly ShellNavigationEntry[]
}

/** A node of the shell's navigation: an entry, a group of entries or a parent entry. */
export type ShellNavigationNode = ShellNavigationEntry | ShellNavigationGroup | ShellNavigationParent

/**
 * The navigation menu of the shell, for the site's entries and for the entries a page puts in the sidebar. An open
 * entry shows its label alone, and an open group shows its label above its entries. A collapsed entry shows its icon
 * alone, and Ant Design gives it the label as a tooltip. A collapsed group gives way to a thin rule above its entries.
 * A parent entry opens by itself while one of its entries is marked. A person's open or fold of it holds until another
 * entry is marked or the parents that hold the marked entry change, and a collapsed parent opens as Ant Design's popup.
 * The collapsed state is passed on, because a page's entries render through a portal and so do not see the Sider's
 * context. No entry shows a count.
 */
export function ShellNavigation({
  entries,
  activeKey,
  collapsed = false,
}: {
  entries: readonly ShellNavigationNode[]
  activeKey?: string
  collapsed?: boolean
}) {
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
  // The parents open for the marked entry. The person's own open or fold is kept while the marked entry and the parents
  // that hold it stay the same. A parent drawn later, such as one that waits for the reader, starts again from them.
  const opened = entries.flatMap((entry) =>
    'children' in entry && entry.children.some((child) => child.key === activeKey) ? [entry.key] : [],
  )
  const [held, setHeld] = useState<{ activeKey?: string; opened: string[]; keys: string[] }>({
    activeKey,
    opened,
    keys: opened,
  })
  if (held.activeKey !== activeKey || JSON.stringify(held.opened) !== JSON.stringify(opened)) {
    setHeld({ activeKey, opened, keys: opened })
  }
  const items = entries.flatMap((entry): NonNullable<MenuProps['items']> => {
    if ('children' in entry) {
      const Icon = navigationIcon(entry.icon)
      return [
        {
          key: entry.key,
          icon: collapsed ? <Icon aria-hidden /> : undefined,
          label: entry.label,
          children: entry.children.map(item),
        },
      ]
    }
    if (!('entries' in entry)) return [item(entry)]
    if (collapsed) return [{ key: `${entry.key}-divider`, type: 'divider' as const }, ...entry.entries.map(item)]
    return [{ key: entry.key, type: 'group' as const, label: entry.label, children: entry.entries.map(item) }]
  })
  return (
    <Menu
      mode="inline"
      inlineCollapsed={collapsed}
      style={{ borderInlineEnd: 'none' }}
      selectedKeys={activeKey ? [activeKey] : []}
      openKeys={collapsed ? undefined : held.keys}
      onOpenChange={collapsed ? undefined : (keys) => setHeld({ activeKey, opened, keys })}
      items={items}
    />
  )
}
