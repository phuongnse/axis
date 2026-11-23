import { AxisButton } from './AxisButton'
import { Chevron } from './Chevron'

/**
 * The fold of a part, a table row or a diff's file: a subtle `AxisButton` with `Chevron`, pointing right while folded
 * and down while open, named by `showKey` or `hideKey` with `nameValues`, and with `controls`, the id of what it folds.
 * See docs/reference/actions-and-controls.md#buttons and docs/reference/shared-widgets.md#which-widget.
 */
export function FoldButton({
  open,
  showKey,
  hideKey,
  nameValues,
  controls,
  size,
  onToggle,
}: {
  open: boolean
  showKey: string
  hideKey: string
  nameValues?: Record<string, string>
  controls?: string
  size?: 'small'
  onToggle: () => void
}) {
  return (
    <AxisButton
      role="subtle"
      size={size}
      icon={<Chevron point={open ? 'down' : 'right'} />}
      nameKey={open ? hideKey : showKey}
      nameValues={nameValues}
      aria-expanded={open}
      aria-controls={controls}
      onClick={onToggle}
    />
  )
}
