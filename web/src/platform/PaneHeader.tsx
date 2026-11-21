import { Flex } from 'antd'
import type { ReactNode } from 'react'
import { useDesignTokens } from './themeMode'
import { workspaceRowHeight } from './workspaceTabRow'

/**
 * The title row of a pane of a workspace, such as the explorer's Files, see docs/reference/shared-widgets.md#which-widget.
 * The title is a level-two heading at the body size, with an optional `tag` beside it, such as a run's outcome as a
 * `StatusTag`, and the pane's light `actions` sit on the right. The row has the pane's inset and a `borderSubtle` rule
 * under it, and no box.
 */
export function PaneHeader({ title, tag, actions }: { title: string; tag?: ReactNode; actions?: ReactNode }) {
  const tokens = useDesignTokens()
  return (
    <Flex
      align="center"
      justify="space-between"
      gap={tokens.spacing.xs}
      style={{
        boxSizing: 'border-box',
        height: workspaceRowHeight(tokens),
        paddingInline: tokens.spacing.md,
        borderBottom: `1px solid ${tokens.colour.borderSubtle}`,
      }}
    >
      <Flex align="center" gap={tokens.spacing.xs}>
        <h2 style={{ margin: 0, fontSize: 'inherit', fontWeight: 600, lineHeight: 'inherit' }}>{title}</h2>
        {tag}
      </Flex>
      {actions}
    </Flex>
  )
}
