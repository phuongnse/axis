import { DndContext, DragOverlay, PointerSensor, useSensor, useSensors, type DragEndEvent } from '@dnd-kit/core'
import { Button, Flex } from 'antd'
import { useState } from 'react'
import { dropPlace } from '../platform/formLayout'
import { useText } from '../platform/texts'
import { innermostCollision, silent } from './designDrag'
import { FormOutline } from './FormOutline'
import { FormPalette } from './FormPalette'
import { insertField } from './formPaletteSupport'
import type { ChangeHandler } from './SchemaForm'
import { StudioFormCanvas } from './StudioFormCanvas'
import { type FormUse, StudioFormUsedOn } from './StudioFormUsedOn'
import type { OpenResource } from './StudioReferences'

/**
 * The Design tab of a form: the list of its uses, the canvas, and beside the canvas the field palette, the layout
 * outline and the properties. A palette field that is dragged with the pointer onto a section or a field of the
 * outline is inserted there: at the end of the section, or after the field. The innermost target wins, and the new
 * field is selected. The live region names the field, its section and its position. The canvas, the palette, the
 * outline and the drag library are one lazy chunk, which loads when the tab opens.
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
  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 4 } }))

  function drop({ active, over }: DragEndEvent) {
    setDragging(undefined)
    const field = (active.data.current as { field?: unknown } | undefined)?.field
    const pointer = (over?.data.current as { pointer?: unknown } | undefined)?.pointer
    if (typeof field !== 'string' || typeof pointer !== 'string') {
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
      collisionDetection={innermostCollision}
      accessibility={{ announcements: silent }}
      onDragStart={({ active }) => setDragging(String((active.data.current as { field?: unknown })?.field ?? ''))}
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
