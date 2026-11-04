import { Breadcrumb, Flex, Typography } from 'antd'
import { useContext, type ReactNode } from 'react'
import { Link } from 'react-router'
import { sizeTokens } from './designTokens'
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
  compact: compactProp,
  status,
  subtitle,
  headingExtra,
  titleExtra,
  children,
}: {
  title: string
  breadcrumb?: readonly BreadcrumbStep[]
  /** Proposed: the page's own actions, on the right of its title, such as Studio's Validate and Publish. */
  actions?: ReactNode
  /**
   * Proposed: a compact page, such as every page of Studio, draws its band as one row of the page's state and actions,
   * and heads its content with the title. Without it, a workspace that claimed the shell's sidebar is compact.
   */
  compact?: boolean
  /** Proposed: what a compact band shows of the page's state, such as Studio's errors and changes that are not live. */
  status?: ReactNode
  /** Proposed: one line in textMuted under a compact page's heading, such as an open resource's file. */
  subtitle?: ReactNode
  /** Proposed: what sits on the right of a compact page's heading, such as a scenario's Run scenario. */
  headingExtra?: ReactNode
  /** Proposed: what follows a compact page's title on its line, such as a release's Live and Off-standard tags. */
  titleExtra?: ReactNode
  children: ReactNode
}) {
  const { colour, spacing } = useDesignTokens()
  const contentPadding = useContext(ShellSidebarContext)?.contentPadding ?? spacing.lg
  const compact = compactProp ?? contentPadding < spacing.lg
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

  if (compact) {
    // Proposed: every compact page has one heading, the page's title, at the head of its content: the band holds no
    // title, only the page's state and actions, and a page with neither has no band. The heading has no margin of its
    // own, so it sits as far below the band and above the content as the page area's padding.
    return (
      <Flex vertical gap={contentPadding}>
        {(actions || status) && <CompactBand actions={actions} status={status} />}
        <Flex vertical gap={4}>
          <Flex align="center" justify="space-between" gap="middle" wrap>
            {titleExtra ? (
              <Flex align="center" gap="small" wrap>
                {title4}
                {titleExtra}
              </Flex>
            ) : (
              title4
            )}
            {headingExtra}
          </Flex>
          {subtitle && <Typography.Text style={{ color: colour.textMuted }}>{subtitle}</Typography.Text>}
        </Flex>
        {children}
      </Flex>
    )
  }

  return (
    <Flex vertical gap="middle">
      {breadcrumb ? <HeaderBand steps={breadcrumb}>{heading}</HeaderBand> : heading}
      {children}
    </Flex>
  )
}

/** Negative margins reach the edges of the page area, whose padding the shell gives. */
function useBandMargin() {
  const { spacing } = useDesignTokens()
  return useContext(ShellSidebarContext)?.contentPadding ?? spacing.lg
}

/** Proposed: the compact band, one bar high: the page's state on the left and its actions on the right. */
function CompactBand({ actions, status }: { actions?: ReactNode; status?: ReactNode }) {
  const { colour, spacing } = useDesignTokens()
  const pad = useBandMargin()
  return (
    <div
      data-axis-band
      style={{
        // Proposed: the band takes its width from the page alone, so what it holds can tell when it has too little room.
        contain: 'inline-size',
        margin: `-${pad}px -${pad}px 0`,
        // Proposed: the compact band, which holds the page's actions, stays under the shell's header as the page scrolls.
        position: 'sticky',
        top: sizeTokens.bar,
        zIndex: 9,
        // One bar high with its rule, as the shell's header and Studio's application row, so their rules line up.
        padding: `0 ${pad}px`,
        minHeight: sizeTokens.bar,
        boxSizing: 'border-box',
        display: 'flex',
        alignItems: 'center',
        gap: spacing.md,
        background: colour.surface,
        borderBottom: `1px solid ${colour.borderSubtle}`,
      }}
    >
      {status}
      <div style={{ flex: 1 }} />
      {actions}
    </div>
  )
}

function HeaderBand({ steps, children }: { steps: readonly BreadcrumbStep[]; children: ReactNode }) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const pad = useBandMargin()

  return (
    <div
      style={{
        margin: `-${pad}px -${pad}px 0`,
        padding: `${spacing.lg}px ${pad}px ${spacing.md}px`,
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
