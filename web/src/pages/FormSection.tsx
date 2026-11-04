import { Flex, Typography } from 'antd'
import { useState, type ReactNode } from 'react'
import { useDesignTokens } from '../platform/themeMode'
import { SectionActionSlot } from './schemaFormSections'

/**
 * Proposed: a section of a Studio form, as the application's Settings draws it: a panel on `surface` with its title, a
 * line that says what it holds, and its main action, such as Add module…, on the right of its heading. Its fields are
 * spacing-md apart, one to a row. A list inside it can put its action in the heading through `SectionActionSlot`.
 */
export function SectionPanel({
  title,
  description,
  action,
  testId,
  children,
}: {
  title: string | null
  description: string | null
  action?: ReactNode
  testId: string
  children: ReactNode
}) {
  const { colour, radius, spacing } = useDesignTokens()
  const [slot, setSlot] = useState<HTMLDivElement | null>(null)
  return (
    <section
      data-testid={`studio-form-section-${testId || 'more'}`}
      style={{
        background: colour.surface,
        border: `1px solid ${colour.borderSubtle}`,
        borderRadius: radius.base,
        padding: spacing.lg,
      }}
    >
      <Flex vertical gap={spacing.md}>
        {(title || description) && (
          <Flex align="center" gap={spacing.md}>
            <Flex vertical gap={4} style={{ flex: 1 }}>
              {title && (
                <Typography.Title level={5} style={{ margin: 0 }}>
                  {title}
                </Typography.Title>
              )}
              {description && <Typography.Text style={{ color: colour.textMuted }}>{description}</Typography.Text>}
            </Flex>
            {action}
            {/* The slot takes no box of its own, so an empty one adds no gap after the action. */}
            <div ref={setSlot} style={{ display: 'contents' }} />
          </Flex>
        )}
        <SectionActionSlot.Provider value={slot}>{children}</SectionActionSlot.Provider>
      </Flex>
    </section>
  )
}
