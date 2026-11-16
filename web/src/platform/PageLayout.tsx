import { Breadcrumb, Flex, Typography } from 'antd'
import { createContext, useContext, useLayoutEffect, useState, type ReactNode } from 'react'
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
  breadcrumb,
  children,
}: {
  title: string
  breadcrumb?: readonly BreadcrumbStep[]
  children: ReactNode
}) {
  const [slot, setSlot] = useState<HTMLDivElement | null>(null)
  const [named, setNamed] = useState<string | null>(null)
  const [lineSlot, setLineSlot] = useState<HTMLDivElement | null>(null)
  const { colour, spacing } = useDesignTokens()
  const heading = (
    // Proposed: the page's own actions sit on the heading's right, as a Studio page's do, see the review on #3650.
    <Flex justify="space-between" align="center" gap="middle">
      <Flex vertical style={{ minWidth: 0 }}>
        {/* Level one for the outline, at the 20px and 28px that a level-four heading has. */}
        <Typography.Title level={1} style={{ margin: 0, fontSize: 20, lineHeight: '28px' }}>
          {named ?? title}
        </Typography.Title>
        {/* Proposed: the line that gives the thing the page names its context, see the review on #3650. */}
        <div ref={setLineSlot} style={{ color: colour.textMuted }} />
      </Flex>
      {/* The page's own actions of every widget sit in one row, in the order the widgets draw them. */}
      <div ref={setSlot} style={{ display: 'flex', gap: spacing.sm, flexWrap: 'wrap', justifyContent: 'end' }} />
    </Flex>
  )

  return (
    <PageActionsSlot.Provider value={slot}>
      <PageHeadingSlot.Provider value={{ name: setNamed, line: lineSlot }}>
        <Flex vertical gap="middle">
          {breadcrumb ? <HeaderBand steps={breadcrumb}>{heading}</HeaderBand> : heading}
          {children}
        </Flex>
      </PageHeadingSlot.Provider>
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

// Where a widget names what its page shows: the heading's text and the line under it.
const PageHeadingSlot = createContext<
  { name: (title: string | null) => void; line: HTMLDivElement | null } | undefined
>(undefined)

/**
 * Proposed, see the review on #3650: a page that shows one thing, such as a saved record or a task, is headed by that
 * thing's name, in place of the page's title, and the line under the heading, in `textMuted`, gives its context: what
 * kind of thing it is, or what it is about and when it is due. Outside a page layout it draws nothing.
 */
export function PageHeading({ title, line }: { title: string; line?: ReactNode }) {
  const slot = useContext(PageHeadingSlot)
  const name = slot?.name
  useLayoutEffect(() => {
    name?.(title)
    return () => name?.(null)
  }, [name, title])
  return line !== undefined && slot?.line ? createPortal(line, slot.line) : null
}
