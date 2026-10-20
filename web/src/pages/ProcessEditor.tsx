import { Button, Card, Flex, Input, Select, Typography } from 'antd'
import { lazy, Suspense, useContext } from 'react'
import type { JsonSchema } from '../platform/studioSchemas'
import { useText } from '../platform/texts'
import { ExpressionCheckContext } from './expressionCheckContext'
import { expressionScope } from './expressionScope'
import { ReferencePicker } from './ReferencePicker'
import { SchemaForm, type ChangeHandler } from './SchemaForm'

const LazyExpressionEditor = lazy(() => import('./ExpressionEditor'))

type Content = Record<string, unknown>

const stepTypes = ['decision', 'task', 'operation', 'end']

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

/** The keys of an operation or a task step that the editor does not edit yet. */
const jsonOnlyKeys: Record<string, string[]> = {
  operation: ['operation', 'set', 'connector', 'input', 'output', 'retry', 'event', 'fields'],
  task: ['label', 'assignee', 'form', 'dueIn'],
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
 * The editor of a process. It shows the process's name, entity and start condition with the generic form, the start
 * step, and the steps as cards in file order. A decision shows its branches and its `otherwise`, an end shows only
 * its name and type, and every transition is a picker over the step names of the draft. A task or an operation shows
 * its transitions and points to the JSON view for its other properties. The editor edits the draft through
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
  const names = [
    ...new Set(
      steps.flatMap((step) =>
        isContent(step) && typeof step.name === 'string' && step.name !== '' ? [step.name] : [],
      ),
    ),
  ]

  function move(index: number, offset: number) {
    const next = [...steps]
    ;[next[index], next[index + offset]] = [next[index + offset], next[index]]
    onChange('/steps', next)
  }

  return (
    <Flex vertical gap="middle" data-testid="studio-process-editor">
      <SchemaForm schema={schema} value={value} onChange={onChange} />
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
              names={names}
              last={index === steps.length - 1}
              onMove={(offset) => move(index, offset)}
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

function Step({
  index,
  step,
  names,
  last,
  onMove,
  onRemove,
  onChange,
}: {
  index: number
  step: Content
  names: string[]
  last: boolean
  onMove: (offset: number) => void
  onRemove: () => void
  onChange: ChangeHandler
}) {
  const t = useText()
  const pointer = `/steps/${index}`
  const type = typeof step.type === 'string' ? step.type : undefined
  const present = (keys: string[]) => keys.filter((key) => step[key] !== undefined)

  return (
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
            <Transition
              pointer={`${pointer}/next`}
              label={t('shell.studio.process.next')}
              value={step.next}
              names={names}
              onChange={onChange}
            />
            <JsonOnlyKeys pointer={pointer} keys={present(jsonOnlyKeys.operation)} />
          </>
        )}
        {type === 'task' && (
          <>
            <Outcomes pointer={pointer} step={step} names={names} onChange={onChange} />
            <JsonOnlyKeys pointer={pointer} keys={present(jsonOnlyKeys.task)} />
          </>
        )}
      </Flex>
    </Card>
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
  const check = useContext(ExpressionCheckContext)
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
                  {check ? (
                    <Suspense fallback={<Input aria-label={name} value={when} readOnly />}>
                      <LazyExpressionEditor
                        application={check.application}
                        resourceId={check.resourceId}
                        path={whenPointer}
                        name={name}
                        value={when}
                        required
                        options={expressionScope(check.kind, check.draft, whenPointer, check.names)}
                        onChange={onChange}
                      />
                    </Suspense>
                  ) : (
                    <Input
                      aria-label={name}
                      value={when}
                      onChange={(event) => onChange(whenPointer, event.target.value)}
                    />
                  )}
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

/** The outcomes of a task: only their targets are edited. The rest of an outcome stays as the file has it. */
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
  const outcomes = Array.isArray(step.outcomes) ? step.outcomes : []

  return (
    <div data-testid={`studio-field-${pointer}/outcomes`}>
      <div>
        <Typography.Text strong>{t('shell.studio.process.outcomes')}</Typography.Text>
      </div>
      <Typography.Text type="secondary" data-testid="studio-json-only">
        {t('shell.studio.editInJson')}
      </Typography.Text>
      <Flex vertical gap="small">
        {outcomes.map((outcome, index) => (
          <Transition
            key={index}
            pointer={`${pointer}/outcomes/${index}/next`}
            label={text(isContent(outcome) ? outcome.name : undefined) || t('shell.studio.process.next')}
            value={isContent(outcome) ? outcome.next : undefined}
            names={names}
            onChange={onChange}
          />
        ))}
      </Flex>
    </div>
  )
}
