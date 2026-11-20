/**
 * Proposed, see #3978: one action of a menu, such as an entry of a split button's menu or a select's own action after
 * its choices.
 */
export interface MenuAction {
  key: string
  label: string
  /** Drawn in `dangerText`, as a menu draws a destructive entry. */
  destructive?: boolean
  disabled?: boolean
  onClick: () => void
}

/** The items and the click handler that Ant Design's `Menu` and a `Dropdown`'s menu take for `actions`. */
export function menuOf(actions: readonly MenuAction[], onChosen?: () => void) {
  return {
    items: actions.map((action) => ({
      key: action.key,
      label: action.label,
      danger: action.destructive,
      disabled: action.disabled,
    })),
    onClick: ({ key }: { key: string }) => {
      onChosen?.()
      actions.find((action) => action.key === key)?.onClick()
    },
  }
}
