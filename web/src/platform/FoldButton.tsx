import { AxisButton } from './AxisButton'
import { Chevron } from './Chevron'

/**
 * The one fold of a part, a table row and a diff's file: a subtle `AxisButton` with `Chevron`, pointing right when folded
 * and down when open, see docs/reference/shared-widgets.md#which-widget. Its name is the show text or the hide text of
 * `names`, filled with `values`. It sets `aria-expanded`, and `aria-controls` when it is given the body it opens.
 */
export function FoldButton({
  open,
  onToggle,
  names,
  values,
  controls,
  size,
}: {
  open: boolean
  onToggle: () => void
  /** The text keys of the name while folded (`show`) and while open (`hide`). */
  names: { show: string; hide: string }
  values?: Record<string, string>
  /** The id of the body the fold opens. */
  controls?: string
  size?: 'small'
}) {
  return (
    <AxisButton
      role="subtle"
      size={size}
      icon={<Chevron point={open ? 'down' : 'right'} />}
      nameKey={open ? names.hide : names.show}
      nameValues={values}
      aria-expanded={open}
      aria-controls={controls}
      onClick={onToggle}
    />
  )
}
