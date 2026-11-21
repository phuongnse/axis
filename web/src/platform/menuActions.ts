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

/** The entries of an Ant Design menu for `actions`, in their order. The theme draws a `danger` entry in `dangerText`. */
export function menuOf(actions: readonly MenuAction[]): NonNullable<MenuProps['items']> {
  return actions.map((action) => ({
    key: action.key,
    label: action.label,
    danger: action.destructive,
    disabled: action.disabled,
    onClick: action.onClick,
  }))
}
