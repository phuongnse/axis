import { Menu, type MenuProps } from 'antd'
import { useState } from 'react'
import { Link } from 'react-router'
import { FoldIcon } from './FoldIcon'
import { navigationIcon } from './navigationIcons'
import { useDesignTokens } from './themeMode'

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

/**
 * Proposed, see the review on #3263: an entry whose own entries sit under it, such as the views of one list, which it
 * opens and folds. It goes to no address of its own.
 */
export interface ShellNavigationParent {
  key: string
  label: string
  /** An Axis icon name, see navigationIcons. */
  icon?: string | null
  children: readonly ShellNavigationEntry[]
}

/** The width of Ant Design's own submenu arrow, whose room a parent's end keeps. */
const menuArrowSize = 10

export type ShellNavigationNode = ShellNavigationEntry | ShellNavigationGroup | ShellNavigationParent

/**
 * The navigation menu of the shell, for the site's entries and for the entries a page puts in the sidebar. An open
 * entry shows its label alone, and an open group shows its label above its entries. A collapsed entry shows its icon
 * alone, and Ant Design gives it the label as a tooltip. A collapsed group gives way to a thin rule above its entries.
 * The collapsed state is passed on, because a page's entries render through a portal and so do not see the Sider's
 * context. No entry shows a count. Proposed: a parent shows its label with a caret, and a click opens or folds its
 * entries, indented one step under it; it opens by itself while one of them is the current page, and a collapsed parent
 * shows its icon, its entries in a menu beside it, see the review on #3263.
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
  // The parent of the current page opens by itself, and a person opens and folds any parent; their choice holds until
  // the page changes, when the new page's parent opens again.
  const activeParent = entries.find(
    (entry) => 'children' in entry && entry.children.some((child) => child.key === activeKey),
  )?.key
  const [choice, setChoice] = useState<{ page: string | undefined; open: string[] }>()
  const openKeys = choice !== undefined && choice.page === activeKey ? choice.open : activeParent ? [activeParent] : []
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
  const { spacing } = useDesignTokens()
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
      // Proposed: an entry's words sit at spacing-lg from the sidebar's edge, inside its spacing-xs inset, and a group's
      // heading at spacing-md, as in every list with group headings, see the review on #3263.
      inlineIndent={spacing.lg - spacing.xs}
      inlineCollapsed={collapsed}
      style={{ borderInlineEnd: 'none' }}
      selectedKeys={activeKey ? [activeKey] : []}
      openKeys={collapsed ? undefined : openKeys}
      // Proposed: a parent opens and folds with the one fold mark and no animation, see FoldIcon.
      // Ant Design keeps the room of its own arrow at a parent's end, its 10 px arrow and spacing-xs beside its padding,
      // so the fold mark takes it back and ends spacing-md from the entry's edge, as its words start spacing-md from the
      // other edge.
      expandIcon={({ isOpen }) =>
        collapsed ? null : (
          <span style={{ display: 'inline-flex', marginInlineEnd: -(menuArrowSize + spacing.xs) }}>
            <FoldIcon open={isOpen ?? false} />
          </span>
        )
      }
      motion={{ motionAppear: false, motionEnter: false, motionLeave: false }}
      onOpenChange={(open) => setChoice({ page: activeKey, open })}
      items={items}
    />
  )
}
