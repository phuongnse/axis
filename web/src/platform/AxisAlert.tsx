import { Alert, type AlertProps } from 'antd'
import type { ReactNode } from 'react'
import { useDesignTokens } from './themeMode'

export type AlertKind = 'info' | 'success' | 'warning' | 'error'

type Base = Omit<AlertProps, 'type' | 'showIcon' | 'icon' | 'title' | 'message' | 'role'>

/**
 * A message on a page, a form or a dialog, see docs/reference/shared-widgets.md#which-widget. It always shows the
 * kind's icon, so the kind is never told by colour alone. Proposed: the icon sits on the first line of the words,
 * through Ant Design's documented `styles.icon`, so a message that wraps reads from its icon, see the review on #3263.
 */
export function AxisAlert({ kind, message, ...rest }: Base & { kind: AlertKind; message: ReactNode }) {
  const { spacing } = useDesignTokens()
  // The icon is a line's height less its own, half of spacing-xs, above its middle, so it centres on the first line.
  return (
    <Alert
      {...rest}
      type={kind}
      showIcon
      title={message}
      styles={{ icon: { alignSelf: 'flex-start', marginTop: spacing.xs / 2 } }}
    />
  )
}
