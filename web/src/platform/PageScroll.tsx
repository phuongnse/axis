import { useEffect, useLayoutEffect, useRef } from 'react'
import { ScrollRestoration, useLocation, useNavigationType } from 'react-router'

/**
 * Going Back or Forward returns the page to where the person left it, and a new address opens at the top, see
 * docs/reference/frontend.md#routing. Pages load their rows after they open, so `KeepPageHeight` comes before
 * `ScrollRestoration`: React runs sibling layout effects in order, so the height is set before the scroll.
 */
export function PageScroll() {
  return (
    <>
      <KeepPageHeight />
      <ScrollRestoration />
    </>
  )
}

/**
 * After Back or Forward, keeps the page as tall as it was at that history entry until its content is that tall again,
 * so the saved scroll position still fits while the rows load. The window scrolls, see docs/reference/shell.md#layout.
 */
function KeepPageHeight() {
  const location = useLocation()
  const navigationType = useNavigationType()
  const heights = useRef(new Map<string, number>())
  const currentKey = useRef(location.key)
  const held = useRef<number | null>(null)

  useEffect(() => {
    const root = document.getElementById('root')
    if (!root) {
      return
    }
    const observer = new ResizeObserver(() => {
      const height = root.offsetHeight
      if (held.current !== null) {
        if (height < held.current) {
          return
        }
        held.current = null
        document.body.style.minHeight = ''
      }
      heights.current.set(currentKey.current, height)
    })
    observer.observe(root)
    return () => {
      observer.disconnect()
      document.body.style.minHeight = ''
    }
  }, [])

  useLayoutEffect(() => {
    currentKey.current = location.key
    const height = navigationType === 'POP' ? heights.current.get(location.key) : undefined
    held.current = height ?? null
    document.body.style.minHeight = height === undefined ? '' : `${height}px`
  }, [location.key, navigationType])

  return null
}
