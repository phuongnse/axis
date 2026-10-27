import { Button, Card, Flex, Input, Radio, Select, Switch, Typography } from 'antd'
import { useContext } from 'react'
import { StudioNamesContext } from '../platform/studioNames'
import type { StudioNames } from '../platform/studio'
import type { JsonSchema } from '../platform/studioSchemas'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { MappingRows } from './MappingRows'
import { NotifyFields } from './NotifyFields'
import { OperationPanel } from './OperationPanel'
import { operationJsonOnlyKeys } from './operationKeys'
import { Expression, NamePicker } from './ProcessFields'
import { requiredStepKeys } from './processPalette'
import { stepTypes, withType } from './processStepSchema'
import { ReferencePicker } from './ReferencePicker'
import { SchemaForm, type ChangeHandler } from './SchemaForm'

type Content = Record<string, unknown>

/** The step types that have a panel. The keys of any other type show as markers for the JSON view. */
const panelTypes = new Set(['decision', 'task', 'operation', 'timer', 'subProcess', 'end'])

const parameterTypes = ['text', 'integer', 'decimal', 'boolean', 'date', 'date-time']

function isContent(value: unknown): value is Content {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function text(value: unknown): string {
  return typeof value === 'string' ? value : ''
}

/**
 * The process with its subject switched: the other key goes and the new key, empty, takes its place, so the key order
 * of the file holds. A process with neither key gets the new key last.
 */
function withSubject(process: Content, subject: 'entity' | 'parameters'): Content {
  const other = subject === 'entity' ? 'parameters' : 'entity'
  const initial = subject === 'entity' ? '' : []
  const next: [string, unknown][] = []
  let placed = false
  for (const [key, value] of Object.entries(process)) {
    if (key === other && !placed) {
      next.push([subject, initial])
      placed = true
    } else if (key !== other && key !== subject) {
      next.push([key, value])
    }
  }
  if (!placed) {
    next.push([subject, initial])
  }
  return Object.fromEntries(next)
}

/** The distinct, non-empty step names of a draft, for the pickers of every transition. */
function stepNames(steps: unknown[]): string[] {
  return [
    ...new Set(
      steps.flatMap((step) =>
        isContent(step) && typeof step.name === 'string' && step.name !== '' ? [step.name] : [],
      ),
    ),
  ]
}

/** The steps with the step at `index` swapped with its neighbour `offset` places away. */
function moved(steps: unknown[], index: number, offset: number): unknown[] {
  const next = [...steps]
  ;[next[index], next[index + offset]] = [next[index + offset], next[index]]
  return next
}

/**
 * The editor of a process. It shows the process's name, entity and start condition with the generic form, the start
 * step, and the steps as cards in file order. A decision shows its branches and its `otherwise`, an end shows only
 * its name and type, and every transition is a picker over the step names of the draft. A task shows its full panel:
 * label, assignee, form, due time, deadline, escalations and outcomes. An operation shows its operation panel and its
 * `next`. The editor edits the draft through `onChange`, so a key it does not show stays as it is, and removing a step leaves the transitions that name it for
 * the compiler to report.
 */
export function ProcessEditor({
  schema,
  value,
  onChange,
}: {
  schema: JsonSchema
  value: Content
  onChange: ChangeHandler
}) {
  const t = useText()
  const steps = Array.isArray(value.steps) ? value.steps : []
  const names = stepNames(steps)

  return (
    <Flex vertical gap="middle" data-testid="studio-process-editor">
      <SchemaForm schema={withoutSubject(schema)} value={value} onChange={onChange} />
      <Subject value={value} onChange={onChange} />
      <div data-testid="studio-field-/start">
        <div>{t('shell.studio.process.start')}</div>
        <ReferencePicker
          name={t('shell.studio.process.start')}
          value={text(value.start)}
          required
          options={names}
          onChange={(next) => onChange('/start', next ?? '')}
        />
      </div>
      <div data-testid="studio-field-/steps">
        <Flex vertical gap="small">
          {steps.map((step, index) => (
            <Step
              key={index}
              schema={schema}
              index={index}
              step={isContent(step) ? step : {}}
              entity={text(value.entity)}
              names={names}
              last={index === steps.length - 1}
              onMove={(offset) => onChange('/steps', moved(steps, index, offset))}
              onRemove={() =>
                onChange(
                  '/steps',
                  steps.filter((_, position) => position !== index),
                )
              }
              onChange={onChange}
            />
          ))}
          <div>
            <Select
              aria-label={t('shell.studio.process.addStep')}
              placeholder={t('shell.studio.process.addStep')}
              style={{ minWidth: 240 }}
              virtual={false}
              value={null}
              options={stepTypes(schema).map((type) => ({ value: type, label: type }))}
              onChange={(type: string) =>
                onChange('/steps', [...steps, { name: '', type, ...structuredClone(requiredStepKeys[type]) }])
              }
            />
          </div>
        </Flex>
      </div>
    </Flex>
  )
}

/** The schema without `entity` and `parameters`, which the Subject choice edits. */
function withoutSubject(schema: JsonSchema): JsonSchema {
  const { entity: _entity, parameters: _parameters, ...properties } = schema.properties ?? {}
  return {
    ...schema,
    properties,
    required: schema.required?.filter((name) => name !== 'entity' && name !== 'parameters'),
  }
}

/**
 * The subject of a process: a choice of an entity or parameters. A switch replaces the draft, so that it holds only the
 * chosen key, and one undo step brings the other back.
 */
function Subject({ value, onChange }: { value: Content; onChange: ChangeHandler }) {
  const t = useText()
  const mode = value.parameters !== undefined && value.entity === undefined ? 'parameters' : 'entity'
  const parameters = Array.isArray(value.parameters) ? value.parameters : []

  return (
    // The entity picker carries the `/entity` field marker itself.
    <div data-testid={mode === 'parameters' ? 'studio-field-/parameters' : undefined}>
      <Flex vertical gap="small">
        <Typography.Text strong>{t('shell.studio.process.subject')}</Typography.Text>
        <Radio.Group
          // A name of its own keeps the assignee radios of a task from joining this group when ids are not unique.
          name="process-subject"
          optionType="button"
          aria-label={t('shell.studio.process.subject')}
          value={mode}
          options={[
            { value: 'entity', label: t('shell.studio.process.subjectEntity') },
            { value: 'parameters', label: t('shell.studio.process.subjectParameters') },
          ]}
          onChange={(event) => {
            if (event.target.value !== mode) {
              onChange('', withSubject(value, event.target.value))
            }
          }}
        />
        {mode === 'entity' ? (
          <NamePicker
            pointer="/entity"
            label={t('shell.studio.process.subjectEntity')}
            kind="entity"
            value={value.entity}
            onPick={(next) => onChange('/entity', next)}
          />
        ) : (
          <>
            {parameters.map((parameter, index) => {
              const pointer = `/parameters/${index}`
              const content = isContent(parameter) ? parameter : {}
              return (
                <Flex key={index} gap="small" align="flex-start" wrap data-testid={`studio-field-${pointer}`}>
                  <div data-testid={`studio-field-${pointer}/name`}>
                    <div>{t('shell.studio.process.parameterName')}</div>
                    <Input
                      aria-label={t('shell.studio.process.parameterName')}
                      value={text(content.name)}
                      onChange={(event) => onChange(`${pointer}/name`, event.target.value)}
                    />
                  </div>
                  <div data-testid={`studio-field-${pointer}/type`}>
                    <div>{t('shell.studio.process.parameterType')}</div>
                    <Select
                      aria-label={t('shell.studio.process.parameterType')}
                      style={{ minWidth: 160 }}
                      virtual={false}
                      value={typeof content.type === 'string' ? content.type : undefined}
                      options={parameterTypes.map((type) => ({ value: type, label: type }))}
                      onChange={(next: string) => onChange(`${pointer}/type`, next)}
                    />
                  </div>
                  <Button onClick={() => onChange(pointer, undefined)}>
                    {t('shell.studio.process.removeParameter')}
                  </Button>
                </Flex>
              )
            })}
            <div>
              <Button onClick={() => onChange('/parameters', [...parameters, { name: '', type: 'text' }])}>
                {t('shell.studio.process.addParameter')}
              </Button>
            </div>
          </>
        )}
      </Flex>
    </div>
  )
}

/**
 * The card of one step of a process, as the Form tab shows it, for the Design tab's side panel. Move up and Move down
 * select the step at its new place. Remove calls `onRemove`, which owns the removal, the selection and what is said.
 */
export function ProcessStepPanel({
  schema,
  value,
  index,
  onChange,
  onSelect,
  onRemove,
}: {
  schema: JsonSchema
  value: Content
  index: number
  onChange: ChangeHandler
  onSelect: (index: number | null) => void
  onRemove: () => void
}) {
  const steps = Array.isArray(value.steps) ? value.steps : []
  const step = steps[index]

  return (
    <Step
      schema={schema}
      index={index}
      step={isContent(step) ? step : {}}
      entity={text(value.entity)}
      names={stepNames(steps)}
      last={index === steps.length - 1}
      onMove={(offset) => {
        onChange('/steps', moved(steps, index, offset))
        onSelect(index + offset)
      }}
      onRemove={onRemove}
      onChange={onChange}
    />
  )
}

function Step({
  schema,
  index,
  step,
  entity,
  names,
  last,
  onMove,
  onRemove,
  onChange,
}: {
  schema: JsonSchema
  index: number
  step: Content
  entity: string
  names: string[]
  last: boolean
  onMove: (offset: number) => void
  onRemove: () => void
  onChange: ChangeHandler
}) {
  const t = useText()
  const pointer = `/steps/${index}`
  const type = typeof step.type === 'string' ? step.type : undefined

  return (
    <div data-testid={`studio-field-${pointer}`}>
      <Card
        size="small"
        data-testid={`studio-step-${index}`}
        title={`${text(step.name)} · ${type ?? ''}`}
        extra={
          <Flex gap="small">
            <Button disabled={index === 0} onClick={() => onMove(-1)}>
              {t('shell.studio.process.moveUp')}
            </Button>
            <Button disabled={last} onClick={() => onMove(1)}>
              {t('shell.studio.process.moveDown')}
            </Button>
            <Button danger onClick={onRemove}>
              {t('shell.studio.process.removeStep')}
            </Button>
          </Flex>
        }
      >
        <Flex vertical gap="small">
          <div data-testid={`studio-field-${pointer}/name`}>
            <div>{t('shell.studio.process.stepName')}</div>
            <Input
              aria-label={t('shell.studio.process.stepName')}
              value={text(step.name)}
              onChange={(event) => onChange(`${pointer}/name`, event.target.value)}
            />
          </div>
          <div data-testid={`studio-field-${pointer}/type`}>
            <div>{t('shell.studio.process.stepType')}</div>
            <Select
              aria-label={t('shell.studio.process.stepType')}
              style={{ minWidth: 240 }}
              virtual={false}
              value={type}
              options={stepTypes(schema).map((option) => ({ value: option, label: option }))}
              onChange={(next: string) => onChange(pointer, withType(step, next, schema))}
            />
          </div>
          {type === 'decision' && <Decision pointer={pointer} step={step} names={names} onChange={onChange} />}
          {type === 'operation' && (
            <>
              <OperationPanel pointer={pointer} step={step} entity={entity} onChange={onChange} />
              <Transition
                pointer={`${pointer}/next`}
                label={t('shell.studio.process.next')}
                value={step.next}
                names={names}
                onChange={onChange}
              />
              <JsonOnlyKeys pointer={pointer} keys={operationJsonOnlyKeys(step)} />
            </>
          )}
          {type === 'task' && <Task pointer={pointer} step={step} names={names} onChange={onChange} />}
          {type === 'timer' && <Timer pointer={pointer} step={step} names={names} onChange={onChange} />}
          {type === 'subProcess' && <SubProcess pointer={pointer} step={step} names={names} onChange={onChange} />}
          {type !== undefined && !panelTypes.has(type) && (
            <JsonOnlyKeys
              pointer={pointer}
              keys={Object.keys(step).filter((key) => key !== 'name' && key !== 'type')}
            />
          )}
        </Flex>
      </Card>
    </div>
  )
}

/** A picker over the step names for one transition. A target that is not a step stays visible and is tagged unknown. */
function Transition({
  pointer,
  label,
  value,
  names,
  onChange,
}: {
  pointer: string
  label: string
  value: unknown
  names: string[]
  onChange: ChangeHandler
}) {
  return (
    <div data-testid={`studio-field-${pointer}`}>
      <div>{label}</div>
      <ReferencePicker
        name={label}
        value={text(value)}
        required
        options={names}
        onChange={(next) => onChange(pointer, next ?? '')}
      />
    </div>
  )
}

function JsonOnlyKeys({ pointer, keys }: { pointer: string; keys: string[] }) {
  const t = useText()
  return keys.map((key) => (
    <div key={key} data-testid={`studio-field-${pointer}/${key}`}>
      <div>{key}</div>
      <Typography.Text type="secondary" data-testid="studio-json-only">
        {t('shell.studio.editInJson')}
      </Typography.Text>
    </div>
  ))
}

/**
 * The editor of an assignee: a user expression or a role, and a switch replaces the object at `pointer` so that it holds
 * only the chosen key. A task's `assignee` and an escalation's `reassign` both use it. The radios have a name of their
 * own per pointer, so two editors in one panel never join one group.
 */
function AssigneeEditor({
  pointer,
  value,
  label,
  onChange,
}: {
  pointer: string
  value: unknown
  label: string
  onChange: ChangeHandler
}) {
  const t = useText()
  const assignee = isContent(value) ? value : {}
  const mode = assignee.role !== undefined && assignee.user === undefined ? 'role' : 'user'

  function switchTo(next: 'user' | 'role') {
    const { user: _user, role: _role, ...others } = assignee
    onChange(pointer, { ...others, [next]: text(assignee[next]) })
  }

  return (
    <div data-testid={`studio-field-${pointer}`}>
      <Flex vertical gap="small">
        <Typography.Text strong>{label}</Typography.Text>
        <Radio.Group
          name={`assignee-mode-${pointer}`}
          optionType="button"
          aria-label={label}
          value={mode}
          options={[
            { value: 'user', label: t('shell.studio.process.assigneeUser') },
            { value: 'role', label: t('shell.studio.process.assigneeRole') },
          ]}
          onChange={(event) => switchTo(event.target.value)}
        />
        {mode === 'user' ? (
          <div data-testid={`studio-field-${pointer}/user`}>
            <Expression
              pointer={`${pointer}/user`}
              name={t('shell.studio.process.assigneeUser')}
              value={text(assignee.user)}
              onChange={onChange}
            />
          </div>
        ) : (
          <NamePicker
            pointer={`${pointer}/role`}
            label={t('shell.studio.process.assigneeRole')}
            kind="role"
            value={assignee.role}
            onPick={(next) => onChange(`${pointer}/role`, next)}
          />
        )}
      </Flex>
    </div>
  )
}

/**
 * The panel of a task: its label, assignee, form, due time, deadline, escalations and outcomes. Each value is edited
 * through its own pointer, so a key the panel does not show stays as it is. The assignee is a user expression or a
 * role, and a switch replaces the `assignee` object so that it holds only the chosen key.
 */
function Task({
  pointer,
  step,
  names,
  onChange,
}: {
  pointer: string
  step: Content
  names: string[]
  onChange: ChangeHandler
}) {
  const t = useText()
  const label = isContent(step.label) ? step.label : {}

  return (
    <>
      <NamePicker
        pointer={`${pointer}/label/textKey`}
        label={t('shell.studio.process.label')}
        kind="text"
        value={label.textKey}
        onPick={(next) => onChange(`${pointer}/label/textKey`, next)}
      />
      <AssigneeEditor
        pointer={`${pointer}/assignee`}
        value={step.assignee}
        label={t('shell.studio.process.assignee')}
        onChange={onChange}
      />
      <NamePicker
        pointer={`${pointer}/form`}
        label={t('shell.studio.process.form')}
        kind="form"
        value={step.form}
        onPick={(next) => onChange(`${pointer}/form`, next)}
      />
      <div data-testid={`studio-field-${pointer}/dueIn`}>
        <div>{t('shell.studio.process.dueIn')}</div>
        <Input
          aria-label={t('shell.studio.process.dueIn')}
          value={text(step.dueIn)}
          onChange={(event) => onChange(`${pointer}/dueIn`, event.target.value === '' ? undefined : event.target.value)}
        />
      </div>
      <Deadline pointer={pointer} step={step} names={names} onChange={onChange} />
      <Escalations pointer={pointer} step={step} onChange={onChange} />
      <Outcomes pointer={pointer} step={step} names={names} onChange={onChange} />
    </>
  )
}

/**
 * The optional deadline of a task: how long after the task opens it runs out, and the step it moves to. Adding it
 * writes empty values for the diagnostics to mark, and removing it removes the key.
 */
function Deadline({
  pointer,
  step,
  names,
  onChange,
}: {
  pointer: string
  step: Content
  names: string[]
  onChange: ChangeHandler
}) {
  const t = useText()
  const deadlinePointer = `${pointer}/deadline`
  if (step.deadline !== undefined && !isContent(step.deadline)) {
    return <JsonOnlyKeys pointer={pointer} keys={['deadline']} />
  }
  const deadline = step.deadline

  return (
    <div data-testid={`studio-field-${deadlinePointer}`}>
      <Flex vertical gap="small">
        <Typography.Text strong>{t('shell.studio.process.deadline')}</Typography.Text>
        {deadline === undefined ? (
          <div>
            <Button onClick={() => onChange(deadlinePointer, { after: '', next: '' })}>
              {t('shell.studio.process.addDeadline')}
            </Button>
          </div>
        ) : (
          <Flex gap="small" align="flex-start" wrap>
            <div data-testid={`studio-field-${deadlinePointer}/after`}>
              <div>{t('shell.studio.process.deadlineAfter')}</div>
              <Input
                aria-label={t('shell.studio.process.deadlineAfter')}
                value={text(deadline.after)}
                onChange={(event) => onChange(`${deadlinePointer}/after`, event.target.value)}
              />
            </div>
            <Transition
              pointer={`${deadlinePointer}/next`}
              label={t('shell.studio.process.next')}
              value={deadline.next}
              names={names}
              onChange={onChange}
            />
            <Button onClick={() => onChange(deadlinePointer, undefined)}>
              {t('shell.studio.process.removeDeadline')}
            </Button>
          </Flex>
        )}
      </Flex>
    </div>
  )
}

/** The notify an escalation starts with, the same as the palette's empty `notify` step. */
const emptyNotify = { to: [], channels: [], message: { subject: { textKey: '' }, body: { textKey: '' } } }

/**
 * The escalations of a task as rows. Each has an `after` duration, and Reassign and Notify switches that add or remove
 * its `reassign` and `notify`. A new escalation holds only an empty `after`, which the diagnostics mark until one of
 * the switches is on. Removing the last escalation removes the `escalations` key.
 */
function Escalations({ pointer, step, onChange }: { pointer: string; step: Content; onChange: ChangeHandler }) {
  const t = useText()
  const { colour, radius, spacing } = useDesignTokens()
  const escalationsPointer = `${pointer}/escalations`
  if (step.escalations !== undefined && !Array.isArray(step.escalations)) {
    return <JsonOnlyKeys pointer={pointer} keys={['escalations']} />
  }
  const escalations: unknown[] = step.escalations ?? []

  return (
    <div data-testid={`studio-field-${escalationsPointer}`}>
      <Flex vertical gap="small">
        <Typography.Text strong>{t('shell.studio.process.escalations')}</Typography.Text>
        {escalations.map((escalation, index) => {
          const rowPointer = `${escalationsPointer}/${index}`
          const row = isContent(escalation) ? escalation : {}
          return (
            <Flex
              key={index}
              vertical
              gap="small"
              style={{ border: `1px solid ${colour.border}`, borderRadius: radius.base, padding: spacing.xs }}
              data-testid={`studio-field-${rowPointer}`}
            >
              <Flex gap="small" align="flex-end" wrap>
                <div data-testid={`studio-field-${rowPointer}/after`}>
                  <div>{t('shell.studio.process.escalationAfter')}</div>
                  <Input
                    aria-label={t('shell.studio.process.escalationAfter')}
                    value={text(row.after)}
                    onChange={(event) => onChange(`${rowPointer}/after`, event.target.value)}
                  />
                </div>
                <Button onClick={() => onChange(escalations.length === 1 ? escalationsPointer : rowPointer, undefined)}>
                  {t('shell.studio.process.removeEscalation')}
                </Button>
              </Flex>
              <Flex gap="small" align="center">
                <Switch
                  aria-label={t('shell.studio.process.reassign')}
                  checked={row.reassign !== undefined}
                  onChange={(checked) => onChange(`${rowPointer}/reassign`, checked ? { user: '' } : undefined)}
                />
                <span>{t('shell.studio.process.reassign')}</span>
              </Flex>
              {row.reassign !== undefined &&
                (isContent(row.reassign) ? (
                  <AssigneeEditor
                    pointer={`${rowPointer}/reassign`}
                    value={row.reassign}
                    label={t('shell.studio.process.reassign')}
                    onChange={onChange}
                  />
                ) : (
                  <JsonOnlyKeys pointer={rowPointer} keys={['reassign']} />
                ))}
              <Flex gap="small" align="center">
                <Switch
                  aria-label={t('shell.studio.process.notify')}
                  checked={row.notify !== undefined}
                  onChange={(checked) =>
                    onChange(`${rowPointer}/notify`, checked ? structuredClone(emptyNotify) : undefined)
                  }
                />
                <span>{t('shell.studio.process.notify')}</span>
              </Flex>
              {row.notify !== undefined &&
                (isContent(row.notify) ? (
                  <NotifyFields pointer={`${rowPointer}/notify`} value={row.notify} onChange={onChange} />
                ) : (
                  <JsonOnlyKeys pointer={rowPointer} keys={['notify']} />
                ))}
            </Flex>
          )
        })}
        <div>
          <Button onClick={() => onChange(escalationsPointer, [...escalations, { after: '' }])}>
            {t('shell.studio.process.addEscalation')}
          </Button>
        </div>
      </Flex>
    </div>
  )
}

/**
 * The panel of a timer: Wait for a duration or Until a time, and the next step. A switch replaces the other key in
 * place with the chosen key, which keeps a value it already holds. A timer with neither key or both opens in Wait.
 */
function Timer({
  pointer,
  step,
  names,
  onChange,
}: {
  pointer: string
  step: Content
  names: string[]
  onChange: ChangeHandler
}) {
  const t = useText()
  const mode = step.until !== undefined && step.wait === undefined ? 'until' : 'wait'

  function switchTo(chosen: 'wait' | 'until') {
    const other = chosen === 'wait' ? 'until' : 'wait'
    const next: [string, unknown][] = []
    let placed = false
    for (const [key, value] of Object.entries(step)) {
      if (key === other && !placed) {
        next.push([chosen, text(step[chosen])])
        placed = true
      } else if (key !== other && key !== chosen) {
        next.push([key, value])
      }
    }
    if (!placed) {
      next.push([chosen, ''])
    }
    onChange(pointer, Object.fromEntries(next))
  }

  return (
    <>
      <div data-testid={`studio-field-${pointer}/timer`}>
        <Flex vertical gap="small">
          <Typography.Text strong>{t('shell.studio.process.timerMode')}</Typography.Text>
          <Radio.Group
            // A name of its own per step keeps the radios of two timers from joining one group.
            name={`timer-mode-${pointer}`}
            optionType="button"
            aria-label={t('shell.studio.process.timerMode')}
            value={mode}
            options={[
              { value: 'wait', label: t('shell.studio.process.timerWait') },
              { value: 'until', label: t('shell.studio.process.timerUntil') },
            ]}
            onChange={(event) => switchTo(event.target.value)}
          />
          {mode === 'wait' ? (
            <div data-testid={`studio-field-${pointer}/wait`}>
              <div>{t('shell.studio.process.waitFor')}</div>
              <Input
                aria-label={t('shell.studio.process.waitFor')}
                value={text(step.wait)}
                onChange={(event) => onChange(`${pointer}/wait`, event.target.value)}
              />
            </div>
          ) : (
            <div data-testid={`studio-field-${pointer}/until`}>
              <div>{t('shell.studio.process.untilTime')}</div>
              <Expression
                pointer={`${pointer}/until`}
                name={t('shell.studio.process.untilTime')}
                value={text(step.until)}
                onChange={onChange}
              />
            </div>
          )}
        </Flex>
      </div>
      <Transition
        pointer={`${pointer}/next`}
        label={t('shell.studio.process.next')}
        value={step.next}
        names={names}
        onChange={onChange}
      />
    </>
  )
}

type ChildProcess = NonNullable<StudioNames['processes']>[number]

/**
 * The step with `process` set to the chosen child. A child with a subject entity drops `parameters`. A child without
 * one drops `subject` and gets one parameter per child parameter, in the child's order, each keeping a value the step
 * already holds for that name. The keys swap in place. An unknown child changes only `process`.
 */
function withChild(step: Content, process: string, child: ChildProcess | undefined): Content {
  if (child === undefined) {
    return { ...step, process }
  }
  const held = isContent(step.parameters) ? Object.entries(step.parameters) : []
  const parameters = Object.fromEntries(
    child.parameters.map((parameter) => {
      const value = held.find(([key]) => key.toLowerCase() === parameter.name.toLowerCase())?.[1]
      return [parameter.name, typeof value === 'string' ? value : '']
    }),
  )
  if (child.entity !== null) {
    return { ...Object.fromEntries(Object.entries(step).filter(([key]) => key !== 'parameters')), process }
  }
  const next: [string, unknown][] = []
  let placed = false
  for (const [key, value] of Object.entries({ ...step, process })) {
    if (key === 'subject' || key === 'parameters') {
      if (!placed) {
        next.push(['parameters', parameters])
        placed = true
      }
    } else {
      next.push([key, value])
    }
  }
  if (!placed) {
    const at = next.findIndex(([key]) => key === 'process') + 1
    next.splice(at, 0, ['parameters', parameters])
  }
  return Object.fromEntries(next)
}

/**
 * The panel of a sub-process step: the child process, then its inputs and the next step. A child with a subject entity
 * takes a `subject` expression, and a child without one takes an expression for each of its parameters. When the child
 * is not in the names list, the keys the draft already holds decide which inputs show.
 */
function SubProcess({
  pointer,
  step,
  names,
  onChange,
}: {
  pointer: string
  step: Content
  names: string[]
  onChange: ChangeHandler
}) {
  const t = useText()
  const studioNames = useContext(StudioNamesContext)
  const childOf = (name: string) =>
    studioNames?.processes.find((process) => process.name.toLowerCase() === name.toLowerCase())
  const child = childOf(text(step.process))
  const mode = child
    ? child.entity === null
      ? 'parameters'
      : 'subject'
    : step.parameters !== undefined && step.subject === undefined
      ? 'parameters'
      : 'subject'

  return (
    <>
      <NamePicker
        pointer={`${pointer}/process`}
        label={t('shell.studio.process.childProcess')}
        kind="process"
        value={step.process}
        onPick={(next) => onChange(pointer, withChild(step, next, childOf(next)))}
      />
      {mode === 'subject' ? (
        <div data-testid={`studio-field-${pointer}/subject`}>
          <div>{t('shell.studio.process.subjectExpression')}</div>
          <Expression
            pointer={`${pointer}/subject`}
            name={t('shell.studio.process.subjectExpression')}
            value={text(step.subject)}
            onChange={(changed, value) => onChange(changed, value === '' ? undefined : value)}
          />
        </div>
      ) : (
        <MappingRows
          pointer={`${pointer}/parameters`}
          title={t('shell.studio.process.childParameters')}
          value={step.parameters}
          keyLabel={t('shell.studio.process.parameterName')}
          keyOptions={child?.parameters.map((parameter) => parameter.name)}
          target={{ kind: 'expression' }}
          onChange={onChange}
        />
      )}
      <Transition
        pointer={`${pointer}/next`}
        label={t('shell.studio.process.next')}
        value={step.next}
        names={names}
        onChange={onChange}
      />
    </>
  )
}

function Decision({
  pointer,
  step,
  names,
  onChange,
}: {
  pointer: string
  step: Content
  names: string[]
  onChange: ChangeHandler
}) {
  const t = useText()
  const branchesPointer = `${pointer}/branches`
  const branches = Array.isArray(step.branches) ? step.branches : []

  return (
    <>
      <div data-testid={`studio-field-${branchesPointer}`}>
        <Flex vertical gap="small">
          <Typography.Text strong>{t('shell.studio.process.branches')}</Typography.Text>
          {branches.map((branch, index) => {
            const branchPointer = `${branchesPointer}/${index}`
            const whenPointer = `${branchPointer}/when`
            const when = text(isContent(branch) ? branch.when : undefined)
            const name = t('shell.studio.process.when')
            return (
              <Flex key={index} gap="small" align="flex-start" wrap data-testid={`studio-field-${branchPointer}`}>
                <div data-testid={`studio-field-${whenPointer}`} style={{ flex: 1, minWidth: 240 }}>
                  <div>{name}</div>
                  <Expression pointer={whenPointer} name={name} value={when} onChange={onChange} />
                </div>
                <Transition
                  pointer={`${branchPointer}/next`}
                  label={t('shell.studio.process.next')}
                  value={isContent(branch) ? branch.next : undefined}
                  names={names}
                  onChange={onChange}
                />
                <Button onClick={() => onChange(branchPointer, undefined)}>
                  {t('shell.studio.process.removeBranch')}
                </Button>
              </Flex>
            )
          })}
          <div>
            <Button onClick={() => onChange(branchesPointer, [...branches, { when: '', next: '' }])}>
              {t('shell.studio.process.addBranch')}
            </Button>
          </div>
        </Flex>
      </div>
      <Transition
        pointer={`${pointer}/otherwise`}
        label={t('shell.studio.process.otherwise')}
        value={step.otherwise}
        names={names}
        onChange={onChange}
      />
    </>
  )
}

/** The outcomes of a task as rows of name, label and target step. Each value is edited through its own pointer. */
function Outcomes({
  pointer,
  step,
  names,
  onChange,
}: {
  pointer: string
  step: Content
  names: string[]
  onChange: ChangeHandler
}) {
  const t = useText()
  const outcomesPointer = `${pointer}/outcomes`
  const outcomes = Array.isArray(step.outcomes) ? step.outcomes : []

  return (
    <div data-testid={`studio-field-${outcomesPointer}`}>
      <Flex vertical gap="small">
        <Typography.Text strong>{t('shell.studio.process.outcomes')}</Typography.Text>
        {outcomes.map((outcome, index) => {
          const outcomePointer = `${outcomesPointer}/${index}`
          const content = isContent(outcome) ? outcome : {}
          return (
            <Flex key={index} gap="small" align="flex-start" wrap data-testid={`studio-field-${outcomePointer}`}>
              <div data-testid={`studio-field-${outcomePointer}/name`}>
                <div>{t('shell.studio.process.outcomeName')}</div>
                <Input
                  aria-label={t('shell.studio.process.outcomeName')}
                  value={text(content.name)}
                  onChange={(event) => onChange(`${outcomePointer}/name`, event.target.value)}
                />
              </div>
              <NamePicker
                pointer={`${outcomePointer}/label/textKey`}
                label={t('shell.studio.process.outcomeLabel')}
                kind="text"
                value={isContent(content.label) ? content.label.textKey : undefined}
                onPick={(next) => onChange(`${outcomePointer}/label/textKey`, next)}
              />
              <Transition
                pointer={`${outcomePointer}/next`}
                label={t('shell.studio.process.next')}
                value={content.next}
                names={names}
                onChange={onChange}
              />
              <Button onClick={() => onChange(outcomePointer, undefined)}>
                {t('shell.studio.process.removeOutcome')}
              </Button>
            </Flex>
          )
        })}
        <div>
          <Button
            onClick={() => onChange(outcomesPointer, [...outcomes, { name: '', label: { textKey: '' }, next: '' }])}
          >
            {t('shell.studio.process.addOutcome')}
          </Button>
        </div>
      </Flex>
    </div>
  )
}
