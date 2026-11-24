import type { MenuProps } from 'antd'
import type { ReactNode } from 'react'
import { Link } from 'react-router'

/** `useText`'s function, which gives the text of a key. */
type Text = (key: string) => string

type Item = NonNullable<MenuProps['items']>[number]

/**
 * The name of an action. One the platform knows, such as Remove, has a `labelKey` and may have an `icon`, a `meaning` and
 * a `nameSuffix`. An `icon` shows only where a row draws an icon button, named by its text as a tooltip. One whose name
 * an author wrote, such as an application's title, has a `label` and nothing else.
 */
type Named =
  | { labelKey: string; label?: never; icon?: ReactNode; meaning?: 'remove'; nameSuffix?: string }
  | { label: string; labelKey?: never; icon?: never; meaning?: never; nameSuffix?: never }

/**
 * An action of a row, an action row, a split button or a menu, see docs/reference/actions-and-controls.md#menus. A
 * `destructive` action is drawn in `dangerText`. A `selected` action is a choice, which a menu marks, and a choice
 * with an `href` is a link. `groupKey` is the heading of a choice's group. `attributes` go to its button.
 */
export type AxisAction = Named & {
  key: string
  destructive?: boolean
  disabled?: boolean
  /** Whether a choice is the chosen one. Leave it out for an action. */
  selected?: boolean
  /** A link: a path is a router link, and anything else a plain link to its address. */
  href?: string
  /** The text key of the heading of the group a choice sits in. */
  groupKey?: string
  /** A button shows it as a spinner. A menu entry does not show it. */
  loading?: boolean
  onClick?: () => void
  /** The attributes of its button, such as a `data-testid`. A menu entry keeps them too, and the fixed fields win. */
  attributes?: object
}

/** The text of `action`, whether its key or its author's own text. */
export function actionText(action: AxisAction, t: Text): string {
  return action.labelKey === undefined ? action.label : t(action.labelKey)
}

/** The label of a menu entry: its text, or a link to its address when it has one. */
function labelOf(action: AxisAction, t: Text): ReactNode {
  const text = actionText(action, t)
  if (action.href === undefined) {
    return text
  }
  if (action.href.startsWith('/')) {
    return (
      <Link to={action.href} aria-current={action.selected ? 'page' : undefined}>
        {text}
      </Link>
    )
  }
  return <a href={action.href}>{text}</a>
}

/** The entry of `action` in an Ant Design menu. A choice with no address is a checked radio entry. */
function entryOf(action: AxisAction, t: Text): Item {
  const entry = {
    ...action.attributes,
    key: action.key,
    label: labelOf(action, t),
    danger: action.destructive,
    disabled: action.disabled,
    onClick: action.onClick,
  }
  if (action.selected === undefined || action.href !== undefined) {
    return entry as Item
  }
  return { ...entry, role: 'menuitemradio', 'aria-checked': action.selected } as Item
}

/**
 * The menu of `actions`, see docs/reference/actions-and-controls.md#menus. The choices come first, in their groups with
 * a heading and no divider between groups. Then come the other actions, behind one divider, and a destructive action
 * last, behind another. A menu with a choice marks it, through the `selectedKeys` this returns. Spread the result into
 * a menu's props.
 */
export function menuOf(
  actions: readonly AxisAction[],
  t: Text,
): Pick<MenuProps, 'items' | 'selectable' | 'selectedKeys'> {
  const choices = actions.filter((action) => action.selected !== undefined)
  const others = actions.filter((action) => action.selected === undefined && action.destructive !== true)
  const destructive = actions.filter((action) => action.selected === undefined && action.destructive === true)

  const items: Item[] = []
  const groups = new Map<string, { type: 'group'; key: string; label: string; children: Item[] }>()
  for (const choice of choices) {
    const entry = entryOf(choice, t)
    if (choice.groupKey === undefined) {
      items.push(entry)
      continue
    }
    let group = groups.get(choice.groupKey)
    if (group === undefined) {
      group = { type: 'group', key: choice.groupKey, label: t(choice.groupKey), children: [] }
      groups.set(choice.groupKey, group)
      items.push(group as Item)
    }
    group.children.push(entry)
  }

  const actionItems = others.map((action) => entryOf(action, t))
  if (items.length > 0 && actionItems.length > 0) {
    items.push({ type: 'divider', key: 'actions-divider' })
  }
  items.push(...actionItems)

  const destructiveItems = destructive.map((action) => entryOf(action, t))
  if (items.length > 0 && destructiveItems.length > 0) {
    items.push({ type: 'divider', key: 'destructive-divider' })
  }
  items.push(...destructiveItems)

  return {
    items,
    selectable: choices.length > 0,
    selectedKeys: choices.filter((choice) => choice.selected).map((choice) => choice.key),
  }
}
