import { Button, Card, Flex, Input, Radio, Select, Typography } from 'antd'
import { lazy, Suspense, useContext } from 'react'
import { referenceOptions, StudioNamesContext } from '../platform/studioNames'
import type { JsonSchema } from '../platform/studioSchemas'
import { useText } from '../platform/texts'
import { ExpressionCheckContext } from './expressionCheckContext'
import { expressionScope } from './expressionScope'
import { OperationPanel } from './OperationPanel'
import { operationJsonOnlyKeys } from './operationKeys'
import { ReferencePicker } from './ReferencePicker'
import { SchemaForm, type ChangeHandler } from './SchemaForm'

const LazyExpressionEditor = lazy(() => import('./ExpressionEditor'))

type Content = Record<string, unknown>

const stepTypes = ['decision', 'task', 'operation', 'end']

const parameterTypes = ['text', 'integer', 'decimal', 'boolean', 'date', 'date-time']

/** The keys a step type forbids, from the first four conditionals of the process schema. */
const forbiddenStepKeys: Record<string, string[]> = {
  decision: [
    'operation',
    'set',
    'connector',
    'input',
    'output',
    'retry',
    'event',
    'entity',
    'fields',
    'next',
    'label',
    'assignee',
    'form',
    'dueIn',
    'outcomes',
  ],
  task: [
    'branches',
    'otherwise',
    'operation',
    'set',
    'connector',
    'input',
    'output',
    'retry',
    'event',
    'entity',
    'fields',
    'next',
  ],
  operation: ['branches', 'otherwise', 'label', 'assignee', 'form', 'dueIn', 'outcomes'],
  end: [
    'branches',
    'otherwise',
    'operation',
    'set',
    'connector',
    'input',
    'output',
    'retry',
    'event',
    'entity',
    'fields',
    'next',
    'label',
    'assignee',
    'form',
    'dueIn',
    'outcomes',
  ],
}

/** The keys a step type requires, with the empty value that a new key starts with. */
const requiredStepKeys: Record<string, Content> = {
  decision: { branches: [], otherwise: '' },
  task: { label: { textKey: '' }, assignee: {}, form: '', outcomes: [] },
  operation: { operation: '', next: '' },
  end: {},
}

function isContent(value: unknown): value is Content {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function text(value: unknown): string {
  return typeof value === 'string' ? value : ''
}

/** The step with its type changed: the new type's forbidden keys go and its required keys are added empty. */
function withType(step: Content, type: string): Content {
  const forbidden = new Set(forbiddenStepKeys[type])
  const next: Content = {}
  for (const [key, value] of Object.entries(step)) {
    if (!forbidden.has(key)) {
      next[key] = value
    }
  }
  next.type = type
  for (const [key, initial] of Object.entries(structuredClone(requiredStepKeys[type]))) {
    if (next[key] === undefined) {
      next[key] = initial
    }
  }
  return next
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
 * label, assignee, form, due time and outcomes. An operation shows its operation panel and its `next`. The editor
 * edits the draft through
 * `onChange`, so a key it does not show stays as it is, and removing a step leaves the transitions that name it for
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
              options={stepTypes.map((type) => ({ value: type, label: type }))}
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
  value,
  index,
  onChange,
  onSelect,
  onRemove,
}: {
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
  index,
  step,
  entity,
  names,
  last,
  onMove,
  onRemove,
  onChange,
}: {
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
              options={stepTypes.map((option) => ({ value: option, label: option }))}
              onChange={(next: string) => onChange(pointer, withType(step, next))}
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

/** The editor of an expression: the expression editor when the draft can check expressions, a plain box otherwise. */
function Expression({
  pointer,
  name,
  value,
  onChange,
}: {
  pointer: string
  name: string
  value: string
  onChange: ChangeHandler
}) {
  const check = useContext(ExpressionCheckContext)

  return check ? (
    <Suspense fallback={<Input aria-label={name} value={value} readOnly />}>
      <LazyExpressionEditor
        application={check.application}
        resourceId={check.resourceId}
        path={pointer}
        name={name}
        value={value}
        required
        options={expressionScope(check.kind, check.draft, pointer, check.names)}
        onChange={onChange}
      />
    </Suspense>
  ) : (
    <Input aria-label={name} value={value} onChange={(event) => onChange(pointer, event.target.value)} />
  )
}

/** A picker over the application's names of one kind, or a plain box while the names have not loaded. */
function NamePicker({
  pointer,
  label,
  kind,
  value,
  onPick,
}: {
  pointer: string
  label: string
  kind: string
  value: unknown
  onPick: (next: string) => void
}) {
  const names = useContext(StudioNamesContext)

  return (
    <div data-testid={`studio-field-${pointer}`}>
      <div>{label}</div>
      {names === undefined ? (
        <Input aria-label={label} value={text(value)} onChange={(event) => onPick(event.target.value)} />
      ) : (
        <ReferencePicker
          name={label}
          value={text(value)}
          required
          options={referenceOptions(names, kind, undefined, pointer)}
          onChange={(next) => onPick(next ?? '')}
        />
      )}
    </div>
  )
}

/**
 * The panel of a task: its label, assignee, form, due time and outcomes. Each value is edited through its own pointer,
 * so a key the panel does not show stays as it is. The assignee is a user expression or a role, and a switch replaces
 * the `assignee` object so that it holds only the chosen key.
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
  const assignee = isContent(step.assignee) ? step.assignee : {}
  const mode = assignee.role !== undefined && assignee.user === undefined ? 'role' : 'user'
  const assigneePointer = `${pointer}/assignee`

  function switchTo(next: 'user' | 'role') {
    const { user: _user, role: _role, ...others } = assignee
    onChange(assigneePointer, { ...others, [next]: text(assignee[next]) })
  }

  return (
    <>
      <NamePicker
        pointer={`${pointer}/label/textKey`}
        label={t('shell.studio.process.label')}
        kind="text"
        value={label.textKey}
        onPick={(next) => onChange(`${pointer}/label/textKey`, next)}
      />
      <div data-testid={`studio-field-${assigneePointer}`}>
        <Flex vertical gap="small">
          <Typography.Text strong>{t('shell.studio.process.assignee')}</Typography.Text>
          <Radio.Group
            optionType="button"
            aria-label={t('shell.studio.process.assignee')}
            value={mode}
            options={[
              { value: 'user', label: t('shell.studio.process.assigneeUser') },
              { value: 'role', label: t('shell.studio.process.assigneeRole') },
            ]}
            onChange={(event) => switchTo(event.target.value)}
          />
          {mode === 'user' ? (
            <div data-testid={`studio-field-${assigneePointer}/user`}>
              <Expression
                pointer={`${assigneePointer}/user`}
                name={t('shell.studio.process.assigneeUser')}
                value={text(assignee.user)}
                onChange={onChange}
              />
            </div>
          ) : (
            <NamePicker
              pointer={`${assigneePointer}/role`}
              label={t('shell.studio.process.assigneeRole')}
              kind="role"
              value={assignee.role}
              onPick={(next) => onChange(`${assigneePointer}/role`, next)}
            />
          )}
        </Flex>
      </div>
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
      <Outcomes pointer={pointer} step={step} names={names} onChange={onChange} />
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
