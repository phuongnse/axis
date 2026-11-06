import { Flex, Typography } from 'antd'
import type { ReactNode } from 'react'
import { useDesignTokens } from '../platform/themeMode'

/**
 * The heading of a Studio page: one level-4 heading with no margin of its own, an optional line under it in
 * `textMuted`, and the page's own actions on its right. Optional `tags` sit beside the title.
 */
export function StudioPageHeading({
  title,
  line,
  actions,
  tags,
}: {
  title: string
  line?: string | null
  actions?: ReactNode
  tags?: ReactNode
}) {
  const { colour } = useDesignTokens()
  return (
    <Flex justify="space-between" align="center" gap="middle">
      <Flex vertical style={{ minWidth: 0 }}>
        <Flex gap="small" align="center">
          <Typography.Title level={4} style={{ margin: 0 }}>
            {title}
          </Typography.Title>
          {tags}
        </Flex>
        {line && <Typography.Text style={{ color: colour.textMuted }}>{line}</Typography.Text>}
      </Flex>
      {actions}
    </Flex>
  )
}
