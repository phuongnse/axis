import { CloseOutlined } from '@ant-design/icons'
import { Flex, Typography } from 'antd'
import type { ReactNode } from 'react'
import { AxisButton } from '../platform/AxisButton'
import { useDesignTokens } from '../platform/themeMode'

/**
 * Proposed: one side panel frame for Studio, `sidePanel` wide, which the Problems panel and the request panel share. A
 * column beside the page, under the header and the band, that is not modal, so the page stays usable while it is open.
 * It stays in place while the page scrolls and is as tall as its content, at most the window under the header and the
 * band. Its title, its close button and `top`, such as a form, stay, and only `children`, its list, scrolls, with no
 * scroll passed on to the page at its end. Studio shows one side panel at a time: opening one closes the other. See
 * docs/reference/actions-and-controls.md#opening-things and the scrolling rules of the review on #3263.
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
  const offset = size.bar * 2 + spacing.md

  return (
    <aside
      id={id}
      data-testid={testId}
      aria-label={title}
      style={{
        position: 'sticky',
        top: offset,
        alignSelf: 'flex-start',
        flex: 'none',
        display: 'flex',
        flexDirection: 'column',
        width: size.sidePanel,
        maxHeight: `calc(var(--shell-min-height, 100vh) - ${offset + spacing.md}px)`,
        background: colour.surface,
        border: `1px solid ${colour.borderSubtle}`,
      }}
    >
      <Flex vertical gap="small" style={{ flex: 'none', padding: `${spacing.sm}px ${spacing.sm}px 0` }}>
        <Flex justify="space-between" align="center">
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
        {top}
      </Flex>
      <div
        style={{
          flex: '1 1 auto',
          minHeight: 0,
          overflowY: 'auto',
          overscrollBehavior: 'contain',
          padding: spacing.sm,
        }}
      >
        {children}
      </div>
    </aside>
  )
}
