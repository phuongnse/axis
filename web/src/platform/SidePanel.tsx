import { CloseOutlined } from '@ant-design/icons'
import { Flex, Typography } from 'antd'
import { useRef, type ReactNode } from 'react'
import { AxisButton } from './AxisButton'
import { ResizeHandle } from './ResizeHandle'
import { ShellAside } from './shellAside'
import { useSidePanelWidth } from './sidePanelWidth'
import { useDesignTokens } from './themeMode'
import { useNarrowWidth } from './useNarrowWidth'

/** What a panel the person opens has: a close button named by `closeNameKey`. The process designer's step panel has none. */
type Closing =
  | { onClose?: undefined; closeNameKey?: undefined; closeTestId?: undefined }
  | { onClose: () => void; closeNameKey: string; closeTestId?: string }

/**
 * The one side panel frame of every page, drawn in the shell's right column with `ShellAside`, see
 * docs/reference/shell.md#layout. It is a titled panel that is not modal. Its handle is a `ResizeHandle` on its start edge. It sets
 * the width from `sidePanel` to half the window, and the browser keeps that width for every side panel, see
 * `useSidePanelWidth`. At narrow width the panel takes the window's width and has no handle, see docs/reference/frontend.md#narrow-width. A `status`, such as a `StatusTag`, shows
 * beside the title, and a `line` shows under it in `textMuted`. The head stays in place, and the body, which holds `children`, scrolls on its own and keeps room
 * for its scrollbar. `testId` marks the panel. With `onClose` the panel has a close button named by `closeNameKey` and
 * marked by `closeTestId`, and it counts as opened by the person, so it shows over a panel with no close button.
 */
export function SidePanel({
  id,
  title,
  status,
  line,
  testId,
  onClose,
  closeNameKey,
  closeTestId,
  children,
}: {
  id: string
  title: string
  status?: ReactNode
  line?: ReactNode
  testId: string
  children: ReactNode
} & Closing) {
  const { colour, size, spacing } = useDesignTokens()
  const { width, min, max, set } = useSidePanelWidth()
  const narrow = useNarrowWidth()
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
          width: narrow ? '100%' : width,
          height: `calc(var(--shell-min-height, 100vh) - ${size.bar}px)`,
          padding: spacing.sm,
        }}
      >
        {!narrow && (
          <ResizeHandle
            nameKey="shell.sidePanel.resize"
            orientation="vertical"
            edge="start"
            value={width}
            min={min}
            max={max}
            onChange={set}
            region={panel}
          />
        )}
        <Flex justify="space-between" align="center" gap={spacing.sm} style={{ flex: 'none' }}>
          <Flex vertical style={{ minWidth: 0 }}>
            <Flex align="center" gap={spacing.sm}>
              <Typography.Title level={5} style={{ margin: 0 }}>
                {title}
              </Typography.Title>
              {status}
            </Flex>
            {line !== undefined && <span style={{ color: colour.textMuted }}>{line}</span>}
          </Flex>
          {onClose !== undefined && (
            <AxisButton
              role="subtle"
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
