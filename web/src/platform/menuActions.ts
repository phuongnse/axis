import type { MenuProps } from 'antd'

/**
 * An action that a menu or a split button lists, see docs/reference/actions-and-controls.md#buttons. `label` is its
 * text, `destructive` draws it in `dangerText`, and `attributes` go to its button when it is not in a menu.
 */
export interface MenuAction {
  key: string
  label: string
  destructive?: boolean
  disabled?: boolean
  onClick: () => void
  attributes?: object
}

/**
 * The entries of an Ant Design menu for `actions`, in their order, see docs/reference/actions-and-controls.md#menus. A
 * destructive action comes last, after one divider, when the menu also has other actions. The theme draws a `danger`
 * entry in `dangerText`.
 */
export function menuOf(actions: readonly MenuAction[]): NonNullable<MenuProps['items']> {
  const entry = (action: MenuAction) => ({
    key: action.key,
    label: action.label,
    danger: action.destructive,
    disabled: action.disabled,
    onClick: action.onClick,
  })
  const others = actions.filter((action) => action.destructive !== true)
  const destructive = actions.filter((action) => action.destructive === true)
  if (others.length === 0 || destructive.length === 0) {
    return actions.map(entry)
  }
  return [...others.map(entry), { type: 'divider' as const, key: 'destructive-divider' }, ...destructive.map(entry)]
}
