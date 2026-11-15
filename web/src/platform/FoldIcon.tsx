import { RightOutlined } from '@ant-design/icons'

/**
 * Proposed, see the review on #3263: the one mark of everything that opens and folds, a diff's file, a group row of a
 * table and a sidebar entry with entries under it: an arrow that points right while folded and down while open, as
 * Ant Design's Collapse draws it, with no animation, as Axis moves nothing for show. A down arrow alone is the caret
 * of a menu trigger, see docs/reference/actions-and-controls.md#menus. It is hidden from assistive technology, as the
 * control that holds it says whether it is open.
 */
export function FoldIcon({ open }: { open: boolean }) {
  return <RightOutlined aria-hidden style={{ transform: open ? 'rotate(90deg)' : undefined }} />
}
