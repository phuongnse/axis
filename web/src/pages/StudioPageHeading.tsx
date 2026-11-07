import { Flex, Typography } from 'antd'
import type { ReactNode } from 'react'
import { useDesignTokens } from '../platform/themeMode'

/**
 * The heading of a Studio page: one level-4 heading with no margin of its own, optional lines under it in
 * `textMuted`, one line each, and the page's own actions on its right. Optional `tags` sit beside the title.
 */
export function StudioPageHeading({
  title,
  line,
  actions,
  tags,
}: {
  title: string
  line?: string | readonly string[] | null
  actions?: ReactNode
  tags?: ReactNode
}) {
  const { colour } = useDesignTokens()
  const lines = (typeof line === 'string' ? [line] : (line ?? [])).filter((text) => text !== '')
  return (
    <Flex justify="space-between" align="center" gap="middle">
      <Flex vertical style={{ minWidth: 0 }}>
        <Flex gap="small" align="center">
          <Typography.Title level={4} style={{ margin: 0 }}>
            {title}
          </Typography.Title>
          {tags}
        </Flex>
        {lines.map((text, index) => (
          <Typography.Text key={index} style={{ color: colour.textMuted }}>
            {text}
          </Typography.Text>
        ))}
      </Flex>
      {actions}
    </Flex>
  )
}
