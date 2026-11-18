import { CloseOutlined } from '@ant-design/icons'
import { Flex, Typography } from 'antd'
import { useLayoutEffect, useRef, useState, type ReactNode, type RefObject } from 'react'
import { AxisButton } from './AxisButton'
import { ShellAside } from './shellAside'
import { useDesignTokens } from './themeMode'

/**
 * Proposed: the one side panel frame of every page, which Studio's Problems and request panels and a process
 * instance's step run share, see the reviews on #3263 and #3650. It shows details or tools for what is chosen on the
 * page while the page stays in use; a task to finish or cancel is a dialog, and what stands alone is a page. It is
 * drawn in the shell's right column through `ShellAside`, from `sidePanel` wide to half the shell, as the person sets
 * it, from under the header to the bottom of the window, and stays while the page scrolls. At narrow width it takes the
 * window's width, over the page. Its title row is as high as the band. The title, its kind under it, the close button
 * and `top`, such as a form or tabs, stay, and only `children` scrolls, with no scroll passed on to the page at its
 * end; while it fits, a wheel over the panel scrolls the page, as it does over the sidebar. It is not modal, and a
 * page shows one side panel at a time: opening one closes the other.
 */
export function SidePanel({
  id,
  title,
  status,
  kind,
  closeKey,
  onClose,
  testId,
  closeTestId,
  top,
  children,
}: {
  id: string
  title: ReactNode
  /** Proposed, see the review on #3650: the state of what the panel shows, a `StatusTag` beside its title. */
  status?: ReactNode
  /** What the panel shows, under its title in muted text, such as a step's type. */
  kind?: ReactNode
  /** The text key of the close button's name. */
  closeKey: string
  onClose: () => void
  testId?: string
  closeTestId?: string
  /** What stays above the part that scrolls, such as a form or tabs. */
  top?: ReactNode
  /** The part that scrolls. */
  children: ReactNode
}) {
  const { colour, size, spacing } = useDesignTokens()
  const body = useRef<HTMLDivElement>(null)
  const content = useRef<HTMLDivElement>(null)
  const overflows = useOverflow(body, content)

  return (
    <ShellAside>
      <aside
        id={id}
        data-testid={testId}
        aria-labelledby={`${id}-title`}
        style={{ display: 'flex', flexDirection: 'column', flex: '1 1 auto', minHeight: 0 }}
      >
        <Flex
          justify="space-between"
          align="center"
          gap="small"
          style={{
            flex: 'none',
            minHeight: size.bar,
            paddingBlock: kind === undefined ? 0 : spacing.xs,
            paddingInline: spacing.md,
            borderBottom: `1px solid ${colour.borderSubtle}`,
          }}
        >
          <Flex vertical style={{ minWidth: 0 }}>
            <Flex align="center" gap="small" wrap>
              <Typography.Title id={`${id}-title`} level={5} style={{ margin: 0 }}>
                {title}
              </Typography.Title>
              {status}
            </Flex>
            {kind !== undefined && <Typography.Text type="secondary">{kind}</Typography.Text>}
          </Flex>
          <AxisButton
            role="icon"
            nameKey={closeKey}
            icon={<CloseOutlined />}
            data-testid={closeTestId}
            onClick={onClose}
          />
        </Flex>
        {top !== undefined && <div style={{ flex: 'none', padding: `${spacing.sm}px ${spacing.md}px 0` }}>{top}</div>}
        <div
          ref={body}
          data-testid="side-panel-body"
          style={{
            flex: '1 1 auto',
            minHeight: 0,
            overflowY: 'auto',
            // Proposed, see the review on #3650: the body keeps room for its scrollbar while it fits, so its content
            // is as wide when a scrollbar comes, and a table in it moves no column under its row as a row opens.
            scrollbarGutter: 'stable',
            // A body that fits is no scroll of its own, so it must not hold the page's scroll.
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
