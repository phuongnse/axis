import { useDndContext, useDroppable } from '@dnd-kit/core'
import { SortableContext, useSortable, verticalListSortingStrategy } from '@dnd-kit/sortable'
import { CSS } from '@dnd-kit/utilities'
import { Flex, Typography } from 'antd'
import type { CSSProperties, KeyboardEvent, MouseEvent, PointerEvent, ReactNode } from 'react'
import { outline } from '../platform/formLayout'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { sectionLabel } from './formPaletteSupport'

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
 * field. A section is named by its `name`, or `Section N`, because the translated titles load only inside the canvas.
 */
export function FormOutline({
  draft,
  selected,
  onSelect,
}: {
  draft: Content
  selected: string | null
  onSelect: (pointer: string) => void
}) {
  const t = useText()
  const sections = Array.isArray(draft.sections) ? (draft.sections as unknown[]) : []
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
                droppable={false}
                path={group.step.pointer}
                selected={selected === group.step.pointer}
                onSelect={onSelect}
              >
                <Typography.Text strong>
                  {group.step.titleKey === '' ? group.step.pointer : t(group.step.titleKey)}
                </Typography.Text>
              </DroppableRow>
            )}
            <ul style={{ listStyle: 'none', margin: 0, paddingLeft: group.step === null ? 0 : 16 }}>
              {group.sections.map((index) => (
                <SectionRow
                  key={index}
                  draft={draft}
                  index={index}
                  fields={sectionFields(sections[index])}
                  selected={selected}
                  onSelect={onSelect}
                />
              ))}
            </ul>
          </li>
        ))}
      </ul>
    </Flex>
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
  selected,
  onSelect,
}: {
  draft: Content
  index: number
  fields: unknown[]
  selected: string | null
  onSelect: (pointer: string) => void
}) {
  const t = useText()
  const { active } = useDndContext()
  const path = `/sections/${index}`
  const moving = (active?.data.current as { move?: unknown } | undefined)?.move !== undefined
  const names = fields.map((field) => fieldName(field))
  const keys = names.map((name, position) =>
    name === undefined || names.indexOf(name) !== position ? `#${position}` : name,
  )
  return (
    <DroppableRow
      testId="studio-outline-section"
      path={path}
      selected={selected === path}
      onSelect={onSelect}
      droppable={!(moving && fields.length > 0)}
    >
      <Typography.Text>{sectionLabel(t, draft, index)}</Typography.Text>
      <SortableContext
        items={fields.map((_, position) => `outline:${path}/fields/${position}`)}
        strategy={verticalListSortingStrategy}
      >
        <ul style={{ listStyle: 'none', margin: 0, paddingLeft: 16 }}>
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
              />
            )
          })}
        </ul>
      </SortableContext>
    </DroppableRow>
  )
}

function fieldName(field: unknown): string | undefined {
  const name = typeof field === 'object' && field !== null ? (field as { field?: unknown }).field : undefined
  return typeof name === 'string' ? name : undefined
}

/**
 * A field row. It is a drop target for a palette field and a sortable item: its data carries the pointer it stands for,
 * the place `move` it moves from and its `label`. There is no `field` key, which marks a palette drag.
 */
function FieldRow({
  path,
  move,
  name,
  selected,
  onSelect,
}: {
  path: string
  move: { section: number; index: number }
  name: string
  selected: boolean
  onSelect: (pointer: string) => void
}) {
  const { setNodeRef, listeners, transform, transition, isOver } = useSortable({
    id: `outline:${path}`,
    data: { pointer: path, move, label: name },
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
