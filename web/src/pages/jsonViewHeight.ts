import { useEffect, useLayoutEffect, useRef, useState, type RefObject } from 'react'
import { useDesignTokens } from '../platform/themeMode'

/** The least height of the view, so a tiny window still leaves room to read. */
export const minHeight = 160

export interface JsonViewHeightInput {
  windowHeight: number
  /** The top of the view on the page. */
  top: number
  footerHeight: number
  /** The space that sits below the view and above the footer: what follows the view and the content's padding. */
  below: number
  /** The gap kept between the view and the footer. */
  gap: number
  /** The least height of the view. It defaults to `minHeight`. */
  least?: number
}

/** The height that puts the bottom of the view `gap` above the footer, which sits at the foot of the window. */
export function jsonViewHeight({ windowHeight, top, footerHeight, below, gap, least }: JsonViewHeightInput): number {
  return Math.max(windowHeight - footerHeight - gap - top - below, least ?? minHeight)
}

export interface FillOptions {
  /** The element whose bottom edge ends the content that follows, so what follows stays on screen. */
  boundary?: RefObject<HTMLElement | null>
  /**
   * A design-system frame, which holds its own shell: no footer, bar or gap lies below it. `least` is the frame's
   * `frameHeight` and `below` the space under it, the canvas's padding and the frame's borders. See
   * docs/reference/shell.md#design-system-previews.
   */
  frame?: { least: number; below: number }
}

/**
 * The height that fills the window down to the footer for the element in `ref`, while `fill` is on. The one
 * measurement behind `CodeEditor`'s `fill`, the package workspace's empty editor area, see
 * docs/reference/studio-packages.md#a-packages-files, and a design-system frame's height, see
 * docs/reference/shell.md#design-system-previews.
 */
export function useFillHeight(
  ref: RefObject<HTMLElement | null>,
  fill: boolean,
  { boundary, frame }: FillOptions = {},
): number {
  const tokens = useDesignTokens()
  const [height, setHeight] = useState(frame?.least ?? minHeight)
  const gap = tokens.spacing.md
  const fit = () => {
    const host = ref.current
    if (!fill || !host || host.offsetParent === null) {
      return
    }
    const rect = host.getBoundingClientRect()
    if (frame) {
      // The frame's own shell fills it, so the measure is the window's bottom less the frame's `below`.
      const next = jsonViewHeight({
        windowHeight: window.innerHeight,
        top: rect.top,
        footerHeight: 0,
        below: frame.below,
        gap: 0,
        least: frame.least,
      })
      setHeight((current) => (current === next ? current : next))
      return
    }
    const content = host.closest('main')
    const next = jsonViewHeight({
      windowHeight: window.innerHeight,
      top: rect.top + window.scrollY,
      // Ant Design's `Layout.Footer` is a plain `footer` element and has no `contentinfo` role.
      footerHeight: document.querySelector('footer')?.getBoundingClientRect().height ?? 0,
      below:
        (boundary?.current ? Math.max(boundary.current.getBoundingClientRect().bottom - rect.bottom, 0) : 0) +
        (content ? parseFloat(getComputedStyle(content).paddingBottom) || 0 : 0) +
        // The bar slot is under the page, so the element ends above the bar that sticks to the window's foot.
        (document.querySelector('[data-shell-bar-slot]')?.getBoundingClientRect().height ?? 0),
      gap,
    })
    setHeight((current) => (current === next ? current : next))
  }
  const fitRef = useRef(fit)

  useLayoutEffect(() => {
    fitRef.current = fit
    fit()
  })

  useEffect(() => {
    const onResize = () => fitRef.current()
    window.addEventListener('resize', onResize)
    return () => window.removeEventListener('resize', onResize)
  }, [])

  return height
}
