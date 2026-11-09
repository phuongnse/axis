import { CloseOutlined } from '@ant-design/icons'
import { Flex, Typography } from 'antd'
import type { ReactNode } from 'react'
import { AxisButton } from '../platform/AxisButton'
import { ShellAside } from '../platform/shellAside'
import { useDesignTokens } from '../platform/themeMode'

/**
 * Proposed: one side panel frame for Studio, which the Problems panel and the request panel share. It is drawn in the
 * shell's right column through `ShellAside`, `sidePanel` wide, from under the header to the bottom of the window, so
 * no footer pushes it and it stays while the page scrolls. Its title row is as high as the band, so their lines meet.
 * The title, the close button and `top`, such as a form, stay, and only `children`, its list, scrolls, with no scroll
 * passed on to the page at its end. It is not modal, and Studio shows one side panel at a time: opening one closes
 * the other. See docs/reference/actions-and-controls.md#opening-things and the scrolling rules of the review on #3263.
 */
export function StudioSidePanel({
  id,
  title,
  closeKey,
  onClose,
  testId,
  closeTestId,
  top,
  children,
}: {
  id: string
  title: string
  /** The text key of the close button's name. */
  closeKey: string
  onClose: () => void
  testId?: string
  closeTestId?: string
  /** What stays above the list, such as a form. */
  top?: ReactNode
  /** The list, the one part that scrolls. */
  children: ReactNode
}) {
  const { colour, size, spacing } = useDesignTokens()

  return (
    <ShellAside>
      <aside
        id={id}
        data-testid={testId}
        aria-label={title}
        style={{ display: 'flex', flexDirection: 'column', flex: '1 1 auto', minHeight: 0 }}
      >
        <Flex
          justify="space-between"
          align="center"
          style={{
            flex: 'none',
            height: size.bar,
            paddingInline: spacing.md,
            borderBottom: `1px solid ${colour.borderSubtle}`,
          }}
        >
          <Typography.Title level={5} style={{ margin: 0 }}>
            {title}
          </Typography.Title>
          <AxisButton
            role="icon"
            nameKey={closeKey}
            icon={<CloseOutlined />}
            data-testid={closeTestId}
            onClick={onClose}
          />
        </Flex>
        {top !== undefined && <div style={{ flex: 'none', padding: `${spacing.md}px ${spacing.md}px 0` }}>{top}</div>}
        <div
          style={{
            flex: '1 1 auto',
            minHeight: 0,
            overflowY: 'auto',
            overscrollBehavior: 'contain',
            padding: `${spacing.xs}px ${spacing.md}px ${spacing.md}px`,
          }}
        >
          {children}
        </div>
      </aside>
    </ShellAside>
  )
}
