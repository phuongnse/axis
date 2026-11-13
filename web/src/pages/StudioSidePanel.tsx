import { CloseOutlined } from '@ant-design/icons'
import { Flex, Typography } from 'antd'
import { useLayoutEffect, useRef, useState, type ReactNode, type RefObject } from 'react'
import { AxisButton } from '../platform/AxisButton'
import { ShellAside } from '../platform/shellAside'
import { useDesignTokens } from '../platform/themeMode'

/**
 * Proposed: one side panel frame for Studio, which the Problems panel and the request panel share. It is drawn in the
 * shell's right column through `ShellAside`, `sidePanel` wide, from under the header to the bottom of the window, so
 * no footer pushes it and it stays while the page scrolls. At narrow width it takes the window's width, over the page. Its title row is as high as the band, so their lines meet.
 * The title, the close button and `top`, such as a form, stay, and only `children`, its list, scrolls, with no scroll
 * passed on to the page at its end. Proposed: while the list fits, a wheel over the panel scrolls the page, as it does
 * over the sidebar, see the review on #3263. It is not modal, and Studio shows one side panel at a time: opening one closes
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
  const list = useRef<HTMLDivElement>(null)
  const content = useRef<HTMLDivElement>(null)
  const overflows = useOverflow(list, content)

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
          ref={list}
          data-testid="studio-side-panel-list"
          style={{
            flex: '1 1 auto',
            minHeight: 0,
            overflowY: 'auto',
            // A list that fits is no scroll of its own, so it must not hold the page's scroll.
            overscrollBehavior: overflows ? 'contain' : 'auto',
            padding: `${spacing.xs}px ${spacing.md}px ${spacing.md}px`,
          }}
        >
          <div ref={content}>{children}</div>
        </div>
      </aside>
    </ShellAside>
  )
}

/** Whether `content` is taller than the `box` that scrolls it, measured again when either changes size. */
function useOverflow(box: RefObject<HTMLElement | null>, content: RefObject<HTMLElement | null>): boolean {
  const [overflows, setOverflows] = useState(false)
  useLayoutEffect(() => {
    const outer = box.current
    const inner = content.current
    if (outer === null || inner === null || typeof ResizeObserver === 'undefined') {
      return
    }
    const measure = () => setOverflows(outer.scrollHeight > outer.clientHeight)
    const observer = new ResizeObserver(measure)
    observer.observe(outer)
    observer.observe(inner)
    measure()
    return () => observer.disconnect()
  }, [box, content])
  return overflows
}
