import { RightOutlined } from '@ant-design/icons'

/**
 * Proposed, see the reviews on #3263 and #3650: the one mark of everything that opens to show content, a diff's file, a group
 * row of a table and a folded section of a page: an arrow that points right while folded and down while open, as Ant Design's Collapse draws it. It
 * turns at once, as Ant Design's table turns its own expand mark, so it reads no motion value the token file does not
 * hold. A sidebar entry with entries under it keeps Ant Design's own submenu arrow, as navigation does. A down arrow
 * alone is the caret of a menu trigger, see docs/reference/actions-and-controls.md#menus. It is hidden from assistive
 * technology, as the control that holds it says whether it is open.
 */
export function FoldIcon({ open }: { open: boolean }) {
  return <RightOutlined aria-hidden style={{ transform: open ? 'rotate(90deg)' : undefined }} />
}
