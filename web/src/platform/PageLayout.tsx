import { Flex, Typography } from 'antd'
import type { ReactNode } from 'react'

/**
 * The layout of a page: its title as a heading, with the page's sections stacked below it. It
 * replaces the old page container. Pages add no spacing or styling of their own.
 */
export function PageLayout({ title, children }: { title: string; children: ReactNode }) {
  return (
    <Flex vertical gap="middle">
      <Typography.Title level={4} style={{ margin: 0 }}>
        {title}
      </Typography.Title>
      {children}
    </Flex>
  )
}
