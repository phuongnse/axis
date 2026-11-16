import { Flex, Typography } from 'antd'
import { Children, useId, useState, type ReactNode } from 'react'
import { useDesignTokens } from './themeMode'

/**
 * The column of a Studio page's parts, one above another with a `divider` rule before every part but the first,
 * see docs/reference/studio-resource-form.md#sections and docs/reference/shell.md#layout. The rule is its own element
 * between parts, so a part has no border and a column nested in another one still draws no rule above its first part.
 */
export function SectionParts({ children }: { children: ReactNode }) {
  const { colour, spacing } = useDesignTokens()
  return (
    <Flex vertical gap={spacing.md}>
      {Children.toArray(children).flatMap((part, index) =>
        index === 0
          ? [part]
          : [<div key={`rule-${index}`} role="separator" style={{ borderTop: `1px solid ${colour.divider}` }} />, part],
      )}
    </Flex>
  )
}

/**
 * A part of a Studio page, see docs/reference/studio-resource-form.md#sections: an optional heading, its optional
 * line and a slot for an action on the right of the heading, with no panel. A part with no heading is its fields alone.
 */
export function SectionPart({
  testName,
  heading,
  action,
  children,
}: {
  /** In `data-testid="studio-section-<testName>"`. */
  testName: string
  /** The heading, and its line when it has one. None for the last section of unnamed properties. */
  heading?: { title: string; line?: string }
  /** On the right of the heading, such as a list's Add. A function child draws the slot in its place. */
  action?: ReactNode
  /** A function child gets a slot on the right of the heading, for a list that draws its own Add… into it. */
  children: ReactNode | ((actionSlot: HTMLElement | null) => ReactNode)
}) {
  const { spacing } = useDesignTokens()
  const headingId = useId()
  const [slot, setSlot] = useState<HTMLElement | null>(null)
  const body = typeof children === 'function' ? children(slot) : children
  if (heading === undefined) {
    return <div data-testid={`studio-section-${testName}`}>{body}</div>
  }
  return (
    <section aria-labelledby={headingId} data-testid={`studio-section-${testName}`}>
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
