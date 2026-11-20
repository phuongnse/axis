import { Flex } from 'antd'
import type { ReactNode } from 'react'
import { AxisActionRow, type AxisFoldAction } from './AxisButton'
import { useDesignTokens } from './themeMode'

/**
 * A bar of actions at the foot of a page, see docs/reference/actions-and-controls.md#buttons. It is drawn on `surface`
 * with a `borderSubtle` rule above it. `start` is drawn at its start edge. The `fold` actions and `end` are drawn at its
 * end edge, and the `fold` actions become the entries of one More actions menu when the bar is too narrow, as
 * `AxisActionRow` folds them. The bar has no margin and no position, so its caller places it. A bar that must stay at
 * the foot of the window draws in `ShellBarSlot`. `data-testid` names the bar.
 */
export function ActionBar({
  start,
  fold,
  end,
  'data-testid': testId,
}: {
  start?: ReactNode
  fold?: AxisFoldAction[]
  end?: ReactNode
  'data-testid'?: string
}): ReactNode {
  const { size, colour, spacing } = useDesignTokens()
  return (
    <Flex
      data-action-bar
      data-testid={testId}
      align="center"
      justify="space-between"
      gap="middle"
      style={{
        minHeight: size.bar,
        boxSizing: 'border-box',
        paddingBlock: spacing.xs,
        paddingInline: spacing.lg,
        background: colour.surface,
        borderTop: `1px solid ${colour.borderSubtle}`,
      }}
    >
      {start !== undefined && <AxisActionRow align="center">{start}</AxisActionRow>}
      <AxisActionRow align="center" justify="flex-end" fold={fold ?? []} end={end} />
    </Flex>
  )
}
