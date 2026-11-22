import type { ReactNode } from 'react'
import { AxisActionRow, AxisButton } from './AxisButton'

/**
 * The Cancel and the finish button of an Ant Design dialog, passed as its `footer`, see
 * docs/reference/actions-and-controls.md#buttons. Cancel is a secondary button, a light text button inside the row,
 * and the finish is a `finish` button, a solid `danger` fill when the dialog confirms a removal. The dialog keeps its
 * own `onCancel` for Escape and the close icon, and passes the same handler here, so Cancel does what they do.
 */
export function DialogFooter({
  actions,
  finishText,
  cancelText,
  onFinish,
  onCancel,
  destructive,
  finishing,
  finishDisabled,
  finishTestId,
}: {
  finishText: ReactNode
  cancelText: ReactNode
  onFinish: () => void
  onCancel: () => void
  destructive?: boolean
  /** Shows the finish button's spinner while the dialog's work runs. */
  finishing?: boolean
  finishDisabled?: boolean
  finishTestId?: string
  /** Other actions of the dialog, such as a Reset, drawn before Cancel in the same row. */
  actions?: ReactNode
}) {
  return (
    <AxisActionRow justify="end">
      {actions}
      <AxisButton role="secondary" onClick={onCancel}>
        {cancelText}
      </AxisButton>
      <AxisButton
        role="finish"
        destructive={destructive}
        loading={finishing}
        disabled={finishDisabled}
        data-testid={finishTestId}
        onClick={onFinish}
      >
        {finishText}
      </AxisButton>
    </AxisActionRow>
  )
}
