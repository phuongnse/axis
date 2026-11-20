import { RightOutlined } from '@ant-design/icons'

/**
 * The icon that shows a folded part as open or closed: a caret that points right when closed and down when open, as Ant
 * Design's `Collapse` draws it, see docs/reference/shared-widgets.md#which-widget. It sits at the start of a fold, before
 * the heading or name it opens.
 */
export function FoldIcon({ open }: { open: boolean }) {
  return <RightOutlined rotate={open ? 90 : 0} aria-hidden />
}
