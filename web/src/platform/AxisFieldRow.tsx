import { Children, type ReactNode } from 'react'
import { AxisFieldGroup } from './AxisFieldGroup'
import { useDesignTokens } from './themeMode'
import { useNarrowWidth } from './useNarrowWidth'

/**
 * Two related fields side by side, such as a minimum and a maximum, in a titled group with its help, see
 * docs/reference/actions-and-controls.md#fields. Below the narrow-width breakpoint the fields stack, see
 * docs/reference/frontend.md#narrow-width.
 */
export function AxisFieldRow({ title, help, children }: { title: string; help: string; children: ReactNode }) {
  const { spacing } = useDesignTokens()
  const narrow = useNarrowWidth()
  return (
    <AxisFieldGroup title={title} help={help}>
      <div style={{ display: 'flex', flexDirection: narrow ? 'column' : 'row', columnGap: spacing.md }}>
        {Children.toArray(children).map((child, index) => (
          <div key={index} style={{ flex: '1 1 0', minWidth: 0 }}>
            {child}
          </div>
        ))}
      </div>
    </AxisFieldGroup>
  )
}
