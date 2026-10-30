import { Breadcrumb, Flex, Typography } from 'antd'
import type { ReactNode } from 'react'
import { Link } from 'react-router'
import { useText } from './texts'
import { useDesignTokens } from './themeMode'

/** One step of a page's breadcrumb. A step with `to` is a link, and the last step has none. */
export interface BreadcrumbStep {
  label: string
  to?: string
}

/**
 * The layout of a page: its title as a heading, with the page's sections stacked below it. With a
 * breadcrumb, the title sits under it in a band on the surface colour that spans the page area, and
 * the last step is marked as the current page. It replaces the old page container. Pages add no
 * spacing or styling of their own.
 */
export function PageLayout({
  title,
  breadcrumb,
  actions,
  children,
}: {
  title: string
  breadcrumb?: readonly BreadcrumbStep[]
  /** Proposed: the page's own actions, on the right of its title, such as Studio's Validate and Publish. */
  actions?: ReactNode
  children: ReactNode
}) {
  const title4 = (
    <Typography.Title level={4} style={{ margin: 0 }}>
      {title}
    </Typography.Title>
  )
  const heading = actions ? (
    <Flex align="center" justify="space-between" gap="middle" wrap>
      {title4}
      {actions}
    </Flex>
  ) : (
    title4
  )

  return (
    <Flex vertical gap="middle">
      {breadcrumb ? <HeaderBand steps={breadcrumb}>{heading}</HeaderBand> : heading}
      {children}
    </Flex>
  )
}

function HeaderBand({ steps, children }: { steps: readonly BreadcrumbStep[]; children: ReactNode }) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()

  return (
    <div
      style={{
        // Negative margins reach the edges of the content area, whose padding is spacing.lg.
        margin: `-${spacing.lg}px -${spacing.lg}px 0`,
        padding: `${spacing.lg}px ${spacing.lg}px ${spacing.md}px`,
        background: colour.surface,
        borderBottom: `1px solid ${colour.borderSubtle}`,
      }}
    >
      <Flex vertical gap={spacing.xs}>
        <Breadcrumb
          aria-label={t('shell.breadcrumb.label')}
          items={steps.map((step) => ({
            title: step.to ? <Link to={step.to}>{step.label}</Link> : <span aria-current="page">{step.label}</span>,
          }))}
        />
        {children}
      </Flex>
    </div>
  )
}
