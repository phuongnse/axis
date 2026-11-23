import { Alert, type AlertProps } from 'antd'
import type { ReactNode } from 'react'
import { useAlertIconStyles } from './antTheme'

export type AlertKind = 'info' | 'success' | 'warning' | 'error'

type Base = Omit<AlertProps, 'type' | 'showIcon' | 'icon' | 'title' | 'message' | 'role' | 'styles'>

/**
 * A message on a page, a form or a dialog, see docs/reference/shared-widgets.md#which-widget. It always shows the
 * kind's icon, so the kind is never told by colour alone. The icon sits on the first line of a message that wraps, in
 * its kind's text colour.
 */
export function AxisAlert({ kind, message, ...rest }: Base & { kind: AlertKind; message: ReactNode }) {
  const icons = useAlertIconStyles()
  return (
    <Alert
      {...rest}
      type={kind}
      showIcon
      title={message}
      styles={{ root: { alignItems: 'flex-start' }, icon: icons[kind] }}
    />
  )
}
