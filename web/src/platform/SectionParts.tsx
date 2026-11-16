import { Flex, Typography } from 'antd'
import { useId, useState, type ReactNode } from 'react'
import { AxisButton } from './AxisButton'
import { FoldIcon } from './FoldIcon'
import { useDesignTokens } from './themeMode'

/**
 * Proposed, see the reviews on #3263 and #3650: the rule that sits above every part of a page but the first, in
 * `borderSubtle`, so the parts stand apart on the page area's `surface` with no panel.
 */
export function PartRule() {
  const { colour } = useDesignTokens()
  return <div role="separator" style={{ borderTop: `1px solid ${colour.borderSubtle}` }} />
}

/**
 * Proposed, see the reviews on #3263 and #3650: one part of a page, a region named by its level-5 heading, then its
 * content. A part that `folds` has a fold button before its heading, which shows and hides the content, and starts
 * closed when `startsClosed`. A part that does not fold always shows its content.
 */
export function SectionPart({
  title,
  folds = false,
  startsClosed = false,
  children,
}: {
  title: string
  folds?: boolean
  startsClosed?: boolean
  children: ReactNode
}) {
  const { spacing } = useDesignTokens()
  const headingId = useId()
  const contentId = useId()
  const [open, setOpen] = useState(!(folds && startsClosed))
  return (
    <section aria-labelledby={headingId}>
      <Flex vertical gap={spacing.sm}>
        <Flex align="center" gap={spacing.xs}>
          {folds && (
            <AxisButton
              role="icon"
              nameKey={open ? 'shell.section.hide' : 'shell.section.show'}
              nameSuffix={title}
              icon={<FoldIcon open={open} />}
              aria-expanded={open}
              aria-controls={contentId}
              onClick={() => setOpen(!open)}
            />
          )}
          <Typography.Title id={headingId} level={5} style={{ margin: 0 }}>
            {title}
          </Typography.Title>
        </Flex>
        <div id={contentId} hidden={!open}>
          {open && children}
        </div>
      </Flex>
    </section>
  )
}

/**
 * Proposed, see the review on #3650: names a thing that a page shows inside it, such as the record a task is about, in
 * the shape a page that shows one thing has: its name as a level-2 heading, and its kind alone as the line under it, in
 * `textMuted`. The parts that follow belong to it.
 */
export function ThingHeading({ name, kind }: { name: string; kind: string }) {
  const { colour } = useDesignTokens()
  return (
    <Flex vertical>
      <Typography.Title level={2} style={{ margin: 0, fontSize: 16, lineHeight: '24px' }}>
        {name}
      </Typography.Title>
      <span style={{ color: colour.textMuted }}>{kind}</span>
    </Flex>
  )
}
