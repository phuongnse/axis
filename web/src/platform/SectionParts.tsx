import { Flex, Typography } from 'antd'
import { Children, createContext, isValidElement, useContext, useId, useState, type ReactNode } from 'react'
import { AxisActionRow, AxisButton } from './AxisButton'
import { Chevron } from './Chevron'
import { useDesignTokens } from './themeMode'

/** Whether the part that reads it draws a `borderSubtle` rule above itself. */
const RuleAbove = createContext(false)

/** A boundary a part does not draw its rule across, such as a tile: a part inside it draws no rule above itself. */
export function PartBoundary({ children }: { children: ReactNode }) {
  return <RuleAbove.Provider value={false}>{children}</RuleAbove.Provider>
}

/**
 * The column of a page's parts or a Studio section's parts, one above another, see docs/reference/shell.md#layout and
 * docs/reference/studio-resource-form.md#sections. Each part draws the `borderSubtle` rule above itself, so a child that renders
 * nothing leaves no rule, and a column nested after another part still draws a rule above its first part.
 */
export function SectionParts({ children }: { children: ReactNode }) {
  const { spacing } = useDesignTokens()
  const outer = useContext(RuleAbove)
  return (
    <Flex vertical gap={spacing.md}>
      {Children.toArray(children).map((part, index) => (
        <RuleAbove.Provider
          key={isValidElement(part) && part.key != null ? part.key : index}
          value={index > 0 || outer}
        >
          {part}
        </RuleAbove.Provider>
      ))}
    </Flex>
  )
}

/**
 * The test id of a part, either the `testName` of `studio-section-<testName>` or a whole `testId` for a part that keeps
 * the test id it had as a card. One of the two is required.
 */
type SectionPartName = { testName: string; testId?: undefined } | { testId: string; testName?: undefined }

/**
 * A part of a page or a Studio section, see docs/reference/shell.md#layout and docs/reference/studio-resource-form.md#sections:
 * an optional heading, its optional line and a slot for an action on the right of the heading, with no panel. A part with no
 * heading is its fields alone. It draws the `borderSubtle` rule above itself when `SectionParts` says a part comes before it.
 * Its action is drawn through `AxisActionRow`, so a `main`, `secondary` or `destructive` button is a text button, see
 * docs/reference/shell.md#layout ('A title row's actions are light').
 */
export function SectionPart({
  testName,
  testId,
  heading,
  fold,
  action,
  busy,
  children,
}: SectionPartName & {
  /** The heading, and its line when it has one. None for the last section of unnamed properties. */
  heading?: { title: ReactNode; line?: string }
  /**
   * Makes the part fold, for a section marked `collapsed`, see docs/reference/container-widgets.md#sections: a fold button
   * before the heading, named by `title`, and a body that is drawn only while `open`. Needs a `heading`.
   */
  fold?: { open: boolean; onToggle: () => void; title: string }
  /**
   * On the right of the heading, such as a list's Add, drawn through `AxisActionRow`. A function child draws the slot in
   * its place, and draws its own action through `AxisActionRow`.
   */
  action?: ReactNode
  /** Sets `aria-busy` on the part while its body loads. */
  busy?: boolean
  /** A function child gets a slot on the right of the heading, for a list that draws its own Add… into it. */
  children: ReactNode | ((actionSlot: HTMLElement | null) => ReactNode)
}) {
  const { colour, spacing } = useDesignTokens()
  const ruled = useContext(RuleAbove)
  const headingId = useId()
  const bodyId = useId()
  const [slot, setSlot] = useState<HTMLElement | null>(null)
  const dataTestId = testId ?? `studio-section-${testName}`
  const drawn = (
    <RuleAbove.Provider value={false}>{typeof children === 'function' ? children(slot) : children}</RuleAbove.Provider>
  )
  // A closed fold draws none of its body, so what is inside it loads nothing.
  const body = fold ? <div id={bodyId}>{fold.open && drawn}</div> : drawn
  const headingBlock = (
    <div>
      {/* Level two for the outline, at the 16px and 24px that a level-five heading has. */}
      <Typography.Title id={headingId} level={2} style={{ margin: 0, fontSize: 16, lineHeight: '24px' }}>
        {heading?.title}
      </Typography.Title>
      {heading?.line !== undefined && <Typography.Text type="secondary">{heading.line}</Typography.Text>}
    </div>
  )
  const rule = ruled && <div role="separator" style={{ borderTop: `1px solid ${colour.borderSubtle}` }} />
  if (heading === undefined) {
    return (
      <>
        {rule}
        <div data-testid={dataTestId} aria-busy={busy}>
          {body}
        </div>
      </>
    )
  }
  return (
    <>
      {rule}
      <section aria-labelledby={headingId} data-testid={dataTestId} aria-busy={busy}>
        <Flex justify="space-between" align="flex-start" gap="small" style={{ marginBottom: spacing.sm }}>
          {fold ? (
            <Flex align="center" gap="small">
              {/* Outside the heading, so the region's name from the heading holds no button name. */}
              <AxisButton
                role="icon"
                icon={<Chevron point={fold.open ? 'down' : 'right'} />}
                nameKey={fold.open ? 'shell.section.hide' : 'shell.section.show'}
                nameValues={{ title: fold.title }}
                aria-expanded={fold.open}
                aria-controls={bodyId}
                onClick={fold.onToggle}
              />
              {headingBlock}
            </Flex>
          ) : (
            headingBlock
          )}
          {typeof children === 'function' ? (
            <div ref={setSlot} />
          ) : (
            action !== null && action !== undefined && <AxisActionRow>{action}</AxisActionRow>
          )}
        </Flex>
        {body}
      </section>
    </>
  )
}
