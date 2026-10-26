import { useEffect, useRef, type ReactNode } from 'react'

/** Submits the form it wraps once, when it mounts, so the form draws the messages its save answers. */
export function SubmittedOnMount({ children }: { children: ReactNode }) {
  const wrapper = useRef<HTMLDivElement>(null)
  const submitted = useRef(false)
  useEffect(() => {
    if (!submitted.current) {
      submitted.current = true
      wrapper.current?.querySelector('form')?.requestSubmit()
    }
  }, [])
  return <div ref={wrapper}>{children}</div>
}
