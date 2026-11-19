import { CloseOutlined } from '@ant-design/icons'
import { Flex, Typography } from 'antd'
import { useRef, type ReactNode } from 'react'
import { AxisButton } from './AxisButton'
import { ResizeHandle } from './ResizeHandle'
import { ShellAside } from './shellAside'
import { useSidePanelWidth } from './sidePanelWidth'
import { useDesignTokens } from './themeMode'

/** What a panel the person opens has: a close button named by `closeNameKey`. The process designer's step panel has none. */
type Closing =
  | { onClose?: undefined; closeNameKey?: undefined; closeTestId?: undefined }
  | { onClose: () => void; closeNameKey: string; closeTestId?: string }

/**
 * The one side panel frame of every page, drawn in the shell's right column with `ShellAside`, see
 * docs/reference/shell.md#layout. It is a titled panel that is not modal. A handle on its start edge, a `separator`
 * that takes the arrow keys, Home and End and a drag, sets its width from `sidePanel` to half the window, and the
 * browser keeps that width for every side panel, see `useSidePanelWidth`. A `status`, such as a `StatusTag`, shows
 * beside the title. The head stays in place, and the body, which holds `children`, scrolls on its own and keeps room
 * for its scrollbar. `testId` marks the panel. With `onClose` the panel has a close button named by `closeNameKey` and
 * marked by `closeTestId`, and it counts as opened by the person, so it shows over a panel with no close button.
 */
export function SidePanel({
  id,
  title,
  status,
  testId,
  onClose,
  closeNameKey,
  closeTestId,
  children,
}: {
  id: string
  title: string
  status?: ReactNode
  testId: string
  children: ReactNode
} & Closing) {
  const { size, spacing } = useDesignTokens()
  const { width, min, max, set } = useSidePanelWidth()
  const panel = useRef<HTMLElement>(null)

  return (
    <ShellAside closable={onClose !== undefined}>
      <aside
        ref={panel}
        id={id}
        data-testid={testId}
        aria-label={title}
        style={{
          position: 'relative',
          display: 'flex',
          flexDirection: 'column',
          gap: spacing.sm,
          width,
          height: `calc(var(--shell-min-height, 100vh) - ${size.bar}px)`,
          padding: spacing.sm,
        }}
      >
        <ResizeHandle
          edge="start"
          size={width}
          min={min}
          max={max}
          measure={(event) => (panel.current ? panel.current.getBoundingClientRect().right - event.clientX : width)}
          onChange={set}
        />
        <Flex justify="space-between" align="center" gap={spacing.sm} style={{ flex: 'none' }}>
          <Flex align="center" gap={spacing.sm} style={{ minWidth: 0 }}>
            <Typography.Title level={5} style={{ margin: 0 }}>
              {title}
            </Typography.Title>
            {status}
          </Flex>
          {onClose !== undefined && (
            <AxisButton
              role="icon"
              nameKey={closeNameKey}
              icon={<CloseOutlined />}
              data-testid={closeTestId}
              onClick={onClose}
            />
          )}
        </Flex>
        {/* A scroll area stops at its end, so the page does not scroll, see docs/reference/actions-and-controls.md#scroll-areas. */}
        <div
          style={{
            flex: 1,
            minHeight: 0,
            overflowY: 'auto',
            overscrollBehavior: 'contain',
            scrollbarGutter: 'stable',
          }}
        >
          {children}
        </div>
      </aside>
    </ShellAside>
  )
}
