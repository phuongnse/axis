import { Menu } from 'antd'
import { useDesignTokens } from './themeMode'

/** One action on a select's choices, such as Save as… or Delete. */
export interface ChoiceAction {
  key: string
  label: string
  /** Drawn in `dangerText`, as a menu draws a destructive entry. */
  destructive?: boolean
  disabled?: boolean
  onClick: () => void
}

/**
 * Proposed, see #3978: the actions on a select's choices, drawn in its list after the choices and a divider, as a menu
 * that holds both puts its actions after its choices, see docs/reference/actions-and-controls.md#menus. A select's
 * `popupRender` draws them under its list, so the select keeps the field's width and carries its own actions. `onDone`
 * closes the list once an action is chosen.
 */
export function ChoiceActions({ actions, onDone }: { actions: readonly ChoiceAction[]; onDone: () => void }) {
  const { spacing } = useDesignTokens()
  return (
    <>
      {/* An action reads as one more item of the list: the inset, height and corners of the select's own options. */}
      <style>{`.axis-choice-actions .ant-menu-item { margin: 0; width: 100%; padding-inline: ${spacing.sm}px !important; border-radius: 4px; } .axis-choice-actions .ant-menu-item-divider { margin-block: 4px; }`}</style>
      <Menu
        className="axis-choice-actions"
        selectable={false}
        style={{ border: 'none', background: 'transparent' }}
        items={[
          { type: 'divider' },
          ...actions.map((action) => ({
            key: action.key,
            label: action.label,
            danger: action.destructive,
            disabled: action.disabled,
          })),
        ]}
        onClick={({ key }) => {
          onDone()
          actions.find((action) => action.key === key)?.onClick()
        }}
      />
    </>
  )
}
