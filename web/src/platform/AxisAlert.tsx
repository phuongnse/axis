import { Alert, type AlertProps } from 'antd'
import type { ReactNode } from 'react'

export type AlertKind = 'info' | 'success' | 'warning' | 'error'

type Base = Omit<AlertProps, 'type' | 'showIcon' | 'icon' | 'title' | 'message' | 'role'>

/**
 * A message on a page, a form or a dialog, see docs/reference/shared-widgets.md#which-widget. It always shows the
 * kind's icon, so the kind is never told by colour alone.
 */
export function AxisAlert({ kind, message, ...rest }: Base & { kind: AlertKind; message: ReactNode }) {
  return <Alert {...rest} type={kind} showIcon title={message} />
}
