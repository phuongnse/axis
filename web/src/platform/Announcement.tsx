import type { ReactNode } from 'react'
import { visuallyHidden } from './visuallyHidden'

/** A polite live region hidden from the eye, see docs/reference/shared-widgets.md#which-widget. Render it before its text. */
export function Announcement({ testId, children }: { testId?: string; children?: ReactNode }) {
  return (
    <span role="status" aria-live="polite" style={visuallyHidden} data-testid={testId}>
      {children}
    </span>
  )
}
