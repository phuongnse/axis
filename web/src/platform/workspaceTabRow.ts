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
 *
 * With `bleed`, a page's tab row reaches past the page's padding, the shell's `ContentInsetContext`, to the page area's
 * edges. It sits `spacing-sm` into the gap above it and keeps `spacing-md` under its rule, see the same section.
 */
export function workspaceTabRow(
  tokens: DesignTokens,
  { bleed }: { bleed?: number } = {},
): Pick<TabsProps, 'size' | 'tabBarStyle'> {
  const { spacing } = tokens
  return {
    size: 'small',
    tabBarStyle: {
      boxSizing: 'border-box',
      height: workspaceRowHeight(tokens),
      // The shorthand, as jsdom drops logical properties.
      ...(bleed === undefined
        ? { margin: 0, paddingInline: spacing.md }
        : { margin: `-${spacing.sm}px -${bleed}px ${spacing.md}px`, padding: `0 ${bleed}px` }),
      borderBottom: `1px solid ${tokens.colour.borderSubtle}`,
    },
  }
}
