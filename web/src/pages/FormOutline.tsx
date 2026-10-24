import { useDroppable } from '@dnd-kit/core'
import { Flex, Typography } from 'antd'
import type { KeyboardEvent, MouseEvent, ReactNode } from 'react'
import { outline } from '../platform/formLayout'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { sectionLabel } from './formPaletteSupport'

type Content = Record<string, unknown>

/**
 * The layout of a form, read from its draft: the steps, the sections of each step, and the fields of each section. A
 * click on a step, section or field selects it, and the selected one has `aria-current`. A section or a field is a
 * drop target for a palette field, and shows an outline in the primary colour while one is over it. A field row lies
 * inside its section's row, so the innermost row under the pointer wins. A section is named by its `name`, or `Section
 * N`, because the translated titles load only inside the canvas.
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
              <Row
                testId="studio-outline-step"
                as="div"
                path={group.step.pointer}
                selected={selected === group.step.pointer}
                onSelect={onSelect}
              >
                <Typography.Text strong>
                  {group.step.titleKey === '' ? group.step.pointer : t(group.step.titleKey)}
                </Typography.Text>
              </Row>
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
  const path = `/sections/${index}`
  return (
    <Row testId="studio-outline-section" path={path} selected={selected === path} onSelect={onSelect} droppable>
      <Typography.Text>{sectionLabel(t, draft, index)}</Typography.Text>
      <ul style={{ listStyle: 'none', margin: 0, paddingLeft: 16 }}>
        {fields.map((field, position) => {
          const fieldPath = `${path}/fields/${position}`
          const name = typeof field === 'object' && field !== null ? (field as { field?: unknown }).field : undefined
          return (
            <Row
              key={position}
              testId="studio-outline-field"
              path={fieldPath}
              selected={selected === fieldPath}
              onSelect={onSelect}
              droppable
            >
              <Typography.Text type="secondary">{typeof name === 'string' ? name : fieldPath}</Typography.Text>
            </Row>
          )
        })}
      </ul>
    </Row>
  )
}

/**
 * One row of the outline. It is a Tab stop that Enter or Space selects. A droppable row stands for the pointer of its
 * element. The children go inside the row, so a nested row is under its parent's element.
 */
function Row({
  testId,
  path,
  selected,
  onSelect,
  droppable,
  as,
  children,
}: {
  testId: string
  path: string
  selected: boolean
  onSelect: (pointer: string) => void
  droppable?: boolean
  /** A step stands alone in its group's item, so it is a `div`. */
  as?: 'li' | 'div'
  children: ReactNode
}) {
  const { colour, radius } = useDesignTokens()
  const { setNodeRef, isOver } = useDroppable({
    id: `outline:${path}`,
    data: { pointer: path },
    disabled: droppable !== true,
  })
  const props = {
    ref: setNodeRef,
    'data-testid': testId,
    'data-path': path,
    'aria-current': selected ? ('true' as const) : undefined,
    tabIndex: 0,
    onClick: (event: MouseEvent<HTMLElement>) => {
      event.stopPropagation()
      onSelect(path)
    },
    onKeyDown: (event: KeyboardEvent<HTMLElement>) => {
      if (event.target === event.currentTarget && (event.key === 'Enter' || event.key === ' ')) {
        event.preventDefault()
        onSelect(path)
      }
    },
    style: {
      cursor: 'pointer',
      borderRadius: radius.base,
      outline: isOver ? `2px solid ${colour.primary}` : selected ? `1px solid ${colour.primary}` : undefined,
      outlineOffset: 1,
    },
  }
  return as === 'div' ? <div {...props}>{children}</div> : <li {...props}>{children}</li>
}
