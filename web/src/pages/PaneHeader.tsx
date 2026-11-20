import { Flex, Typography } from 'antd'
import type { ReactNode } from 'react'
import { useDesignTokens } from '../platform/themeMode'

/**
 * Proposed, see #3978: the title row of one pane of a package's workspace, such as the explorer's Files or the Run tab's
 * Input and Result. It is `height` high, as the editor's tab row, with the title in strong text on the left, its actions
 * on the right and a rule under it that runs the pane's whole width, so the workspace stays parted by rules, not boxes.
 */
export function PaneHeader({
  title,
  actions,
  height,
  rule = true,
}: {
  title: ReactNode
  actions?: ReactNode
  height: number
  /**
   * Proposed, see #3978: whether a rule runs under the row. A title and the content under it are one block, so the row
   * draws none when nothing else lines up with it, such as the Run tab's Input, Output and Log. The explorer's Files
   * keeps it, as it continues the rule under the editor's tab row.
   */
  rule?: boolean
}) {
  const { colour, spacing } = useDesignTokens()
  return (
    <Flex
      align="center"
      justify="space-between"
      gap="small"
      style={{
        flex: 'none',
        height,
        paddingInline: spacing.md,
        borderBottom: rule ? `1px solid ${colour.borderSubtle}` : undefined,
      }}
    >
      {typeof title === 'string' ? <Typography.Text strong>{title}</Typography.Text> : title}
      {actions}
    </Flex>
  )
}
