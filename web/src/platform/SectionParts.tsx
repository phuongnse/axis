import { Flex, Typography } from 'antd'
import { Children, createContext, isValidElement, useContext, useId, useState, type ReactNode } from 'react'
import { AxisButton } from './AxisButton'
import { FoldIcon } from './FoldIcon'
import { useDesignTokens } from './themeMode'

/** Whether the part that reads it draws a `divider` rule above itself. */
const RuleAbove = createContext(false)

/**
 * The column of a page's parts or a Studio section's parts, one above another, see docs/reference/shell.md#layout and
 * docs/reference/studio-resource-form.md#sections. Each part draws the `divider` rule above itself, so a child that renders
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
 * heading is its fields alone. It draws the `divider` rule above itself when `SectionParts` says a part comes before it.
 */
export function SectionPart({
  testName,
  testId,
  heading,
  action,
  folds = false,
  startsClosed = false,
  children,
}: SectionPartName & {
  /** The heading, and its line when it has one. None for the last section of unnamed properties. */
  heading?: { title: string; line?: string }
  /** On the right of the heading, such as a list's Add. A function child draws the slot in its place. */
  action?: ReactNode
  /**
   * Proposed, see the review on #3650: a part with a heading that folds has a fold button before its heading, which
   * shows and hides its content, and starts closed when `startsClosed`, as a `section` widget marked `collapsed` does.
   */
  folds?: boolean
  startsClosed?: boolean
  /** A function child gets a slot on the right of the heading, for a list that draws its own Add… into it. */
  children: ReactNode | ((actionSlot: HTMLElement | null) => ReactNode)
}) {
  const { spacing } = useDesignTokens()
  const ruled = useContext(RuleAbove)
  const headingId = useId()
  const contentId = useId()
  const [open, setOpen] = useState(!(folds && startsClosed))
  const [slot, setSlot] = useState<HTMLElement | null>(null)
  const dataTestId = testId ?? `studio-section-${testName}`
  const body = (
    <RuleAbove.Provider value={false}>{typeof children === 'function' ? children(slot) : children}</RuleAbove.Provider>
  )
  const rule = ruled && <PartRule />
  if (heading === undefined) {
    return (
      <>
        {rule}
        <div data-testid={dataTestId}>{body}</div>
      </>
    )
  }
  return (
    <>
      {rule}
      <section aria-labelledby={headingId} data-testid={dataTestId}>
        <Flex justify="space-between" align="flex-start" gap="small" style={{ marginBottom: spacing.sm }}>
          <Flex align="flex-start" gap={spacing.xs}>
            {folds && (
              <AxisButton
                role="icon"
                nameKey={open ? 'shell.section.hide' : 'shell.section.show'}
                nameSuffix={heading.title}
                icon={<FoldIcon open={open} />}
                aria-expanded={open}
                aria-controls={contentId}
                onClick={() => setOpen(!open)}
              />
            )}
            <div>
              {/* Level two for the outline, at the 16px and 24px that a level-five heading has. */}
              <Typography.Title id={headingId} level={2} style={{ margin: 0, fontSize: 16, lineHeight: '24px' }}>
                {heading.title}
              </Typography.Title>
              {heading.line !== undefined && <Typography.Text type="secondary">{heading.line}</Typography.Text>}
            </div>
          </Flex>
          {typeof children === 'function' ? <div ref={setSlot} /> : action}
        </Flex>
        {folds ? (
          <div id={contentId} hidden={!open}>
            {open && body}
          </div>
        ) : (
          body
        )}
      </section>
    </>
  )
}

/**
 * Proposed, see the review on #3650: the rule above a part that follows another, in `borderSubtle`, whose light value
 * the review darkened so a rule on `surface` stands out as much as in dark mode. `SectionPart` draws it; a page that
 * lays its parts out itself, as a form's groups are, draws it through this, so the rule has one look.
 */
export function PartRule() {
  const { colour } = useDesignTokens()
  return <div role="separator" style={{ borderTop: `1px solid ${colour.borderSubtle}` }} />
}
