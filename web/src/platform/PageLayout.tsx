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
  compact: compactProp,
  children,
}: {
  title: string
  breadcrumb?: readonly BreadcrumbStep[]
  /** Proposed: the page's own actions, on the right of its title, such as Studio's Validate and Publish. */
  actions?: ReactNode
  /**
   * Proposed: a compact page, such as every page of Studio, draws its band as one row: the breadcrumb, whose last step
   * is the page's title, then the actions. Without it, a workspace that claimed the shell's sidebar is compact.
   */
  compact?: boolean
  children: ReactNode
}) {
  const { spacing } = useDesignTokens()
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

  return (
    // Proposed: under a compact band the content starts as far from it as from the page area's sides.
    <Flex vertical gap={compact ? contentPadding : 'middle'}>
      {breadcrumb && compact ? (
        <CompactBand steps={breadcrumb} title={title} actions={actions} />
      ) : breadcrumb ? (
        <HeaderBand steps={breadcrumb}>{heading}</HeaderBand>
      ) : (
        heading
      )}
      {children}
    </Flex>
  )
}

/** Negative margins reach the edges of the page area, whose padding the shell gives. */
function useBandMargin() {
  const { spacing } = useDesignTokens()
  return useContext(ShellSidebarContext)?.contentPadding ?? spacing.lg
}

/**
 * Proposed: the compact band, one row 48 px high: the breadcrumb, whose last step is the page's title as its heading,
 * and the actions on the right.
 */
function CompactBand({
  steps,
  title,
  actions,
}: {
  steps: readonly BreadcrumbStep[]
  title: string
  actions?: ReactNode
}) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const pad = useBandMargin()
  const earlier = steps.slice(0, -1)
  return (
    <div
      style={{
        margin: `-${pad}px -${pad}px 0`,
        // 48 px with its rule, as the shell's header and Studio's application row, so their rules line up.
        padding: `0 ${pad}px`,
        minHeight: 48,
        boxSizing: 'border-box',
        display: 'flex',
        alignItems: 'center',
        gap: spacing.md,
        background: colour.surface,
        borderBottom: `1px solid ${colour.borderSubtle}`,
      }}
    >
      <Breadcrumb
        aria-label={t('shell.breadcrumb.label')}
        style={{ flex: 1, minWidth: 0 }}
        items={[
          ...earlier.map((step) => ({
            title: step.to ? <Link to={step.to}>{step.label}</Link> : <span>{step.label}</span>,
          })),
          {
            title: (
              <Typography.Title level={5} aria-current="page" style={{ display: 'inline', margin: 0 }}>
                {title}
              </Typography.Title>
            ),
          },
        ]}
      />
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
