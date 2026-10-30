import { Breadcrumb, Flex, Typography } from 'antd'
import { useContext, type ReactNode } from 'react'
import { Link } from 'react-router'
import { ShellSidebarContext } from './shellSidebar'
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
  const { spacing } = useDesignTokens()
  const compact = (useContext(ShellSidebarContext)?.contentPadding ?? spacing.lg) < spacing.lg
  const title4 = (
    <Typography.Title level={compact ? 5 : 4} style={{ margin: 0 }}>
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
    <Flex vertical gap={compact ? 'small' : 'middle'}>
      {breadcrumb ? <HeaderBand steps={breadcrumb}>{heading}</HeaderBand> : heading}
      {children}
    </Flex>
  )
}

function HeaderBand({ steps, children }: { steps: readonly BreadcrumbStep[]; children: ReactNode }) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  // Proposed: in a workspace, whose page area has spacing-md round it, the band is compact too.
  const pad = useContext(ShellSidebarContext)?.contentPadding ?? spacing.lg
  const compact = pad < spacing.lg

  return (
    <div
      style={{
        // Negative margins reach the edges of the content area, whose padding is `pad`.
        margin: `-${pad}px -${pad}px 0`,
        padding: compact ? `${spacing.sm}px ${pad}px` : `${spacing.lg}px ${spacing.lg}px ${spacing.md}px`,
        background: colour.surface,
        borderBottom: `1px solid ${colour.borderSubtle}`,
      }}
    >
      <Flex vertical gap={compact ? 0 : spacing.xs}>
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
