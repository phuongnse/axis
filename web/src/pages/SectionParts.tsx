import { Divider, Flex, Typography } from 'antd'
import { Children, useId, type ReactNode } from 'react'
import { useDesignTokens } from '../platform/themeMode'

/**
 * The parts of a Studio section, one under the other with a `divider` rule between neighbours and none before the first,
 * see docs/reference/shared-widgets.md#which-widget and docs/reference/shell.md#layout. A child that is `false`, `null`
 * or `undefined` is a part the page leaves out and adds no rule. The widget keeps no space round itself: the page that
 * places it gives the space.
 */
export function SectionParts({ children }: { children: ReactNode }) {
  const { spacing } = useDesignTokens()
  const parts = Children.toArray(children)
  return (
    <Flex vertical gap={spacing.md}>
      {parts.flatMap((part, index) =>
        index === 0 ? [part] : [<Divider key={`rule-${index}`} style={{ margin: 0 }} />, part],
      )}
    </Flex>
  )
}

/** One part of `SectionParts`: a region named by its `heading5` heading, with the part's content under it. */
export function SectionPart({ title, children }: { title: string; children: ReactNode }) {
  const { spacing } = useDesignTokens()
  const headingId = useId()
  return (
    <section aria-labelledby={headingId}>
      <Flex vertical gap={spacing.sm}>
        <Typography.Title level={5} id={headingId} style={{ margin: 0 }}>
          {title}
        </Typography.Title>
        {children}
      </Flex>
    </section>
  )
}
