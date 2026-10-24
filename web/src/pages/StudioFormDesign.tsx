import {
  DndContext,
  DragOverlay,
  KeyboardSensor,
  PointerSensor,
  useSensor,
  useSensors,
  type DragEndEvent,
} from '@dnd-kit/core'
import { sortableKeyboardCoordinates } from '@dnd-kit/sortable'
import { Button, Flex } from 'antd'
import { useState } from 'react'
import { dropPlace, movePlace, sectionMovePlace, type FieldPlace } from '../platform/formLayout'
import { useText } from '../platform/texts'
import { outlineCollision, silent } from './designDrag'
import { FormOutline } from './FormOutline'
import { FormPalette } from './FormPalette'
import { insertField, moveFieldTo, moveSectionTo } from './formPaletteSupport'
import type { ChangeHandler } from './SchemaForm'
import { StudioFormCanvas } from './StudioFormCanvas'
import { type FormUse, StudioFormUsedOn } from './StudioFormUsedOn'
import type { OpenResource } from './StudioReferences'

/**
 * The Design tab of a form: the list of its uses, the canvas, and beside the canvas the field palette, the layout
 * outline and the properties. A palette field that is dragged with the pointer onto a section or a field of the
 * outline is inserted there: at the end of the section, or after the field. The innermost target wins, and the new
 * field is selected. A field row of the outline moves by pointer drag onto another field row or an empty section, or by
 * Space, the arrow keys and Space, and takes the place of the field it is dropped on. Escape cancels a move. The moved
 * field stays selected. A section row of the outline moves the same way, onto another section row, which it takes the
 * place of, or onto a step's heading, which takes it at the end of the step. On a form with steps, `sections` then
 * follows the order of the steps. The section stays selected, and one undo reverses the move. For all of them, the
 * live region names the element, its section or step and its position. The canvas, the palette, the outline and the drag
 * library are one lazy chunk, which loads when the tab opens.
 */
export default function StudioFormDesign({
  application,
  resourceId,
  draft,
  selected,
  onSelect,
  onChange,
  usedOn,
  onOpen,
}: {
  application: string
  resourceId: string
  draft: Record<string, unknown>
  /** The pointer of the selected field, section or step, or null. */
  selected: string | null
  onSelect: (pointer: string) => void
  onChange: ChangeHandler
  usedOn: FormUse[] | 'failed' | undefined
  onOpen: OpenResource
}) {
  const t = useText()
  const [announcement, setAnnouncement] = useState('')
  const [dragging, setDragging] = useState<string>()
  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 4 } }),
    useSensor(KeyboardSensor, {
      coordinateGetter: sortableKeyboardCoordinates,
      keyboardCodes: { start: ['Space'], cancel: ['Escape'], end: ['Space'] },
    }),
  )

  function drop({ active, over }: DragEndEvent) {
    setDragging(undefined)
    const data = active.data.current as { field?: unknown; move?: FieldPlace; section?: unknown } | undefined
    const pointer = (over?.data.current as { pointer?: unknown } | undefined)?.pointer
    if (typeof pointer !== 'string') {
      return
    }
    if (typeof data?.section === 'number') {
      const to = sectionMovePlace(draft, pointer)
      if (to !== undefined) {
        moveSectionTo(t, draft, data.section, to, onChange, onSelect, setAnnouncement)
      }
      return
    }
    if (data?.move !== undefined) {
      const to = movePlace(draft, pointer)
      if (to !== undefined) {
        moveFieldTo(t, draft, data.move, to, onChange, onSelect, setAnnouncement)
      }
      return
    }
    const field = data?.field
    if (typeof field !== 'string') {
      return
    }
    const place = dropPlace(draft, pointer)
    if (place !== undefined) {
      insertField(t, draft, field, place, onChange, onSelect, setAnnouncement)
    }
  }

  return (
    <DndContext
      sensors={sensors}
      collisionDetection={outlineCollision}
      accessibility={{ announcements: silent }}
      onDragStart={({ active }) => {
        const data = active.data.current as { field?: unknown; label?: unknown } | undefined
        setDragging(String(data?.field ?? data?.label ?? ''))
      }}
      onDragCancel={() => setDragging(undefined)}
      onDragEnd={drop}
    >
      <div data-testid="studio-form-design">
        <Flex vertical gap="middle">
          <StudioFormUsedOn entries={usedOn} onOpen={onOpen} />
          <StudioFormCanvas
            application={application}
            resourceId={resourceId}
            draft={draft}
            selected={selected}
            onSelect={onSelect}
            onChange={onChange}
            onAnnounce={setAnnouncement}
            aside={
              <Flex vertical gap="middle">
                <FormPalette
                  draft={draft}
                  selected={selected}
                  announcement={announcement}
                  onAnnounce={setAnnouncement}
                  onSelect={onSelect}
                  onChange={onChange}
                />
                <FormOutline draft={draft} selected={selected} onSelect={onSelect} />
              </Flex>
            }
          />
        </Flex>
      </div>
      <DragOverlay dropAnimation={null}>{dragging === undefined ? null : <Button>{dragging}</Button>}</DragOverlay>
    </DndContext>
  )
}
