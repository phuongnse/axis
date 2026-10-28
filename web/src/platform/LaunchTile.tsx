import { Avatar, Flex, Typography } from 'antd'
import type { ReactNode } from 'react'
import { Link } from 'react-router'
import { useDesignTokens } from './themeMode'

/** The first letter of a title on a `primary` tile, the mark of a site. */
export function TitleMark({ title, size }: { title: string; size: number }) {
  const { colour } = useDesignTokens()
  return (
    <Avatar
      aria-hidden
      shape="square"
      size={size}
      style={{ flex: 'none', background: colour.primary, fontWeight: 600 }}
    >
      {title.trim().charAt(0).toUpperCase()}
    </Avatar>
  )
}

/**
 * Proposed: a tile of the home page, which opens a site or a tool. It shows a mark, a title and an optional
 * description, and the whole tile is the link.
 */
export function LaunchTile({
  to,
  mark,
  title,
  description,
}: {
  to: string
  mark: ReactNode
  title: string
  description?: string
}) {
  const { colour, spacing, radius } = useDesignTokens()
  return (
    <Link
      to={to}
      style={{
        display: 'flex',
        alignItems: 'center',
        gap: spacing.sm,
        padding: spacing.md,
        background: colour.surface,
        border: `1px solid ${colour.borderSubtle}`,
        borderRadius: radius.base,
        color: colour.text,
      }}
    >
      {mark}
      <Flex vertical style={{ minWidth: 0 }}>
        <Typography.Text strong ellipsis>
          {title}
        </Typography.Text>
        {description && <Typography.Text type="secondary">{description}</Typography.Text>}
      </Flex>
    </Link>
  )
}
