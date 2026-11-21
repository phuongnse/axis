import { ConfigProvider, Divider, Menu } from 'antd'
import type { ReactNode } from 'react'
import { useChoiceActionsTheme } from './antTheme'
import { menuOf, type MenuAction } from './menuActions'

/**
 * A select's own actions at the end of its list, such as Manage saved inputs…, see
 * docs/reference/actions-and-controls.md#menus. It draws `list`, the select's own list from `popupRender`, then a
 * divider and the `actions` as a menu in the look of the list's items, built by `menuOf` as `AxisSplitButton`'s menu
 * is. Ant Design keeps a select open after a click inside its list, so the select's caller controls `open` and `close`
 * closes it before the action runs. Choosing an action leaves the select's value as it was.
 */
export function ChoiceActions({
  list,
  actions,
  close,
}: {
  list: ReactNode
  actions: readonly MenuAction[]
  close: () => void
}) {
  const choiceTheme = useChoiceActionsTheme()
  return (
    <>
      {list}
      <ConfigProvider theme={choiceTheme}>
        <Divider />
        <Menu
          mode="vertical"
          selectable={false}
          style={{ border: 'none', background: 'transparent' }}
          items={menuOf(
            actions.map((action) => ({
              ...action,
              onClick: () => {
                close()
                action.onClick()
              },
            })),
          )}
        />
      </ConfigProvider>
    </>
  )
}
