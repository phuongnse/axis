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
 * The layout of a page: its title as the page's level-one heading, drawn at the size of a level-four one, with
 * the page's sections stacked below it. With a breadcrumb, the title sits under it in a band on the surface colour that spans the page area, and
 * the last step is marked as the current page. It replaces the old page container. Pages add no
 * spacing or styling of their own.
 *
 * Proposed (#4662): a platform page's band holds its title alone, with no breadcrumb, as `band` draws it.
 */
export function PageLayout({
  title,
  breadcrumb,
  band = false,
  children,
}: {
  title: string
  breadcrumb?: readonly BreadcrumbStep[]
  /** Proposed (#4662): draws the band with the title alone, for a platform page. */
  band?: boolean
  children: ReactNode
}) {
  const heading = (
    // Level one for the outline, at the 20px and 28px that a level-four heading has.
    <Typography.Title level={1} style={{ margin: 0, fontSize: 20, lineHeight: '28px' }}>
      {title}
    </Typography.Title>
  )

  return (
    <Flex vertical gap="middle">
      {breadcrumb || band ? <HeaderBand steps={breadcrumb}>{heading}</HeaderBand> : heading}
      {children}
    </Flex>
  )
}

function HeaderBand({ steps, children }: { steps?: readonly BreadcrumbStep[]; children: ReactNode }) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()

  return (
    <div
      style={{
        // Negative margins reach the edges of the content area, whose padding is spacing.lg.
        margin: `-${spacing.lg}px -${spacing.lg}px 0`,
        // Proposed (#4662): a band with the title alone has spacing.md above and below it, and spacing.lg at its sides,
        // where the title lines up with the content.
        padding: steps ? `${spacing.lg}px ${spacing.lg}px ${spacing.md}px` : `${spacing.md}px ${spacing.lg}px`,
        background: colour.surface,
        borderBottom: `1px solid ${colour.borderSubtle}`,
      }}
    >
      <Flex vertical gap={spacing.xs}>
        {steps && (
          <Breadcrumb
            aria-label={t('shell.breadcrumb.label')}
            items={steps.map((step) => ({
              title: step.to ? <Link to={step.to}>{step.label}</Link> : <span aria-current="page">{step.label}</span>,
            }))}
          />
        )}
        {children}
      </Flex>
    </div>
  )
}
