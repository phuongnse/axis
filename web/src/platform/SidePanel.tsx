import { CloseOutlined } from '@ant-design/icons'
import { Flex, Typography } from 'antd'
import { useRef, type KeyboardEvent, type PointerEvent, type ReactNode } from 'react'
import { AxisButton } from './AxisButton'
import { ShellAside } from './shellAside'
import { useSidePanelWidth } from './sidePanelWidth'
import { useText } from './texts'
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
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const { width, min, max, set } = useSidePanelWidth()
  const panel = useRef<HTMLElement>(null)

  const onKeyDown = (event: KeyboardEvent) => {
    // The handle is on the panel's start edge, so the arrow that moves the edge away from the page widens it.
    const target = { ArrowLeft: width + spacing.md, ArrowRight: width - spacing.md, Home: min, End: max }[event.key]
    if (target !== undefined) {
      event.preventDefault()
      set(target)
    }
  }
  const onPointerDown = (event: PointerEvent<HTMLDivElement>) => {
    event.currentTarget.setPointerCapture(event.pointerId)
  }
  const onPointerMove = (event: PointerEvent<HTMLDivElement>) => {
    if (event.currentTarget.hasPointerCapture(event.pointerId) && panel.current) {
      set(panel.current.getBoundingClientRect().right - event.clientX)
    }
  }

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
          // Proposed, #4934: as tall as the shell's column under the header, which does not scroll.
          height: '100%',
          boxSizing: 'border-box',
          padding: spacing.sm,
        }}
      >
        <style>{`[data-side-panel-handle]:focus-visible { outline: 2px solid ${colour.focusRing}; outline-offset: -2px; }`}</style>
        <div
          role="separator"
          aria-orientation="vertical"
          aria-label={t('shell.sidePanel.resize')}
          aria-valuemin={min}
          aria-valuemax={max}
          aria-valuenow={width}
          tabIndex={0}
          data-side-panel-handle
          onKeyDown={onKeyDown}
          onPointerDown={onPointerDown}
          onPointerMove={onPointerMove}
          style={{
            position: 'absolute',
            insetBlock: 0,
            insetInlineStart: 0,
            width: spacing.xs,
            cursor: 'col-resize',
            touchAction: 'none',
          }}
        />
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
