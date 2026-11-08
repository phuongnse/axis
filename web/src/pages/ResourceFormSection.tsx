import { Flex, Typography } from 'antd'
import { useId, useState, type ReactNode } from 'react'
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
 */
export function ResourceFormSection({
  testName,
  heading,
  action,
  children,
}: {
  /** In `data-testid="studio-section-<testName>"`. */
  testName: string
  /** The heading and its line. None for the last section of unnamed properties. */
  heading?: { title: string; line: string }
  /** On the right of the heading, such as a list's Add. A function child draws the slot in its place. */
  action?: ReactNode
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
          <Typography.Text type="secondary">{heading.line}</Typography.Text>
        </div>
        {typeof children === 'function' ? <div ref={setSlot} /> : action}
      </Flex>
      {body}
    </section>
  )
}
