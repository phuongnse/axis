import { Breadcrumb, Flex, Typography } from 'antd'
import type { ReactNode } from 'react'
import { Link } from 'react-router'
import { useDesignTokens } from './themeMode'

/** One step of a page's breadcrumb: its label, and the address it opens, which the current page has none of. */
export interface BreadcrumbStep {
  label: string
  to?: string
}

/**
 * The layout of a page: its header, then the page's sections stacked below it. It replaces the old page container.
 * Pages add no spacing or styling of their own. Proposed: the header is a band on `surface` across the content area,
 * with the breadcrumb above the title, over the sections on `canvas`.
 */
export function PageLayout({
  title,
  breadcrumb = [],
  children,
}: {
  title: string
  /** Proposed: the steps that lead to the page, from the site down, the last one the page itself. */
  breadcrumb?: readonly BreadcrumbStep[]
  children: ReactNode
}) {
  const { colour, spacing } = useDesignTokens()
  return (
    <Flex vertical gap="middle">
      <header
        style={{
          margin: -spacing.lg,
          marginBottom: 0,
          padding: spacing.lg,
          paddingBottom: spacing.md,
          background: colour.surface,
          borderBottom: `1px solid ${colour.borderSubtle}`,
        }}
      >
        {breadcrumb.length > 1 && (
          <Breadcrumb
            style={{ marginBottom: spacing.xs }}
            items={breadcrumb.map((step, index) => ({
              key: index,
              title:
                step.to && index < breadcrumb.length - 1 ? (
                  <Link to={step.to}>{step.label}</Link>
                ) : (
                  <span aria-current={index === breadcrumb.length - 1 ? 'page' : undefined}>{step.label}</span>
                ),
            }))}
          />
        )}
        <Typography.Title level={4} style={{ margin: 0 }}>
          {title}
        </Typography.Title>
      </header>
      {children}
    </Flex>
  )
}
