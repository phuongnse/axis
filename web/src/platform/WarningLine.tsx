import type { ReactNode } from 'react'
import { useDesignTokens } from './themeMode'

/**
 * A line in `warningText` under a control, for a value that the control keeps shown but that will not pass, see
 * docs/reference/actions-and-controls.md#fields.
 */
export function WarningLine({ testId, children }: { testId: string; children: ReactNode }) {
  const { colour } = useDesignTokens()
  return (
    <div data-testid={testId} style={{ color: colour.warningText }}>
      {children}
    </div>
  )
}
