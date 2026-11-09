import { CloseOutlined } from '@ant-design/icons'
import { Flex, Typography } from 'antd'
import type { ReactNode } from 'react'
import { AxisButton } from '../platform/AxisButton'
import { useDesignTokens } from '../platform/themeMode'

/** The width of a side panel in px. */
const panelWidth = 320

/**
 * Proposed: one side panel frame for Studio, which the Problems panel and the request panel share. A column beside
 * the page, under the header and the band, that is not modal, so the page stays usable while it is open. It has a
 * title and a close button, and its content scrolls inside it. Studio shows one side panel at a time: opening one
 * closes the other. See docs/reference/actions-and-controls.md#opening-things.
 */
export function StudioSidePanel({
  id,
  title,
  closeKey,
  onClose,
  testId,
  closeTestId,
  children,
}: {
  id: string
  title: string
  /** The text key of the close button's name. */
  closeKey: string
  onClose: () => void
  testId?: string
  closeTestId?: string
  children: ReactNode
}) {
  const { colour, size, spacing } = useDesignTokens()
  const top = size.bar * 2 + spacing.md

  return (
    <aside
      id={id}
      data-testid={testId}
      aria-label={title}
      style={{
        position: 'sticky',
        top,
        alignSelf: 'flex-start',
        flex: 'none',
        width: panelWidth,
        maxHeight: `calc(100vh - ${top + spacing.md}px)`,
        overflowY: 'auto',
        padding: spacing.sm,
        background: colour.surface,
        border: `1px solid ${colour.borderSubtle}`,
      }}
    >
      <Flex vertical gap="small">
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
        {children}
      </Flex>
    </aside>
  )
}
