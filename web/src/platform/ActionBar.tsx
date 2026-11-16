import { Flex } from 'antd'
import { useContext, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { ShellBarSlot } from './shellBar'
import { useDesignTokens } from './themeMode'

/**
 * Proposed, see the reviews on #3263 and #3650: the bar that holds what finishes a page's task, such as Save or a
 * task's outcomes, with what it says at its start and its actions at its end. It draws in the shell's place right
 * above the footer, which sticks to the window's foot while the page scrolls, as wide as the content. `SaveBar` draws
 * in it. Outside a shell it draws where it stands.
 */
export function ActionBar({ testId, start, children }: { testId: string; start?: ReactNode; children: ReactNode }) {
  const { size, colour, spacing } = useDesignTokens()
  const slot = useContext(ShellBarSlot)
  const bar = (
    <Flex
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
      <div>{start}</div>
      {children}
    </Flex>
  )
  return slot ? createPortal(bar, slot) : bar
}
