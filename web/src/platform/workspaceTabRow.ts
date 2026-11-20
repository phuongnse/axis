import type { TabsProps } from 'antd'
import type { DesignTokens } from './designTokens'

/**
 * The height of a row that heads a region of a workspace: a tab row and a pane's title row. They share it, so the tab
 * row's rule is level with the rule under the explorer's Files, see docs/reference/actions-and-controls.md#tab-bars.
 */
export function workspaceRowHeight(tokens: DesignTokens): number {
  return tokens.size.smallControl + tokens.spacing.md
}

/**
 * The look of a workspace's tab row: the same padding and a `borderSubtle` rule the region's width, which every tab row
 * of a workspace takes, see docs/reference/actions-and-controls.md#tab-bars. Spread it on a line `Tabs`.
 */
export function workspaceTabRow(tokens: DesignTokens): Pick<TabsProps, 'size' | 'tabBarStyle'> {
  return {
    size: 'small',
    tabBarStyle: {
      boxSizing: 'border-box',
      height: workspaceRowHeight(tokens),
      margin: 0,
      paddingInline: tokens.spacing.md,
      borderBottom: `1px solid ${tokens.colour.borderSubtle}`,
    },
  }
}
