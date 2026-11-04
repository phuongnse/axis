import { useDndContext, useDroppable } from '@dnd-kit/core'
import { SortableContext, useSortable, verticalListSortingStrategy } from '@dnd-kit/sortable'
import { CSS } from '@dnd-kit/utilities'
import { Flex, Typography } from 'antd'
import type { CSSProperties, KeyboardEvent, MouseEvent, PointerEvent, ReactNode } from 'react'
import { outline } from '../platform/formLayout'
import { markOf } from '../platform/layerMarks'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { sectionLabel } from './formPaletteSupport'
import { LayerMarkTag, RemovedRows, type DesignerLayer } from './LayerMarkViews'

type Content = Record<string, unknown>

/** The two listeners of the library that a sortable row uses: the pointer sensor's and the keyboard sensor's. */
interface DragListeners {
  onPointerDown?: (event: PointerEvent<HTMLElement>) => void
  onKeyDown?: (event: KeyboardEvent<HTMLElement>) => void
}

/**
 * The layout of a form, read from its draft: the steps, the sections of each step, and the fields of each section. A
 * click on a step, section or field selects it, and the selected one has `aria-current`. A section or a field is a
 * drop target for a palette field, and shows an outline in the primary colour while one is over it. A field row lies
 * inside its section's row, so the innermost row under the pointer wins. A field row is also sortable: the pointer, or
 * Space, the arrow keys and Space, move it within its section or into another one. While a field moves, the targets
 * are the field rows and the empty sections, and a section that has fields is not one, so a move always lands on a
 * field. A section row is sortable too, by a press on its own area, such as its title line, and not on a field row. It
 * moves within its group, or into another step when a section row or a step's heading of that step is the target.
 * While a section moves, the targets are the section rows and the step headings, and no field row is one. On a form
 * with steps, a section that no step lists is not a target. A section is named by its `name`, or `Section N`, because the translated titles load only inside the canvas. A
 * section with no fields shows a dashed drop area inside its row, so a drop on it lands in that section. With `layer`, a
 * step, a section (by its `name`) and a field (by its `field`) that the layer added or changed show the tag and Reset to
 * module of that mark. The fields that it removed are muted rows inside their section's row, and the removed sections
 * and steps are at the end of the outline. A press, a key or a click on a mark does not start a move or select the row.
 */
export function FormOutline({
  draft,
  selected,
  onSelect,
  layer,
}: {
  draft: Content
  selected: string | null
  onSelect: (pointer: string) => void
  layer?: DesignerLayer
}) {
  const t = useText()
  const { active } = useDndContext()
  const sectionMoving = typeof (active?.data.current as { section?: unknown } | undefined)?.section === 'number'
  const sections = Array.isArray(draft.sections) ? (draft.sections as unknown[]) : []
  const hasSteps = Array.isArray(draft.steps) && draft.steps.length > 0
  return (
    <Flex
      vertical
      gap="small"
      data-testid="studio-form-outline"
      role="group"
      aria-label={t('shell.studio.design.outline')}
    >
      <Typography.Text strong>{t('shell.studio.design.outline')}</Typography.Text>
      <ul style={{ listStyle: 'none', margin: 0, padding: 0 }}>
        {outline(draft).map((group, position) => (
          <li key={group.step?.pointer ?? `rest-${position}`}>
            {group.step !== null && (
              <DroppableRow
                testId="studio-outline-step"
                as="div"
                droppable={sectionMoving}
                path={group.step.pointer}
                selected={selected === group.step.pointer}
                onSelect={onSelect}
              >
                <Typography.Text strong>
                  {group.step.titleKey === '' ? group.step.pointer : t(group.step.titleKey)}
                </Typography.Text>
                <OutlineMark
                  layer={layer}
                  list="/steps"
                  index={pointerIndex(group.step.pointer)}
                  name={nameOf(draft.steps, pointerIndex(group.step.pointer), 'name')}
                />
              </DroppableRow>
            )}
            <SortableContext
              items={group.sections.map((index) => `outline:/sections/${index}`)}
              strategy={verticalListSortingStrategy}
            >
              <ul style={{ listStyle: 'none', margin: 0, paddingLeft: group.step === null ? 0 : 16 }}>
                {group.sections.map((index) => (
                  <SectionRow
                    key={index}
                    draft={draft}
                    index={index}
                    fields={sectionFields(sections[index])}
                    unlisted={group.step === null && hasSteps}
                    selected={selected}
                    onSelect={onSelect}
                    layer={layer}
                  />
                ))}
              </ul>
            </SortableContext>
          </li>
        ))}
      </ul>
      <RemovedRows layer={layer} list="/sections" />
      <RemovedRows layer={layer} list="/steps" />
    </Flex>
  )
}

/** The index at the end of a pointer such as `/steps/2`. */
function pointerIndex(pointer: string): number {
  return Number(pointer.slice(pointer.lastIndexOf('/') + 1))
}

/** The text property `property` of the item at `index` of `list`, or `undefined` when there is none. */
function nameOf(list: unknown, index: number, property: string): string | undefined {
  const item = Array.isArray(list) ? (list[index] as unknown) : undefined
  const value = typeof item === 'object' && item !== null ? (item as Record<string, unknown>)[property] : undefined
  return typeof value === 'string' && value !== '' ? value : undefined
}

/**
 * The tag and Reset to module of an element that the layer added or changed, or nothing. The span keeps a press, a key
 * and a click on the button from reaching the sortable row it sits in, so Space does not start a move and a click does
 * not select the row.
 */
function OutlineMark({
  layer,
  list,
  index,
  name,
}: {
  layer: DesignerLayer | undefined
  list: string
  index: number
  name: string | undefined
}) {
  if (layer === undefined || name === undefined || markOf(layer.elements, list, name) === undefined) {
    return null
  }
  return (
    <Stop>
      <LayerMarkTag layer={layer} list={list} index={index} name={name} />
    </Stop>
  )
}

/** A span that stops a pointer, key or click event of its content from reaching the row around it. */
function Stop({ children }: { children: ReactNode }) {
  return (
    <span
      style={{ display: 'block', cursor: 'default' }}
      onPointerDown={(event) => event.stopPropagation()}
      onKeyDown={(event) => event.stopPropagation()}
      onClick={(event) => event.stopPropagation()}
    >
      {children}
    </span>
  )
}

function sectionFields(section: unknown): unknown[] {
  const fields = typeof section === 'object' && section !== null ? (section as { fields?: unknown }).fields : undefined
  return Array.isArray(fields) ? fields : []
}

function SectionRow({
  draft,
  index,
  fields,
  unlisted,
  selected,
  onSelect,
  layer,
}: {
  draft: Content
  index: number
  fields: unknown[]
  /** The draft has steps and none lists this section, so a section move cannot land here. */
  unlisted: boolean
  selected: string | null
  onSelect: (pointer: string) => void
  layer?: DesignerLayer
}) {
  const t = useText()
  const { active } = useDndContext()
  const { colour, radius } = useDesignTokens()
  const path = `/sections/${index}`
  const data = active?.data.current as { move?: unknown; section?: unknown } | undefined
  const moving = data?.move !== undefined
  const sectionMoving = typeof data?.section === 'number'
  const label = sectionLabel(t, draft, index)
  const { setNodeRef, listeners, transform, transition, isOver } = useSortable({
    id: `outline:${path}`,
    data: { pointer: path, section: index, label },
    disabled: { draggable: false, droppable: (moving && fields.length > 0) || (sectionMoving && unlisted) },
  })
  const names = fields.map((field) => fieldName(field))
  const keys = names.map((name, position) =>
    name === undefined || names.indexOf(name) !== position ? `#${position}` : name,
  )
  return (
    <Row
      testId="studio-outline-section"
      path={path}
      selected={selected === path}
      onSelect={onSelect}
      nodeRef={setNodeRef}
      isOver={isOver}
      listeners={listeners as DragListeners | undefined}
      motion={{ transform: CSS.Translate.toString(transform), transition }}
    >
      <Typography.Text>{label}</Typography.Text>
      <OutlineMark layer={layer} list="/sections" index={index} name={nameOf(draft.sections, index, 'name')} />
      <SortableContext
        items={fields.map((_, position) => `outline:${path}/fields/${position}`)}
        strategy={verticalListSortingStrategy}
      >
        <ul style={{ listStyle: 'none', margin: 0, paddingLeft: 16 }}>
          {fields.length === 0 && (
            <li
              data-testid="studio-outline-empty-section"
              style={{ border: `1px dashed ${colour.border}`, borderRadius: radius.base, padding: '4px 8px' }}
            >
              <Typography.Text type="secondary">{t('shell.studio.design.dropFieldHere')}</Typography.Text>
            </li>
          )}
          {fields.map((_, position) => {
            const fieldPath = `${path}/fields/${position}`
            return (
              <FieldRow
                key={keys[position]}
                path={fieldPath}
                move={{ section: index, index: position }}
                name={names[position] ?? fieldPath}
                selected={selected === fieldPath}
                onSelect={onSelect}
                layer={layer}
                fieldsList={`${path}/fields`}
                markName={names[position]}
              />
            )
          })}
        </ul>
      </SortableContext>
      {layer !== undefined && (
        <Stop>
          <RemovedRows layer={layer} list={`${path}/fields`} />
        </Stop>
      )}
    </Row>
  )
}

function fieldName(field: unknown): string | undefined {
  const name = typeof field === 'object' && field !== null ? (field as { field?: unknown }).field : undefined
  return typeof name === 'string' ? name : undefined
}

/**
 * A field row. It is a drop target for a palette field and a sortable item: its data carries the pointer it stands for,
 * the place `move` it moves from and its `label`. There is no `field` key, which marks a palette drag. It is not a drop
 * target while a section moves.
 */
function FieldRow({
  path,
  move,
  name,
  selected,
  onSelect,
  layer,
  fieldsList,
  markName,
}: {
  path: string
  move: { section: number; index: number }
  name: string
  selected: boolean
  onSelect: (pointer: string) => void
  layer?: DesignerLayer
  /** The pointer of the section's field list, such as `/sections/0/fields`. */
  fieldsList: string
  /** The `field` of the field, which keys its layer mark, or `undefined` when it has none. */
  markName: string | undefined
}) {
  const { active } = useDndContext()
  const sectionMoving = typeof (active?.data.current as { section?: unknown } | undefined)?.section === 'number'
  const { setNodeRef, listeners, transform, transition, isOver } = useSortable({
    id: `outline:${path}`,
    data: { pointer: path, move, label: name },
    disabled: { draggable: false, droppable: sectionMoving },
  })
  return (
    <Row
      testId="studio-outline-field"
      path={path}
      selected={selected}
      onSelect={onSelect}
      nodeRef={setNodeRef}
      isOver={isOver}
      listeners={listeners as DragListeners | undefined}
      motion={{ transform: CSS.Translate.toString(transform), transition }}
    >
      <Typography.Text type="secondary">{name}</Typography.Text>
      <OutlineMark layer={layer} list={fieldsList} index={move.index} name={markName} />
    </Row>
  )
}

/** A row that is a drop target for a palette field and stands for its `path`, unless it is not `droppable`. */
function DroppableRow({
  path,
  droppable,
  ...row
}: {
  testId: string
  path: string
  selected: boolean
  onSelect: (pointer: string) => void
  droppable: boolean
  as?: 'li' | 'div'
  children: ReactNode
}) {
  const { setNodeRef, isOver } = useDroppable({ id: `outline:${path}`, data: { pointer: path }, disabled: !droppable })
  return <Row path={path} nodeRef={setNodeRef} isOver={isOver} {...row} />
}

/**
 * One row of the outline. It is a Tab stop that Enter or Space selects. The children go inside the row, so a nested
 * row is under its parent's element. The caller brings the node ref and `isOver` of the library, and a sortable row
 * also its `listeners` and `motion`. Space selects the row only while no move is under way, so the Space that drops a
 * field does not select the old place.
 */
function Row({
  testId,
  path,
  selected,
  onSelect,
  nodeRef,
  isOver,
  listeners,
  motion,
  as,
  children,
}: {
  testId: string
  path: string
  selected: boolean
  onSelect: (pointer: string) => void
  nodeRef: (node: HTMLElement | null) => void
  isOver: boolean
  listeners?: DragListeners
  motion?: CSSProperties
  /** A step stands alone in its group's item, so it is a `div`. */
  as?: 'li' | 'div'
  children: ReactNode
}) {
  const { colour, radius } = useDesignTokens()
  const { active } = useDndContext()
  const props = {
    ref: nodeRef,
    'data-testid': testId,
    'data-path': path,
    'aria-current': selected ? ('true' as const) : undefined,
    tabIndex: 0,
    onPointerDown: listeners?.onPointerDown,
    onClick: (event: MouseEvent<HTMLElement>) => {
      event.stopPropagation()
      onSelect(path)
    },
    onKeyDown: (event: KeyboardEvent<HTMLElement>) => {
      // The library ignores a key event that is already prevented, so its listener goes first.
      listeners?.onKeyDown?.(event)
      if (active === null && event.target === event.currentTarget && (event.key === 'Enter' || event.key === ' ')) {
        event.preventDefault()
        onSelect(path)
      }
    },
    style: {
      cursor: 'pointer',
      borderRadius: radius.base,
      outline: isOver ? `2px solid ${colour.primary}` : selected ? `1px solid ${colour.primary}` : undefined,
      outlineOffset: 1,
      ...motion,
    },
  }
  return as === 'div' ? <div {...props}>{children}</div> : <li {...props}>{children}</li>
}
