import { Breadcrumb, Flex, Typography } from 'antd'
import { createContext, useContext, useState, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
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
 */
export function PageLayout({
  title,
  line,
  breadcrumb,
  children,
}: {
  title: string
  /** Proposed, see the review on #3650: a line under the heading, in `textMuted`, that says more about the page. */
  line?: ReactNode
  breadcrumb?: readonly BreadcrumbStep[]
  children: ReactNode
}) {
  const [slot, setSlot] = useState<HTMLDivElement | null>(null)
  const { colour } = useDesignTokens()
  const heading = (
    // Proposed: the page's own actions sit on the heading's right, as a Studio page's do, see the review on #3650.
    <Flex justify="space-between" align="center" gap="middle">
      <Flex vertical style={{ minWidth: 0 }}>
        {/* Level one for the outline, at the 20px and 28px that a level-four heading has. */}
        <Typography.Title level={1} style={{ margin: 0, fontSize: 20, lineHeight: '28px' }}>
          {title}
        </Typography.Title>
        {line !== undefined && <div style={{ color: colour.textMuted }}>{line}</div>}
      </Flex>
      <div ref={setSlot} />
    </Flex>
  )

  return (
    <PageActionsSlot.Provider value={slot}>
      <Flex vertical gap="middle">
        {breadcrumb ? <HeaderBand steps={breadcrumb}>{heading}</HeaderBand> : heading}
        {children}
      </Flex>
    </PageActionsSlot.Provider>
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

// Where the page's own actions go: the right of its heading. Outside a page layout, such as in a widget's preview, there is none.
const PageActionsSlot = createContext<HTMLDivElement | null | undefined>(undefined)

/**
 * Proposed, see the review on #3650: draws a widget's actions on the right of its page's heading, as the page's own
 * actions, such as a form's Submit for approval. Outside a page layout it draws them where it stands.
 */
export function PageActions({ children }: { children: ReactNode }) {
  const slot = useContext(PageActionsSlot)
  if (slot === undefined) {
    return <Flex justify="end">{children}</Flex>
  }
  return slot ? createPortal(children, slot) : null
}
