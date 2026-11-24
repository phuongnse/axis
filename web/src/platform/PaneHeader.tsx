import { Flex } from 'antd'
import type { ReactNode } from 'react'
import { useDesignTokens } from './themeMode'
import { workspaceRowHeight } from './workspaceTabRow'

/**
 * The title row of a pane of a workspace, such as the explorer's Files, or of a block inside a pane, such as a process
 * step, see docs/reference/shared-widgets.md#which-widget. The title is a heading at the body size, a level-two heading
 * by default and a level-three one for a block inside a section, with an optional status `tag` beside it, and the pane's
 * light `actions` sit on the right. The row takes the `inset` it is given, `spacing-md` by default, and a `borderSubtle`
 * rule under it, and no box. `id` names the heading, so that a section can point at it.
 */
export function PaneHeader({
  title,
  tag,
  actions,
  inset,
  level = 2,
  id,
}: {
  title: string
  tag?: ReactNode
  actions?: ReactNode
  /** The row's side padding, from the region's inset: `spacing-md` when not given, see docs/reference/shell.md#layout. */
  inset?: number
  level?: 2 | 3
  id?: string
}) {
  const tokens = useDesignTokens()
  const padding = inset ?? tokens.spacing.md
  const Heading = level === 2 ? 'h2' : 'h3'
  return (
    <Flex
      align="center"
      justify="space-between"
      gap={tokens.spacing.xs}
      style={{
        boxSizing: 'border-box',
        height: workspaceRowHeight(tokens),
        paddingInline: padding,
        borderBottom: `1px solid ${tokens.colour.borderSubtle}`,
      }}
    >
      <Flex align="center" gap={tokens.spacing.xs}>
        <Heading id={id} style={{ margin: 0, fontSize: 'inherit', fontWeight: 600, lineHeight: 'inherit' }}>
          {title}
        </Heading>
        {tag}
      </Flex>
      {actions}
    </Flex>
  )
}
