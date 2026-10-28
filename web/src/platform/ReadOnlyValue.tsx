import type { ReactNode } from 'react'

/** The value of a read-only field on a form: plain text with an input's height, under its label. */
export function ReadOnlyValue({ field, children }: { field: string; children: ReactNode }) {
  return (
    <div id={field} data-testid={`field-value-${field}`} style={{ minHeight: 32, lineHeight: '22px' }}>
      {children}
    </div>
  )
}
