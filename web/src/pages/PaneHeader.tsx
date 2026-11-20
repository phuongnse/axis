import { Flex, Typography } from 'antd'
import type { ReactNode } from 'react'
import { useDesignTokens } from '../platform/themeMode'

/**
 * Proposed, see #3978: the title row of one pane of a package's workspace, such as the explorer's Files or the Run tab's
 * Input and Result. It is `height` high, as the editor's tab row, with the title in strong text on the left, its actions
 * on the right and a rule under it that runs the pane's whole width, so the workspace stays parted by rules, not boxes.
 */
export function PaneHeader({ title, actions, height }: { title: ReactNode; actions?: ReactNode; height: number }) {
  const { colour, size, spacing, type } = useDesignTokens()
  return (
    <Flex
      align="center"
      justify="space-between"
      gap="small"
      style={{
        flex: 'none',
        height,
        paddingInline: spacing.md,
        borderBottom: `1px solid ${colour.borderSubtle}`,
      }}
    >
      {typeof title === 'string' ? <Typography.Text strong>{title}</Typography.Text> : title}
      {/* A title row's actions are small, and the last one's icon reaches the row's inset, so it lines up with the fields
          below: the row pulls in the space a small icon button keeps round its icon. */}
      {actions && (
        <Flex align="center" gap="small" style={{ marginInlineEnd: -(size.smallControl - type.fontSize) / 2 }}>
          {actions}
        </Flex>
      )}
    </Flex>
  )
}
