import { CloseOutlined } from '@ant-design/icons'
import { Flex, Typography } from 'antd'
import type { ReactNode } from 'react'
import { AxisButton } from '../platform/AxisButton'
import { ShellAside } from '../platform/shellAside'
import { useDesignTokens } from '../platform/themeMode'

/**
 * Studio's one side panel frame, drawn in the shell's right column with `ShellAside`, see
 * docs/reference/shell.md#layout. It is a titled panel that is not modal, with a close button named by `closeNameKey`.
 * The head stays in place, and the body, which holds `children`, scrolls on its own. `testId` and `closeTestId` mark
 * the panel and its close button, and `onClose` runs from the close button.
 */
export function StudioSidePanel({
  id,
  title,
  closeNameKey,
  onClose,
  testId,
  closeTestId,
  children,
}: {
  id: string
  title: string
  closeNameKey: string
  onClose: () => void
  testId: string
  closeTestId: string
  children: ReactNode
}) {
  const { size, spacing } = useDesignTokens()
  return (
    <ShellAside>
      <aside
        id={id}
        data-testid={testId}
        aria-label={title}
        style={{
          display: 'flex',
          flexDirection: 'column',
          gap: spacing.sm,
          height: `calc(var(--shell-min-height, 100vh) - ${size.bar}px)`,
          padding: spacing.sm,
        }}
      >
        <Flex justify="space-between" align="center" style={{ flex: 'none' }}>
          <Typography.Title level={5} style={{ margin: 0 }}>
            {title}
          </Typography.Title>
          <AxisButton
            role="icon"
            nameKey={closeNameKey}
            icon={<CloseOutlined />}
            data-testid={closeTestId}
            onClick={onClose}
          />
        </Flex>
        {/* A scroll area stops at its end, so the page does not scroll, see docs/reference/actions-and-controls.md#scroll-areas. */}
        <div style={{ flex: 1, minHeight: 0, overflowY: 'auto', overscrollBehavior: 'contain' }}>{children}</div>
      </aside>
    </ShellAside>
  )
}
