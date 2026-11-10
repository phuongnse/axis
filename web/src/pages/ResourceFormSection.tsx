import { Flex, Typography } from 'antd'
import { Children, Fragment, isValidElement, useId, useState, type ReactNode } from 'react'
import { useDesignTokens } from '../platform/themeMode'

/** The column that spaces a resource form's sections apart, see docs/reference/studio-resource-form.md#sections. */
export function ResourceFormSections({ children }: { children: ReactNode }) {
  const { spacing } = useDesignTokens()
  return (
    <Flex vertical gap={spacing.md}>
      {children}
    </Flex>
  )
}

/**
 * A section of a resource form, see docs/reference/studio-resource-form.md#sections: a panel with an optional heading,
 * its line and a slot for an action on the right of the heading. A section with no heading is the panel alone.
 * Proposed: every Studio page draws its sections with it, so the page's canvas holds only its heading, its messages
 * and its actions, and a heading's line is optional; see the review on #3263.
 */
export function ResourceFormSection({
  testName,
  heading,
  action,
  fill = false,
  children,
}: {
  /** In `data-testid="studio-section-<testName>"`. */
  testName: string
  /** The heading and its line. None for the last section of unnamed properties. */
  heading?: { title: string; line?: string }
  /** On the right of the heading, such as a list's Add. A function child draws the slot in its place. */
  action?: ReactNode
  /**
   * Proposed: the page's one panel reaches at least the window's foot, as the sidebar and a side panel do, while each
   * column up to the shell's content grows to fill; see the review on #3263.
   */
  fill?: boolean
  /** A function child gets a slot on the right of the heading, for a list that draws its own Add… into it. */
  children: ReactNode | ((actionSlot: HTMLElement | null) => ReactNode)
}) {
  const { colour, radius, spacing } = useDesignTokens()
  const headingId = useId()
  const [slot, setSlot] = useState<HTMLElement | null>(null)
  const style = {
    background: colour.surface,
    border: `1px solid ${colour.borderSubtle}`,
    borderRadius: radius.base,
    padding: spacing.md,
    ...(fill ? { flex: '1 0 auto' } : {}),
  }
  const body = typeof children === 'function' ? children(slot) : children
  if (heading === undefined) {
    return (
      <div data-testid={`studio-section-${testName}`} style={style}>
        {body}
      </div>
    )
  }
  return (
    <section aria-labelledby={headingId} data-testid={`studio-section-${testName}`} style={style}>
      <Flex justify="space-between" align="flex-start" gap="small" style={{ marginBottom: spacing.sm }}>
        <div>
          <Typography.Title id={headingId} level={5} style={{ margin: 0 }}>
            {heading.title}
          </Typography.Title>
          {heading.line !== undefined && <Typography.Text type="secondary">{heading.line}</Typography.Text>}
        </div>
        {typeof children === 'function' ? <div ref={setSlot} /> : action}
      </Flex>
      {body}
    </section>
  )
}

/**
 * Proposed: the parts of one panel, for a page that reads as one document, such as an AI request's page: each
 * `SectionPart` has its heading, and a `borderSubtle` rule sits above every part but the first. Anything between parts,
 * such as a message or a row of actions, belongs to the part above it. See the review on #3263.
 */
export function SectionParts({ children }: { children: ReactNode }) {
  const { colour, spacing } = useDesignTokens()
  const items = Children.toArray(children)
  const first = items.findIndex((child) => isValidElement(child) && child.type === SectionPart)
  return (
    <Flex vertical gap={spacing.md}>
      {items.map((child, index) => (
        <Fragment key={index}>
          {index > first && isValidElement(child) && child.type === SectionPart && (
            <div role="separator" style={{ borderTop: `1px solid ${colour.borderSubtle}` }} />
          )}
          {child}
        </Fragment>
      ))}
    </Flex>
  )
}

/** Proposed: one part of `SectionParts`: a level-5 heading, then its content. */
export function SectionPart({ testName, title, children }: { testName: string; title: string; children: ReactNode }) {
  const { spacing } = useDesignTokens()
  const headingId = useId()
  return (
    <section aria-labelledby={headingId} data-testid={`studio-part-${testName}`}>
      <Flex vertical gap={spacing.sm}>
        <Typography.Title id={headingId} level={5} style={{ margin: 0 }}>
          {title}
        </Typography.Title>
        {children}
      </Flex>
    </section>
  )
}
