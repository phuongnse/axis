import {
  DndContext,
  DragOverlay,
  PointerSensor,
  useSensor,
  useSensors,
  type DragEndEvent,
  type DragOverEvent,
  type DragStartEvent,
} from '@dnd-kit/core'
import { Button, Flex, Typography } from 'antd'
import { useState } from 'react'
import { getAt } from '../platform/jsonPatch'
import { addableTypes, dropPlace, movePlace, moveTo, newWidget, type DropTarget } from '../platform/pageWidgets'
import { useText } from '../platform/texts'
import { innermostCollision, silent } from './designDrag'
import { PageWidgets } from './PageEditor'
import { PagePalette } from './PagePalette'
import { landedText, paletteReasons, usePartLabel } from './pagePaletteSupport'
import type { ChangeHandler } from './SchemaForm'
import { StudioPageCanvas } from './StudioPageCanvas'

/** The pointer of the widget list or widget before the last segment: `/widgets/1/widgets/0` gives `/widgets/1/widgets`. */
function parentList(pointer: string): string {
  return pointer.slice(0, pointer.lastIndexOf('/'))
}

/**
 * The Design tab of a page: the canvas, and beside it the palette and the widget tree. A palette type that is dragged
 * with the pointer onto the canvas is inserted where it is dropped: before a widget on its upper half, after it on its
 * lower half, or at the end of a list when it is dropped on the list. The innermost target wins. A drop that a page
 * limit forbids inserts nothing, and the live region gives the reason. An allowed drop selects the new widget, and
 * the live region names the list and position where it landed.
 *
 * A widget of the canvas that is dragged moves to the same targets, as one edit of the draft, and stays selected. A
 * move the format refuses is flagged while it is dragged: the drag overlay and the live region give the reason, and a
 * drop there changes nothing. These are a widget inside itself, and a container whose lists would go past level 4. A
 * drop on the widget itself or where it already is changes nothing and says nothing. The canvas, the palette and the
 * drag library are one lazy chunk, which loads when the tab opens.
 */
export default function StudioPageDesign({
  application,
  resourceId,
  draft,
  selected,
  onSelect,
  onChange,
}: {
  application: string
  resourceId: string
  draft: Record<string, unknown>
  /** The pointer of the selected widget. */
  selected: string
  onSelect: (pointer: string) => void
  onChange: ChangeHandler
}) {
  const t = useText()
  const partLabel = usePartLabel()
  const [announcement, setAnnouncement] = useState('')
  const [dragging, setDragging] = useState<{ label: string; refusal?: string }>()
  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 4 } }))

  /** What the pointer is over as a drop target, or undefined. The half is only read for a widget. */
  function targetOf(
    { over, delta, activatorEvent }: DragOverEvent | DragEndEvent,
    lower?: boolean,
  ): DropTarget | undefined {
    const data = over?.data.current as { kind?: unknown; pointer?: unknown } | undefined
    if (over === null || typeof data?.pointer !== 'string') {
      return undefined
    }
    if (data.kind === 'widget') {
      const pointerY = ((activatorEvent as { clientY?: number }).clientY ?? 0) + delta.y
      return {
        kind: 'widget',
        pointer: data.pointer,
        half: lower || pointerY >= over.rect.top + over.rect.height / 2 ? 'lower' : 'upper',
      }
    }
    return data.kind === 'list' ? { kind: 'list', pointer: data.pointer } : undefined
  }

  /** The pointer of the canvas widget that is dragged, or undefined for a palette drag. */
  function movedFrom(active: DragStartEvent['active']): string | undefined {
    const data = active.data.current as { kind?: unknown; pointer?: unknown } | undefined
    return data?.kind === 'move' && typeof data.pointer === 'string' ? data.pointer : undefined
  }

  function start({ active }: DragStartEvent) {
    const data = active.data.current as { type?: unknown; label?: unknown } | undefined
    setDragging({ label: String(movedFrom(active) === undefined ? (data?.type ?? '') : (data?.label ?? '')) })
  }

  function dragOver(event: DragOverEvent) {
    const from = movedFrom(event.active)
    if (from === undefined) {
      return
    }
    const target = targetOf(event, true)
    const widgets = draft.widgets
    const place = target === undefined || !Array.isArray(widgets) ? undefined : movePlace(widgets, from, target)
    const refusal = place !== undefined && 'limit' in place ? t(paletteReasons[place.limit]) : undefined
    setDragging((current) => current && { label: current.label, refusal })
    if (refusal !== undefined) {
      setAnnouncement(refusal)
    }
  }

  function move(event: DragEndEvent, from: string, widgets: unknown[]) {
    const target = targetOf(event)
    const place = target === undefined ? undefined : movePlace(widgets, from, target)
    if (place === undefined) {
      return
    }
    if ('limit' in place) {
      setAnnouncement(t(paletteReasons[place.limit]))
      return
    }
    const moved = moveTo(widgets, from, place.list, place.index)
    if (moved === undefined || moved.pointer === from) {
      return
    }
    const type = (getAt({ widgets }, from) as { type?: unknown }).type
    onChange('/widgets', moved.widgets)
    onSelect(moved.pointer)
    setAnnouncement(
      landedText(
        t,
        partLabel,
        moved.widgets,
        parentList(moved.pointer),
        Number(moved.pointer.slice(moved.pointer.lastIndexOf('/') + 1)),
        String(type),
        'shell.studio.palette.moved',
      ),
    )
  }

  function drop(event: DragEndEvent) {
    const { active, over } = event
    setDragging(undefined)
    const widgets = draft.widgets
    const from = movedFrom(active)
    if (from !== undefined) {
      if (Array.isArray(widgets)) {
        move(event, from, widgets)
      }
      return
    }
    const type = (active.data.current as { type?: unknown } | undefined)?.type
    const target = targetOf(event)
    if (
      over === null ||
      typeof type !== 'string' ||
      !addableTypes.includes(type) ||
      target === undefined ||
      !Array.isArray(widgets)
    ) {
      return
    }
    const place = dropPlace(widgets, type, target)
    if (place === undefined) {
      return
    }
    if ('limit' in place) {
      setAnnouncement(t(paletteReasons[place.limit]))
      return
    }
    const items = getAt({ widgets }, place.list) as unknown[]
    onChange(place.list, [...items.slice(0, place.index), newWidget(type), ...items.slice(place.index)])
    onSelect(`${place.list}/${place.index}`)
    setAnnouncement(landedText(t, partLabel, widgets, place.list, place.index, type))
  }

  return (
    <DndContext
      sensors={sensors}
      collisionDetection={innermostCollision}
      accessibility={{ announcements: silent }}
      onDragStart={start}
      onDragOver={dragOver}
      onDragCancel={() => setDragging(undefined)}
      onDragEnd={drop}
    >
      <Flex gap="middle" align="flex-start" data-testid="studio-page-design">
        <div style={{ flex: 1, minWidth: 0 }}>
          <StudioPageCanvas
            application={application}
            resourceId={resourceId}
            draft={draft}
            selected={selected}
            onSelect={onSelect}
          />
        </div>
        <div data-testid="studio-design-panel" style={{ width: 400, flexShrink: 0 }}>
          <PagePalette
            value={draft}
            selected={selected}
            announcement={announcement}
            onAnnounce={setAnnouncement}
            onSelect={onSelect}
            onChange={onChange}
          />
          <PageWidgets value={draft} selected={selected} onSelect={onSelect} onChange={onChange} />
        </div>
      </Flex>
      <DragOverlay dropAnimation={null}>
        {dragging === undefined ? null : (
          <Flex vertical align="flex-start">
            <Button>{dragging.label}</Button>
            {dragging.refusal !== undefined && (
              <Typography.Text type="danger" data-testid="studio-drag-refusal">
                {dragging.refusal}
              </Typography.Text>
            )}
          </Flex>
        )}
      </DragOverlay>
    </DndContext>
  )
}
