import { Flex } from 'antd'
import type { ReactNode } from 'react'
import { useDesignTokens } from './themeMode'

/**
 * Proposed, see the reviews on #3263 and #3650: the bar that holds what finishes a page's task, such as Save, Next or
 * Publish, with an optional short line at its start. It is the last thing of the page area and sticks to the window's
 * foot while the page scrolls, so the task can be finished from anywhere on a long page, and it rests right above the
 * footer at the page's end. It spans the content, never the sidebar, on `surface` with a `borderSubtle` rule above it.
 */
export function ActionBar({ line, children }: { line?: ReactNode; children: ReactNode }) {
  const { colour, spacing, size } = useDesignTokens()
  return (
    <Flex
      data-testid="action-bar"
      align="center"
      justify="space-between"
      gap="middle"
      style={{
        position: 'sticky',
        bottom: 0,
        zIndex: 10,
        minHeight: size.bar,
        boxSizing: 'border-box',
        paddingBlock: spacing.xs,
        background: colour.surface,
        borderTop: `1px solid ${colour.borderSubtle}`,
      }}
    >
      <span>{line}</span>
      {children}
    </Flex>
  )
}
