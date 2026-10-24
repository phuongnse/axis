import { Checkbox, Flex, Input, Select, Typography } from 'antd'
import { lazy, Suspense, useContext, useState } from 'react'
import { renameSection } from '../platform/formLayout'
import { getAt } from '../platform/jsonPatch'
import type { FieldMetadata } from '../platform/site'
import { StudioNamesContext } from '../platform/studioNames'
import { useText } from '../platform/texts'
import { ExpressionCheckContext } from './expressionCheckContext'
import { expressionScope } from './expressionScope'
import { LabelTexts } from './LabelTexts'
import { moveSectionTo } from './formPaletteSupport'
import { LabelTextsContext } from './labelTextsContext'
import type { ChangeHandler } from './SchemaForm'

const LazyExpressionEditor = lazy(() => import('./ExpressionEditor'))

type Content = Record<string, unknown>

type Condition = 'visibleWhen' | 'requiredWhen' | 'readOnlyWhen'

const fieldPointer = /^\/sections\/\d+\/fields\/\d+$/
const sectionPointer = /^\/sections\/\d+$/
const stepPointer = /^\/steps\/\d+$/

function isContent(value: unknown): value is Content {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

/**
 * The properties of the element that is selected on the form canvas, found by its JSON Pointer in the draft. A field
 * has `readOnly` and its three conditions, a section its name, title, `visibleWhen` and, on a form with steps, a Move
 * to step picker, and a step its title. A field the entity marks required has no `visibleWhen` (AXC0226). A computed, sequence or creator field is always read-only. A
 * selection that finds no object in the draft shows a hint, so no edit goes to a stale pointer.
 */
export function FormProperties({
  draft,
  selected,
  entityFields,
  onSelect,
  onAnnounce,
  onChange,
}: {
  draft: Content
  selected: string | null
  entityFields: FieldMetadata[]
  /** Selects the section where a move lands. */
  onSelect: (pointer: string) => void
  /** Says a move aloud. */
  onAnnounce: (text: string) => void
  onChange: ChangeHandler
}) {
  const t = useText()
  const element = selected === null ? undefined : getAt(draft, selected)
  const content = isContent(element) ? element : undefined
  let panel
  if (selected !== null && content !== undefined) {
    if (fieldPointer.test(selected)) {
      panel = (
        <FieldProperties
          draft={draft}
          pointer={selected}
          field={content}
          entityFields={entityFields}
          onChange={onChange}
        />
      )
    } else if (sectionPointer.test(selected)) {
      panel = (
        <SectionProperties
          draft={draft}
          pointer={selected}
          section={content}
          onSelect={onSelect}
          onAnnounce={onAnnounce}
          onChange={onChange}
        />
      )
    } else if (stepPointer.test(selected)) {
      panel = <StepProperties pointer={selected} step={content} onChange={onChange} />
    }
  }
  return (
    <Flex vertical gap="small" aria-label={t('shell.studio.design.properties')} role="region">
      {panel ?? (
        <Typography.Text type="secondary" data-testid="studio-design-hint">
          {t('shell.studio.design.selectFormElement')}
        </Typography.Text>
      )}
    </Flex>
  )
}

function FieldProperties({
  draft,
  pointer,
  field,
  entityFields,
  onChange,
}: {
  draft: Content
  pointer: string
  field: Content
  entityFields: FieldMetadata[]
  onChange: ChangeHandler
}) {
  const t = useText()
  const name = typeof field.field === 'string' ? field.field : ''
  const entityField = entityFields.find((candidate) => candidate.name.toLowerCase() === name.toLowerCase())
  const locked = entityField !== undefined && (entityField.computed || entityField.sequence || entityField.creator)
  const conditions: Condition[] = entityField?.required
    ? ['requiredWhen', 'readOnlyWhen']
    : ['visibleWhen', 'requiredWhen', 'readOnlyWhen']
  return (
    <Flex vertical gap="small" data-testid="studio-design-field">
      <Typography.Text strong>{name}</Typography.Text>
      <div>
        <Checkbox
          aria-label={t('shell.studio.design.readOnly')}
          checked={locked || field.readOnly === true}
          disabled={locked}
          onChange={(event) => onChange(`${pointer}/readOnly`, event.target.checked)}
        >
          {t('shell.studio.design.readOnly')}
        </Checkbox>
        {locked && (
          <div>
            <Typography.Text type="secondary">{t('shell.studio.design.readOnlyLocked')}</Typography.Text>
          </div>
        )}
      </div>
      {conditions.map((condition) => (
        <ConditionBox
          key={condition}
          draft={draft}
          pointer={pointer}
          element={field}
          condition={condition}
          onChange={onChange}
        />
      ))}
    </Flex>
  )
}

function SectionProperties({
  draft,
  pointer,
  section,
  onSelect,
  onAnnounce,
  onChange,
}: {
  draft: Content
  pointer: string
  section: Content
  onSelect: (pointer: string) => void
  onAnnounce: (text: string) => void
  onChange: ChangeHandler
}) {
  const t = useText()
  const steps = Array.isArray(draft.steps) ? (draft.steps as unknown[]) : []
  const name = typeof section.name === 'string' ? section.name.toLowerCase() : ''
  const current = steps.findIndex(
    (step) =>
      isContent(step) &&
      Array.isArray(step.sections) &&
      step.sections.some((item) => typeof item === 'string' && item.toLowerCase() === name),
  )
  return (
    <Flex vertical gap="small" data-testid="studio-design-section">
      <SectionNameBox
        key={`${pointer}:${typeof section.name === 'string' ? section.name : ''}`}
        draft={draft}
        pointer={pointer}
        section={section}
        onChange={onChange}
      />
      <TitleEditor pointer={pointer} element={section} onChange={onChange} />
      {steps.length > 0 && name !== '' && (
        <div>
          <div>{t('shell.studio.design.moveToStep')}</div>
          <Select
            aria-label={t('shell.studio.design.moveToStep')}
            data-testid="studio-design-move-to-step"
            style={{ width: '100%' }}
            value={current >= 0 ? current : undefined}
            options={steps.map((step, index) => {
              const title = isContent(step) && isContent(step.title) ? step.title.textKey : undefined
              return { value: index, label: typeof title === 'string' && title !== '' ? t(title) : `/steps/${index}` }
            })}
            onChange={(index: number) =>
              moveSectionTo(
                t,
                draft,
                Number(pointer.slice('/sections/'.length)),
                { step: index, index: Number.MAX_SAFE_INTEGER },
                onChange,
                onSelect,
                onAnnounce,
              )
            }
          />
        </div>
      )}
      <ConditionBox draft={draft} pointer={pointer} element={section} condition="visibleWhen" onChange={onChange} />
    </Flex>
  )
}

/**
 * The name of a section. A rename applies on Enter and when the box loses focus, and renames the step entries in the
 * same edit. A refused name stays in the box with its message. The key resets the box on a new selection or an undo.
 */
function SectionNameBox({
  draft,
  pointer,
  section,
  onChange,
}: {
  draft: Content
  pointer: string
  section: Content
  onChange: ChangeHandler
}) {
  const t = useText()
  const [text, setText] = useState(typeof section.name === 'string' ? section.name : '')
  const [refused, setRefused] = useState<'empty' | 'taken' | null>(null)
  const commit = () => {
    const result = renameSection(draft, Number(pointer.slice('/sections/'.length)), text)
    if (result === undefined) {
      setRefused(null)
      return
    }
    if ('refused' in result) {
      setRefused(result.refused)
      return
    }
    setRefused(null)
    onChange('/sections', result.sections)
    if (result.steps !== undefined) {
      onChange('/steps', result.steps)
    }
  }
  return (
    <div>
      <div>{t('shell.studio.design.name')}</div>
      <Input
        aria-label={t('shell.studio.design.name')}
        data-testid="studio-design-section-name"
        status={refused === null ? undefined : 'error'}
        value={text}
        onChange={(event) => setText(event.target.value)}
        onPressEnter={commit}
        onBlur={commit}
      />
      {refused !== null && (
        <Typography.Text type="danger">
          {t(refused === 'taken' ? 'shell.studio.design.nameTaken' : 'shell.studio.design.nameEmpty')}
        </Typography.Text>
      )}
    </div>
  )
}

function StepProperties({ pointer, step, onChange }: { pointer: string; step: Content; onChange: ChangeHandler }) {
  return (
    <Flex vertical gap="small" data-testid="studio-design-step">
      <TitleEditor pointer={pointer} element={step} onChange={onChange} />
    </Flex>
  )
}

/** The title of a section or a step. The label editor keeps its key in state, so the pointer is its key. */
function TitleEditor({ pointer, element, onChange }: { pointer: string; element: Content; onChange: ChangeHandler }) {
  const t = useText()
  const names = useContext(StudioNamesContext)
  const label = useContext(LabelTextsContext)
  const title = `${pointer}/title`
  const textKey = isContent(element.title) && typeof element.title.textKey === 'string' ? element.title.textKey : ''
  return (
    <div data-testid={`studio-field-${title}`}>
      <div>{t('shell.studio.design.title')}</div>
      {names !== undefined && label !== null ? (
        <LabelTexts key={title} pointer={title} value={element.title} onChange={onChange} />
      ) : (
        <Input
          aria-label={t('shell.studio.design.title')}
          value={textKey}
          onChange={(event) => onChange(title, { textKey: event.target.value })}
        />
      )}
    </div>
  )
}

/** An expression box for a condition of a field or a section. It offers only the fields the form lists. */
function ConditionBox({
  draft,
  pointer,
  element,
  condition,
  onChange,
}: {
  draft: Content
  pointer: string
  element: Content
  condition: Condition
  onChange: ChangeHandler
}) {
  const t = useText()
  const check = useContext(ExpressionCheckContext)
  const path = `${pointer}/${condition}`
  const name = t(`shell.studio.design.${condition}`)
  const value = typeof element[condition] === 'string' ? element[condition] : ''
  return (
    <div data-testid={`studio-field-${path}`}>
      <div>{name}</div>
      {check && (
        <Suspense fallback={<Input aria-label={name} value={value} readOnly />}>
          <LazyExpressionEditor
            key={path}
            application={check.application}
            resourceId={check.resourceId}
            path={path}
            name={name}
            value={value}
            required={false}
            options={expressionScope('form', draft, path, check.names)}
            onChange={onChange}
          />
        </Suspense>
      )}
    </div>
  )
}
