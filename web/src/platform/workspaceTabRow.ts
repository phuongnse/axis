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
 * of a workspace takes, see docs/reference/actions-and-controls.md#tab-bars. Spread it on a line `Tabs`. A tab row on a
 * page gives the content area's padding as `bleed`.
 */
export function workspaceTabRow(tokens: DesignTokens, bleed = 0): Pick<TabsProps, 'size' | 'tabBarStyle'> {
  return {
    size: 'small',
    tabBarStyle: {
      boxSizing: 'border-box',
      height: workspaceRowHeight(tokens),
      // Proposed, #3264 and #4888: on a page, `bleed` is the content area's padding, so the row's rule runs the whole
      // way and its tabs still start at the page's inset.
      // What the tabs show starts `spacing-md` under the rule, as a part does under its rule. The row's own space above
      // its tabs stands for most of the gap under the page's heading, so the tabs sit as far under the heading's line as
      // the heading sits under the band's rule.
      margin: bleed === 0 ? 0 : `-${tokens.spacing.sm}px -${bleed}px ${tokens.spacing.md}px`,
      paddingInline: bleed === 0 ? tokens.spacing.md : bleed,
      borderBottom: `1px solid ${tokens.colour.borderSubtle}`,
    },
  }
}
