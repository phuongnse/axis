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

/** The element of the page's heading that {@link PageActions} draws into: `null` before it is drawn, `undefined` outside a page. */
const ActionsSlot = createContext<HTMLElement | null | undefined>(undefined)

/**
 * The layout of a page, see docs/reference/shell.md#page-header: its title as the page's level-one heading, drawn at the
 * size of a level-four one, with its `tags` beside it and its `line`, the kind of the thing, under it in `textMuted`,
 * and the page's sections stacked below. The page's own actions sit on the heading's right, which any component inside
 * the page fills through {@link PageActions}. With a breadcrumb, the heading sits under it in a band on the surface colour
 * that spans the page area, and the last step is marked as the current page. It replaces the old page container. Pages
 * add no spacing or styling of their own.
 */
export function PageLayout({
  title,
  tags,
  line,
  breadcrumb,
  children,
}: {
  title: string
  /** The state of the thing, as `StatusTag`s beside the title. */
  tags?: ReactNode
  /** The kind of the thing alone, under the title. Empty draws nothing. */
  line?: string | null
  breadcrumb?: readonly BreadcrumbStep[]
  children?: ReactNode
}) {
  const [slot, setSlot] = useState<HTMLElement | null>(null)
  const heading = <PageHeading title={title} tags={tags} line={line} setActionsSlot={setSlot} />

  return (
    <ActionsSlot.Provider value={slot}>
      <Flex vertical gap="middle">
        {breadcrumb ? <HeaderBand steps={breadcrumb}>{heading}</HeaderBand> : heading}
        {children}
      </Flex>
    </ActionsSlot.Provider>
  )
}

/**
 * The page's own actions, drawn on the right of its heading from wherever in the page they are written, so a widget
 * fills it as a page does, see docs/reference/shell.md#page-header. Outside a `PageLayout`, as in a widget preview, they
 * draw where they stand, in a row on the right. With `inPlace` they do so inside a `PageLayout` too, for a widget on a
 * designer's canvas, whose own page heading is not theirs.
 */
export function PageActions({ children, inPlace }: { children: ReactNode; inPlace?: boolean }) {
  const slot = useContext(ActionsSlot)
  if (inPlace || slot === undefined) {
    return (
      <Flex gap="small" justify="end" wrap>
        {children}
      </Flex>
    )
  }
  return slot ? createPortal(children, slot) : null
}

/**
 * The heading of a page that shows one thing, which only {@link PageLayout} draws, so a page never does: the title with its tags beside it, the
 * line under it and the actions' slot on the right.
 */
export function PageHeading({
  title,
  tags,
  line,
  setActionsSlot,
}: {
  title: string
  tags?: ReactNode
  line?: string | null
  setActionsSlot: (slot: HTMLElement | null) => void
}) {
  const { colour } = useDesignTokens()

  return (
    <Flex justify="space-between" align="center" gap="middle" data-testid="page-heading">
      <Flex vertical style={{ minWidth: 0 }}>
        <Flex gap="small" align="center">
          {/* Level one for the outline, at the 20px and 28px that a level-four heading has. */}
          <Typography.Title level={1} style={{ margin: 0, fontSize: 20, lineHeight: '28px' }}>
            {title}
          </Typography.Title>
          {tags}
        </Flex>
        {line ? <Typography.Text style={{ color: colour.textMuted }}>{line}</Typography.Text> : null}
      </Flex>
      <div data-testid="page-actions" ref={setActionsSlot} />
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
