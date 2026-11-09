import { Button, Checkbox, Flex, Input, InputNumber, Select, Typography } from 'antd'
import { useState } from 'react'
import { AxisField } from '../platform/AxisField'
import { AxisFieldGroup } from '../platform/AxisFieldGroup'
import { renameSection } from '../platform/formLayout'
import { getAt } from '../platform/jsonPatch'
import type { FieldMetadata } from '../platform/site'
import { useText } from '../platform/texts'
import { LabelTexts } from './LabelTexts'
import { moveSectionTo, removeField, removeSectionAt, removeStepAt } from './formPaletteSupport'
import { Expression } from './ProcessFields'
import type { ChangeHandler } from './SchemaForm'

type Content = Record<string, unknown>

type Condition = 'visibleWhen' | 'requiredWhen' | 'readOnlyWhen'

const fieldPointer = /^\/sections\/(\d+)\/fields\/(\d+)$/
const sectionPointer = /^\/sections\/\d+$/
const stepPointer = /^\/steps\/\d+$/

function isContent(value: unknown): value is Content {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

/** The label and the help of a form's input, from `shell.studio.field.form.<path>`, see docs/reference/frontend.md#platform-field-help. */
function fieldTexts(t: (key: string) => string, path: string) {
  return { label: t(`shell.studio.field.form.${path}`), help: t(`shell.studio.field.form.${path}.help`) }
}

/** The title and the help of a form's group of inputs, which has no one input to label. */
function groupTexts(t: (key: string) => string, path: string) {
  const { label, help } = fieldTexts(t, path)
  return { title: label, help }
}

/**
 * The properties of the element that is selected on the form canvas, found by its JSON Pointer in the draft. A field
 * has `readOnly`, its three conditions and a Remove field button, a section its name, title, `visibleWhen`, a Remove
 * section button and, on a form with steps, a Step picker, and a step its title and a Remove step button. A field the
 * entity marks required has no `visibleWhen` (AXC0226). A computed, sequence or creator field is always read-only. A selection that finds no object
 * in the draft shows a hint, so no edit goes to a stale pointer.
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
  /** Selects the element where a move or a removal lands, or nothing. */
  onSelect: (pointer: string | null) => void
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
          onSelect={onSelect}
          onAnnounce={onAnnounce}
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
      panel = (
        <StepProperties
          draft={draft}
          pointer={selected}
          step={content}
          onSelect={onSelect}
          onAnnounce={onAnnounce}
          onChange={onChange}
        />
      )
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
  onSelect,
  onAnnounce,
  onChange,
}: {
  draft: Content
  pointer: string
  field: Content
  entityFields: FieldMetadata[]
  onSelect: (pointer: string) => void
  onAnnounce: (text: string) => void
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
        <AxisField {...fieldTexts(t, 'sections.fields.readOnly')}>
          <Checkbox
            checked={locked || field.readOnly === true}
            disabled={locked}
            onChange={(event) => onChange(`${pointer}/readOnly`, event.target.checked)}
          />
        </AxisField>
        {locked && (
          <div>
            <Typography.Text type="secondary">{t('shell.studio.design.readOnlyLocked')}</Typography.Text>
          </div>
        )}
      </div>
      <AxisField {...fieldTexts(t, 'sections.fields.width')}>
        <InputNumber
          min={1}
          max={12}
          precision={0}
          placeholder="12"
          style={{ width: '100%' }}
          value={typeof field.width === 'number' ? field.width : null}
          onChange={(next) => onChange(`${pointer}/width`, typeof next === 'number' ? next : undefined)}
        />
      </AxisField>
      {conditions.map((condition) => (
        <ConditionBox
          key={condition}
          pointer={pointer}
          element={field}
          condition={condition}
          textPath="sections.fields"
          onChange={onChange}
        />
      ))}
      {/* oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role. */}
      <Button
        danger
        data-testid="studio-design-remove-field"
        onClick={() => {
          const match = fieldPointer.exec(pointer)
          if (match !== null) {
            removeField(
              t,
              draft,
              { section: Number(match[1]), index: Number(match[2]) },
              onChange,
              onSelect,
              onAnnounce,
            )
          }
        }}
      >
        {t('shell.studio.design.removeField')}
      </Button>
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
  onSelect: (pointer: string | null) => void
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
      <TitleEditor pointer={pointer} element={section} textPath="sections" onChange={onChange} />
      {steps.length > 0 && name !== '' && (
        <AxisField {...fieldTexts(t, 'sections.step')}>
          <Select
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
        </AxisField>
      )}
      <ConditionBox
        pointer={pointer}
        element={section}
        condition="visibleWhen"
        textPath="sections"
        onChange={onChange}
      />
      {/* oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role. */}
      <Button
        danger
        data-testid="studio-design-remove-section"
        onClick={() =>
          removeSectionAt(t, draft, Number(pointer.slice('/sections/'.length)), onChange, onSelect, onAnnounce)
        }
      >
        {t('shell.studio.design.removeSection')}
      </Button>
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
      <AxisField {...fieldTexts(t, 'sections.name')}>
        <Input
          data-testid="studio-design-section-name"
          status={refused === null ? undefined : 'error'}
          value={text}
          onChange={(event) => setText(event.target.value)}
          onPressEnter={commit}
          onBlur={commit}
        />
      </AxisField>
      {refused !== null && (
        <Typography.Text type="danger">
          {t(refused === 'taken' ? 'shell.studio.design.nameTaken' : 'shell.studio.design.nameEmpty')}
        </Typography.Text>
      )}
    </div>
  )
}

function StepProperties({
  draft,
  pointer,
  step,
  onSelect,
  onAnnounce,
  onChange,
}: {
  draft: Content
  pointer: string
  step: Content
  onSelect: (pointer: string | null) => void
  onAnnounce: (text: string) => void
  onChange: ChangeHandler
}) {
  const t = useText()
  return (
    <Flex vertical gap="small" data-testid="studio-design-step">
      <TitleEditor pointer={pointer} element={step} textPath="steps" onChange={onChange} />
      {/* oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role. */}
      <Button
        danger
        data-testid="studio-design-remove-step"
        onClick={() => removeStepAt(t, draft, Number(pointer.slice('/steps/'.length)), onChange, onSelect, onAnnounce)}
      >
        {t('shell.studio.design.removeStep')}
      </Button>
    </Flex>
  )
}

/**
 * The title of a section or a step, under `shell.studio.field.form.<textPath>.title`. The label editor keeps its key in
 * state, so the pointer is its key.
 */
function TitleEditor({
  pointer,
  element,
  textPath,
  onChange,
}: {
  pointer: string
  element: Content
  textPath: string
  onChange: ChangeHandler
}) {
  const t = useText()
  const title = `${pointer}/title`
  return (
    <div data-testid={`studio-field-${title}`}>
      <AxisFieldGroup {...groupTexts(t, `${textPath}.title`)}>
        <LabelTexts key={title} pointer={title} value={element.title} onChange={onChange} />
      </AxisFieldGroup>
    </div>
  )
}

/**
 * An expression box for a condition of a field or a section, under `shell.studio.field.form.<textPath>.<condition>`.
 * It offers only the fields the form lists. The key resets the box when the element changes.
 */
function ConditionBox({
  pointer,
  element,
  condition,
  textPath,
  onChange,
}: {
  pointer: string
  element: Content
  condition: Condition
  textPath: string
  onChange: ChangeHandler
}) {
  const t = useText()
  const path = `${pointer}/${condition}`
  const texts = fieldTexts(t, `${textPath}.${condition}`)
  const value = typeof element[condition] === 'string' ? element[condition] : ''
  return (
    <div key={path} data-testid={`studio-field-${path}`}>
      <AxisField {...texts}>
        <Expression pointer={path} name={texts.label} value={value} required={false} onChange={onChange} />
      </AxisField>
    </div>
  )
}
