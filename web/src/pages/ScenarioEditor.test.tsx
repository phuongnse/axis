import { fireEvent, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { type ReactNode, useState } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import platformVietnamese from '../../../src/Axis.Presentation/Texts/vi.json'
import { setAt } from '../platform/jsonPatch'
import type { StudioNames } from '../platform/studio'
import { TextProvider } from '../platform/TextProvider'
import {
  type Step,
  stepPropertyNames,
  stepProperties,
  type StepProperty,
  type StepType,
  stepTypes,
} from '../platform/scenarioSteps'
import { StudioNamesContext } from '../platform/studioNames'
import { ThemeModeProvider } from '../platform/theme'
import { useThemeMode } from '../platform/themeMode'
import { ScenarioEditor } from './ScenarioEditor'

const english: Record<string, string> = platformEnglish
const vietnamese: Record<string, string> = platformVietnamese

/** The English label of a field of the scenario file, by the path of its property. */
const labelOf = (path: string) => english[`shell.studio.field.scenario.${path}`]
const helpOf = (path: string) => english[`shell.studio.field.scenario.${path}.help`]

/** Every path of the scenario file that the editor draws a field or a group for, see docs/reference/studio.md#scenario-editor. */
const drawnPaths = [
  'name',
  'description',
  'now',
  'users.id',
  'users.roles',
  'stubs.source',
  'stubs.connector.name',
  'stubs.connector.operation',
  'stubs.operation',
  'stubs.result',
  'stubs.output',
  'stubs.output.name',
  'steps.type',
  'steps.entity',
  'steps.record',
  'steps.save',
  'steps.as',
  'steps.process',
  'steps.step',
  'steps.outcome',
  'steps.state',
  'steps.assignee',
  'steps.assignee.kind',
  'steps.assignee.user',
  'steps.assignee.role',
  'steps.values',
  'steps.values.name',
  'steps.expect',
  'steps.expect.invalid',
  'steps.by',
  'steps.recipient',
  'steps.recipient.kind',
  'steps.recipient.user',
  'steps.recipient.role',
  'steps.recipient.email',
  'steps.channel',
]

const names: StudioNames = {
  locales: ['en'],
  entities: [
    {
      id: 'e1',
      name: 'Order',
      module: null,
      fields: [
        { name: 'code', type: 'text', target: null, values: null },
        { name: 'size', type: 'integer', target: null, values: null },
        { name: 'cost', type: 'money', target: null, values: null },
        { name: 'state', type: 'enum', target: null, values: ['draft', 'approved'] },
        { name: 'lines', type: 'child-collection', target: 'OrderLine', values: null },
      ],
    },
    { id: 'e2', name: 'Invoice', module: null, fields: [] },
  ],
  dataSources: [],
  processes: [
    {
      id: 'p1',
      name: 'Approval',
      module: null,
      entity: 'Order',
      parameters: [],
      steps: [
        { name: 'review', type: 'task', outcomes: ['approve', 'reject'] },
        { name: 'notify', type: 'service', outcomes: [] },
      ],
    },
    {
      id: 'p2',
      name: 'Shipping',
      module: null,
      entity: 'Order',
      parameters: [],
      steps: [{ name: 'dispatch', type: 'task', outcomes: ['ship'] }],
    },
  ],
  rules: [],
  connectors: [{ id: 'c1', name: 'Payments', operations: [{ name: 'charge' }] }],
  events: [],
  roles: [
    { id: 'r1', name: 'manager' },
    { id: 'r2', name: 'clerk' },
  ],
  sequences: [],
  forms: [],
  pages: [],
  policies: [],
  contracts: [],
  texts: [],
  seeds: [],
  triggers: [],
  scenarios: [],
  textResources: [],
}

const fixtures: Record<StepType, Step> = {
  createRecord: { type: 'createRecord', entity: 'Order', as: 'author', values: { code: 'A1' }, save: 'order' },
  updateRecord: { type: 'updateRecord', record: 'order', as: 'author', values: { size: 2 }, expect: 'denied' },
  deleteRecord: { type: 'deleteRecord', record: 'order', as: 'author', expect: 'denied' },
  startProcess: { type: 'startProcess', process: 'Approval', record: 'order', as: 'author', expect: 'denied' },
  completeTask: {
    type: 'completeTask',
    record: 'order',
    step: 'review',
    outcome: 'approve',
    as: 'author',
    values: { size: 1 },
    expect: 'denied',
  },
  expectRecord: { type: 'expectRecord', record: 'order', values: { code: 'A1' } },
  expectInstance: { type: 'expectInstance', process: 'Approval', record: 'order', state: 'running', step: 'review' },
  expectTask: { type: 'expectTask', record: 'order', step: 'review', assignee: { user: 'author' } },
  expectNotification: {
    type: 'expectNotification',
    recipient: { user: 'author' },
    channel: 'email',
    values: { title: 'Chairs' },
  },
  advanceTime: { type: 'advanceTime', by: 'PT1H' },
}

/** Two earlier creates, so that a later step has the record names `order` and `invoice` to pick from. */
const prefix: Step[] = [
  { type: 'createRecord', entity: 'Order', as: 'author', values: {}, save: 'order' },
  { type: 'createRecord', entity: 'Invoice', as: 'author', values: {}, save: 'invoice' },
]

const propertyCases: [StepType, StepProperty][] = stepTypes.flatMap((type) =>
  stepProperties[type].map(({ name }): [StepType, StepProperty] => [type, name]),
)

function scenario(steps: Step[], users: Record<string, string[]> = { author: ['manager'], employee: ['clerk'] }) {
  return {
    id: '6f0c1c3e-2a4b-4d7e-9c1f-5b8a7d6e4f21',
    kind: 'scenario',
    name: 'Approve',
    formatVersion: 1,
    users,
    steps,
  }
}

/** The fixture of a step that holds the property, shaped so that the step may hold it. */
function fixtureFor(type: StepType, property: StepProperty): Step {
  if (type === 'createRecord' && property === 'expect') {
    return { type, entity: 'Order', as: 'author', values: { code: 'A1' }, expect: 'denied' }
  }
  if (type === 'expectTask' && property === 'state') {
    return { type, record: 'order', step: 'review', state: 'open' }
  }
  return fixtures[type]
}

function ThemeRoot({ children }: { children: ReactNode }) {
  return <div data-theme-mode={useThemeMode().mode}>{children}</div>
}

function providers(child: ReactNode) {
  return (
    <ThemeModeProvider>
      <ThemeRoot>
        <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
          <StudioNamesContext.Provider value={names}>{child}</StudioNamesContext.Provider>
        </TextProvider>
      </ThemeRoot>
    </ThemeModeProvider>
  )
}

function renderEditor(value: Record<string, unknown>) {
  const onChange = vi.fn()
  const { container, rerender } = render(providers(<ScenarioEditor value={value} onChange={onChange} />))
  return {
    onChange,
    container,
    rerender: (next: Record<string, unknown>) =>
      rerender(providers(<ScenarioEditor value={next} onChange={onChange} />)),
  }
}

/** An editor that applies each change to its draft, as the editor page does. */
function Harness({
  value,
  onChange,
}: {
  value: Record<string, unknown>
  onChange: (pointer: string, next: unknown) => void
}) {
  const [draft, setDraft] = useState(value)
  return (
    <ScenarioEditor
      value={draft}
      onChange={(pointer, next) => {
        onChange(pointer, next)
        setDraft((current) => setAt(current, pointer, next) as Record<string, unknown>)
      }}
    />
  )
}

function renderHarness(value: Record<string, unknown>) {
  const onChange = vi.fn()
  render(providers(<Harness value={value} onChange={onChange} />))
  return { onChange }
}

type Panel = ReturnType<typeof within>

async function pick(panel: Panel, name: string, option: string) {
  await userEvent.click(panel.getByRole('combobox', { name }))
  await userEvent.click(screen.getByRole('option', { name: option }))
}

/**
 * Checks the control of a property shows the fixture's value, then edits it. Returns the path under the step and the
 * value the edit writes.
 */
async function showAndEdit(
  type: StepType,
  property: StepProperty,
  fixture: Step,
  field: HTMLElement,
  panel: Panel,
): Promise<{ path: string; next: unknown }> {
  switch (property) {
    case 'entity':
      expect(field).toHaveTextContent('Order')
      await pick(panel, 'Entity', 'Invoice')
      return { path: 'entity', next: 'Invoice' }
    case 'record':
      expect(field).toHaveTextContent('order')
      await pick(panel, 'Record', 'invoice')
      return { path: 'record', next: 'invoice' }
    case 'as':
      expect(field).toHaveTextContent('author')
      await pick(panel, 'Acts as', 'employee')
      return { path: 'as', next: 'employee' }
    case 'process':
      expect(field).toHaveTextContent('Approval')
      await pick(panel, 'Process', 'Shipping')
      return { path: 'process', next: 'Shipping' }
    case 'step': {
      const next = type === 'expectInstance' ? 'notify' : 'dispatch'
      expect(field).toHaveTextContent('review')
      await pick(panel, 'Process step', next)
      return { path: 'step', next }
    }
    case 'outcome':
      expect(field).toHaveTextContent('approve')
      await pick(panel, 'Outcome', 'reject')
      return { path: 'outcome', next: 'reject' }
    case 'state': {
      const next = type === 'expectInstance' ? 'failed' : 'completed'
      expect(field).toHaveTextContent(String(fixture.state))
      await pick(panel, 'State', next)
      return { path: 'state', next }
    }
    case 'expect':
      expect(field).toHaveTextContent('denied')
      await pick(panel, 'Expect', 'notFound')
      return { path: 'expect', next: 'notFound' }
    case 'assignee':
      expect(field).toHaveTextContent('author')
      await pick(panel, 'User', 'employee')
      return { path: 'assignee/user', next: 'employee' }
    case 'recipient':
      expect(field).toHaveTextContent('author')
      await pick(panel, 'User', 'employee')
      return { path: 'recipient/user', next: 'employee' }
    case 'channel':
      expect(field).toHaveTextContent('email')
      await pick(panel, 'Channel', 'inApp')
      return { path: 'channel', next: 'inApp' }
    case 'save':
    case 'by': {
      const box = panel.getByRole('textbox', { name: property === 'save' ? 'Save as' : 'Moves time by' })
      const next = property === 'save' ? 'orders' : 'PT2H'
      expect(box).toHaveValue(String(fixture[property]))
      fireEvent.change(box, { target: { value: next } })
      return { path: property, next }
    }
    case 'values': {
      if (type === 'expectNotification') {
        const box = panel.getByRole('textbox', { name: 'title' })
        expect(box).toHaveValue('Chairs')
        fireEvent.change(box, { target: { value: 'Desks' } })
        return { path: 'values/title', next: 'Desks' }
      }
      const key = type === 'createRecord' || type === 'expectRecord' ? 'code' : 'size'
      const box = panel.getByRole('textbox', { name: key })
      expect(box).toHaveValue(String((fixture.values as Record<string, unknown>)[key]))
      if (key === 'size') {
        fireEvent.change(box, { target: { value: '7' } })
        return { path: 'values/size', next: 7 }
      }
      fireEvent.change(box, { target: { value: 'A2' } })
      return { path: 'values/code', next: 'A2' }
    }
  }
}

afterEach(() => localStorage.clear())

describe.each(['light', 'dark'])('ScenarioEditor in %s mode', (mode) => {
  beforeEach(() => localStorage.setItem('axis.themeMode', mode))

  it('shows the description after the name and before now, and writes its key', async () => {
    const { onChange } = renderEditor({
      ...scenario([fixtures.advanceTime]),
      description: { textKey: 'scenarios.approve' },
    })

    const description = screen.getByTestId('studio-field-/description')
    const box = within(description).getByRole('textbox', { name: 'Text key' })
    expect(box).toHaveValue('scenarios.approve')
    expect(
      screen.getByTestId('studio-field-/name').compareDocumentPosition(description) & Node.DOCUMENT_POSITION_FOLLOWING,
    ).not.toBe(0)
    expect(
      description.compareDocumentPosition(screen.getByTestId('studio-field-/now')) & Node.DOCUMENT_POSITION_FOLLOWING,
    ).not.toBe(0)

    await userEvent.type(box, 'x')
    expect(onChange).toHaveBeenLastCalledWith('/description/textKey', 'scenarios.approvex')
  })

  it('renders inside the theme root with its controls visible', () => {
    renderEditor(scenario([fixtures.advanceTime]))

    const editor = screen.getByTestId('studio-scenario')
    expect(editor.closest(`[data-theme-mode=${mode}]`)).not.toBeNull()
    expect(editor).toBeVisible()
    expect(screen.getByRole('textbox', { name: 'Name' })).toBeVisible()
    expect(screen.getByRole('button', { name: 'Add step' })).toBeVisible()
    expect(screen.getByRole('textbox', { name: 'Moves time by' })).toBeVisible()
  })

  it.each(stepTypes)('shows the panel of a %s step with only its own properties', (type) => {
    const { container } = renderEditor(scenario([fixtures[type]]))
    const field = (name: string) => container.querySelector(`[data-testid="studio-field-/steps/0/${name}"]`)

    for (const property of stepProperties[type]) {
      expect(field(property.name)).not.toBeNull()
    }
    for (const name of stepPropertyNames.filter((name) => !stepProperties[type].some((p) => p.name === name))) {
      expect(field(name)).toBeNull()
    }
  })

  it.each(propertyCases)(
    'a %s step shows its %s property and writes an edit at its pointer',
    async (type, property) => {
      const index = prefix.length
      const fixture = fixtureFor(type, property)
      const { onChange } = renderEditor(scenario([...prefix, fixture]))
      const panel = within(screen.getByTestId(`studio-field-/steps/${index}`))
      const field = screen.getByTestId(`studio-field-/steps/${index}/${property}`)

      const { path, next } = await showAndEdit(type, property, fixture, field, panel)

      expect(onChange).toHaveBeenLastCalledWith(`/steps/${index}/${path}`, next)
    },
  )

  it('shows a value row for each entity field the step sets, and keeps a key the entity lacks as text', () => {
    renderEditor(scenario([{ ...fixtures.createRecord, values: { code: 'A1', extra: 'x' } }]))

    expect(screen.getByRole('textbox', { name: 'code' })).toHaveValue('A1')
    expect(screen.getByRole('textbox', { name: 'extra' })).toHaveValue('x')
  })

  it.each([
    ['1500', 1500],
    ['-1', -1],
    ['12.5', 12.5],
  ])('writes the number of a money value typed as %s', (text, next) => {
    const { onChange } = renderEditor(
      scenario([{ type: 'createRecord', entity: 'Order', as: 'author', values: { cost: 0 } }]),
    )

    fireEvent.change(screen.getByRole('textbox', { name: 'cost' }), { target: { value: text } })

    expect(onChange).toHaveBeenLastCalledWith('/steps/0/values/cost', next)
  })

  it('adds a money value with the starting number 0', async () => {
    const { onChange } = renderEditor(scenario([{ type: 'createRecord', entity: 'Order', as: 'author', values: {} }]))

    await userEvent.click(screen.getByRole('combobox', { name: 'Add value' }))
    await userEvent.click(screen.getByRole('option', { name: 'cost' }))

    expect(onChange).toHaveBeenCalledWith('/steps/0/values/cost', 0)
  })

  it('adds a value of an entity field with a starting value of its type', async () => {
    const { onChange } = renderEditor(scenario([{ type: 'createRecord', entity: 'Order', as: 'author', values: {} }]))

    await userEvent.click(screen.getByRole('combobox', { name: 'Add value' }))
    await userEvent.click(screen.getByRole('option', { name: 'size' }))

    expect(onChange).toHaveBeenCalledWith('/steps/0/values/size', 0)
  })

  it('changes the type, dropping the properties the new type does not take and keeping unknown keys', async () => {
    const { onChange } = renderEditor(
      scenario([
        { type: 'createRecord', entity: 'Order', as: 'author', values: { code: 'A1' }, save: 'order', note: 'keep me' },
      ]),
    )

    await userEvent.click(screen.getByRole('combobox', { name: 'Step type' }))
    await userEvent.click(screen.getByRole('option', { name: 'advanceTime' }))

    expect(onChange).toHaveBeenCalledWith('/steps/0', { type: 'advanceTime', note: 'keep me' })
  })

  it('switching a step to expectNotification drops as, record and expect', async () => {
    const { onChange } = renderEditor(scenario([fixtures.completeTask]))

    await userEvent.click(screen.getByRole('combobox', { name: 'Step type' }))
    await userEvent.click(screen.getByRole('option', { name: 'expectNotification' }))

    expect(onChange).toHaveBeenCalledWith('/steps/0', { type: 'expectNotification', values: { size: 1 } })
  })

  it('writes an expectNotification step with a recipient, a channel and a value', async () => {
    const initial = scenario([])
    const { onChange } = renderHarness(initial)

    await userEvent.click(screen.getByRole('button', { name: 'Add step' }))
    const panel = within(screen.getByTestId('studio-field-/steps/0'))
    await pick(panel, 'Step type', 'expectNotification')
    await pick(panel, 'Recipient type', 'User')
    await pick(panel, 'User', 'employee')
    await pick(panel, 'Channel', 'email')
    await userEvent.click(panel.getByRole('button', { name: 'Add value' }))
    const name = panel.getByRole('textbox', { name: 'Value name' })
    await userEvent.clear(name)
    await userEvent.type(name, 'title')
    await userEvent.tab()
    await userEvent.type(panel.getByRole('textbox', { name: 'title' }), 'Chairs')

    const saved = onChange.mock.calls.reduce(
      (draft, [pointer, next]) => setAt(draft, pointer, next),
      initial as unknown,
    ) as Record<string, unknown>
    expect(saved.steps).toEqual([
      { type: 'expectNotification', recipient: { user: 'employee' }, channel: 'email', values: { title: 'Chairs' } },
    ])
  })

  it('keeps the unknown keys of the scenario, a stub and a step after edits to other properties', () => {
    const initial = {
      ...scenario([{ ...fixtures.advanceTime, note: 'kept' }]),
      owner: 'author',
      stubs: [{ operation: 'billing/Charge', output: {}, comment: 'kept' }],
    }
    const { onChange } = renderEditor(initial)

    fireEvent.change(screen.getByRole('textbox', { name: 'Name' }), { target: { value: 'Renamed' } })
    fireEvent.change(screen.getByRole('textbox', { name: 'Package operation' }), {
      target: { value: 'billing/Refund' },
    })
    fireEvent.change(screen.getByRole('textbox', { name: 'Moves time by' }), { target: { value: 'PT2H' } })

    const saved = onChange.mock.calls.reduce(
      (draft, [pointer, next]) => setAt(draft, pointer, next),
      initial as unknown,
    ) as Record<string, unknown>
    expect(saved).toMatchObject({
      name: 'Renamed',
      owner: 'author',
      stubs: [{ operation: 'billing/Refund', comment: 'kept' }],
      steps: [{ type: 'advanceTime', by: 'PT2H', note: 'kept' }],
    })
  })

  it('edits the time an advanceTime step moves by', async () => {
    const { onChange } = renderEditor(scenario([fixtures.advanceTime]))

    await userEvent.type(screen.getByRole('textbox', { name: 'Moves time by' }), '0')

    expect(onChange).toHaveBeenLastCalledWith('/steps/0/by', 'PT1H0')
  })

  it('adds an advanceTime step at the end', async () => {
    const { onChange } = renderEditor(scenario([fixtures.advanceTime]))

    await userEvent.click(screen.getByRole('button', { name: 'Add step' }))

    expect(onChange).toHaveBeenCalledWith('/steps', [fixtures.advanceTime, { type: 'advanceTime', by: 'PT1H' }])
  })

  it('renames a user when the id loses focus', async () => {
    const { onChange } = renderEditor(scenario([], { author: ['manager'] }))

    const input = screen.getByRole('textbox', { name: 'User id' })
    await userEvent.clear(input)
    await userEvent.type(input, 'employee')
    await userEvent.tab()

    expect(onChange).toHaveBeenCalledWith('/users', { employee: ['manager'] })
  })

  it('refuses a user id that another user has, and shows the id again', async () => {
    const { onChange } = renderEditor(scenario([], { author: [], employee: [] }))

    const input = screen.getAllByRole('textbox', { name: 'User id' })[0]
    await userEvent.clear(input)
    await userEvent.type(input, 'employee')
    await userEvent.tab()

    expect(onChange).not.toHaveBeenCalledWith('/users', expect.anything())
    expect(input).toHaveValue('author')
  })

  it('picks the roles of a user from the application roles', async () => {
    const { onChange } = renderEditor(scenario([], { author: [] }))

    await userEvent.click(screen.getByRole('combobox', { name: 'Roles' }))
    await userEvent.click(screen.getByRole('option', { name: 'clerk' }))

    expect(onChange).toHaveBeenCalledWith('/users/author', ['clerk'])
  })

  it('switches a stub from a connector operation to a package operation', async () => {
    const { onChange } = renderEditor({
      ...scenario([fixtures.advanceTime]),
      stubs: [{ connector: { name: 'Payments', operation: 'charge' }, output: {} }],
    })

    await userEvent.click(screen.getByRole('combobox', { name: 'Source' }))
    await userEvent.click(screen.getByRole('option', { name: 'Package operation' }))

    expect(onChange).toHaveBeenCalledWith('/stubs/0/connector', undefined)
    expect(onChange).toHaveBeenCalledWith('/stubs/0/operation', '')
  })

  it('adds an output to a stub that returns one', async () => {
    const { onChange } = renderEditor({
      ...scenario([fixtures.advanceTime]),
      stubs: [{ operation: 'billing/Charge', output: {} }],
    })

    await userEvent.click(screen.getByRole('button', { name: 'Add output' }))

    expect(onChange).toHaveBeenCalledWith('/stubs/0/output/output1', '')
  })

  it('does not allow a create step to save and expect at once', () => {
    renderEditor(scenario([{ ...fixtures.createRecord, expect: 'denied' }]))

    expect(screen.getByRole('textbox', { name: 'Save as' })).toBeDisabled()
  })

  it('disables the expect choice of a create step that saves', () => {
    renderEditor(scenario([fixtures.createRecord]))

    expect(screen.getByRole('combobox', { name: 'Expect' })).toBeDisabled()
  })

  it('shows an invalid expectation with its path and writes an edit to that path', () => {
    const { onChange } = renderEditor(scenario([{ ...fixtures.deleteRecord, expect: { invalid: '/steps/0/as' } }]))
    const field = screen.getByTestId('studio-field-/steps/0/expect')
    const path = within(field).getByRole('textbox', { name: 'Path' })

    expect(field).toHaveTextContent('invalid')
    expect(path).toHaveValue('/steps/0/as')
    fireEvent.change(path, { target: { value: '/steps/0/record' } })

    expect(onChange).toHaveBeenLastCalledWith('/steps/0/expect/invalid', '/steps/0/record')
  })

  it('writes an empty path when an expectation becomes invalid', async () => {
    const { onChange } = renderEditor(scenario([fixtures.deleteRecord]))

    await userEvent.click(screen.getByRole('combobox', { name: 'Expect' }))
    await userEvent.click(screen.getByRole('option', { name: 'invalid' }))

    expect(onChange).toHaveBeenCalledWith('/steps/0/expect', { invalid: '' })
  })

  it('takes an assignee or a state on an expectTask step, not both', () => {
    renderEditor(scenario([{ ...fixtures.expectTask, state: 'open' }]))

    expect(screen.getByRole('combobox', { name: 'Assignee type' })).toBeDisabled()
  })

  it('saves the values of a step whose entity is not in the folder only when they are a JSON object', () => {
    const { onChange } = renderEditor(scenario([{ type: 'expectRecord', record: 'order', values: {} }]))
    const box = screen.getByRole('textbox', { name: 'Values' })

    fireEvent.change(box, { target: { value: '{"size": 3}' } })
    expect(onChange).toHaveBeenLastCalledWith('/steps/0/values', { size: 3 })

    onChange.mockClear()
    fireEvent.change(box, { target: { value: '[1]' } })
    expect(onChange).not.toHaveBeenCalled()
    expect(screen.getByText(/not JSON/)).toBeInTheDocument()
  })

  it('keeps the JSON values text as typed after it saves a valid object', () => {
    renderHarness(scenario([{ type: 'expectRecord', record: 'order', values: {} }]))
    const box = screen.getByRole('textbox', { name: 'Values' })

    fireEvent.change(box, { target: { value: '{"size":   3}' } })

    expect(box).toHaveValue('{"size":   3}')
  })

  it('shows the JSON values again when they change from outside the editor', () => {
    const { rerender } = renderEditor(scenario([{ type: 'expectRecord', record: 'order', values: { size: 3 } }]))

    rerender(scenario([{ type: 'expectRecord', record: 'order', values: { size: 5 } }]))

    expect(screen.getByRole('textbox', { name: 'Values' })).toHaveValue(JSON.stringify({ size: 5 }, null, 2))
  })
})

function namedControl(root: HTMLElement, label: string): HTMLElement | null {
  const query = within(root)
  return (
    query.queryByRole('combobox', { name: label }) ??
    query.queryByRole('textbox', { name: label }) ??
    query.queryByRole('group', { name: label })
  )
}

const stubConnector = { connector: { name: 'Payments', operation: 'charge' }, output: { total: 1 } }
const stubOperation = { operation: 'billing/Refund', outcome: 'failed' }
const withStub = (stub: Record<string, unknown>) => ({ ...scenario([fixtures.advanceTime]), stubs: [stub] })
const onlyStep = (step: Step) => scenario([step])

/** The raw names of the step properties and of their parts, which no control of a step card may be named by. */
const rawPropertyNames = [
  ...stepPropertyNames,
  'assignee kind',
  'recipient kind',
  'user',
  'role',
  'email',
  'invalid',
  'operation',
]

/** Each drawn path, with the role of its control, the pointer of the wrapper that holds it and an editor value that draws it. */
const drawnCases: [
  path: string,
  role: 'combobox' | 'textbox' | 'group',
  pointer: string,
  value: Record<string, unknown>,
][] = [
  ['name', 'textbox', '/name', scenario([fixtures.advanceTime])],
  ['description', 'group', '/description', scenario([fixtures.advanceTime])],
  ['now', 'textbox', '/now', scenario([fixtures.advanceTime])],
  ['users.id', 'textbox', '/users/author', scenario([fixtures.advanceTime])],
  ['users.roles', 'combobox', '/users/author', scenario([fixtures.advanceTime])],
  ['stubs.source', 'combobox', '/stubs/0/source', withStub(stubConnector)],
  ['stubs.connector.name', 'combobox', '/stubs/0/connector/name', withStub(stubConnector)],
  ['stubs.connector.operation', 'combobox', '/stubs/0/connector/operation', withStub(stubConnector)],
  ['stubs.operation', 'textbox', '/stubs/0/operation', withStub(stubOperation)],
  ['stubs.result', 'combobox', '/stubs/0/result', withStub(stubConnector)],
  ['stubs.output', 'group', '/stubs/0', withStub(stubConnector)],
  ['stubs.output.name', 'textbox', '/stubs/0', withStub(stubConnector)],
  ['steps.type', 'combobox', '/steps/0/type', onlyStep(fixtures.advanceTime)],
  ['steps.entity', 'combobox', '/steps/0/entity', onlyStep(fixtures.createRecord)],
  ['steps.record', 'combobox', '/steps/0/record', onlyStep(fixtures.updateRecord)],
  ['steps.save', 'textbox', '/steps/0/save', onlyStep(fixtures.createRecord)],
  ['steps.as', 'combobox', '/steps/0/as', onlyStep(fixtures.createRecord)],
  ['steps.process', 'combobox', '/steps/0/process', onlyStep(fixtures.startProcess)],
  ['steps.step', 'combobox', '/steps/0/step', onlyStep(fixtures.completeTask)],
  ['steps.outcome', 'combobox', '/steps/0/outcome', onlyStep(fixtures.completeTask)],
  ['steps.state', 'combobox', '/steps/0/state', onlyStep(fixtures.expectInstance)],
  ['steps.assignee', 'group', '/steps/0/assignee', onlyStep(fixtures.expectTask)],
  ['steps.assignee.kind', 'combobox', '/steps/0/assignee', onlyStep(fixtures.expectTask)],
  ['steps.assignee.user', 'combobox', '/steps/0/assignee', onlyStep(fixtures.expectTask)],
  [
    'steps.assignee.role',
    'combobox',
    '/steps/0/assignee',
    onlyStep({ type: 'expectTask', record: 'order', step: 'review', assignee: { role: 'manager' } }),
  ],
  ['steps.values', 'group', '/steps/0/values', onlyStep(fixtures.createRecord)],
  ['steps.values', 'textbox', '/steps/0/values', onlyStep({ type: 'expectRecord', record: 'order', values: {} })],
  ['steps.values.name', 'textbox', '/steps/0/values', onlyStep(fixtures.expectNotification)],
  ['steps.expect', 'combobox', '/steps/0/expect', onlyStep(fixtures.deleteRecord)],
  [
    'steps.expect.invalid',
    'textbox',
    '/steps/0/expect',
    onlyStep({ ...fixtures.deleteRecord, expect: { invalid: '/steps/0/as' } }),
  ],
  ['steps.by', 'textbox', '/steps/0/by', onlyStep(fixtures.advanceTime)],
  ['steps.recipient', 'group', '/steps/0/recipient', onlyStep(fixtures.expectNotification)],
  ['steps.recipient.kind', 'combobox', '/steps/0/recipient', onlyStep(fixtures.expectNotification)],
  ['steps.recipient.user', 'combobox', '/steps/0/recipient', onlyStep(fixtures.expectNotification)],
  [
    'steps.recipient.role',
    'combobox',
    '/steps/0/recipient',
    onlyStep({ ...fixtures.expectNotification, recipient: { role: 'manager' } }),
  ],
  [
    'steps.recipient.email',
    'textbox',
    '/steps/0/recipient',
    onlyStep({ ...fixtures.expectNotification, recipient: { email: 'clerk@example.com' } }),
  ],
  ['steps.channel', 'combobox', '/steps/0/channel', onlyStep(fixtures.expectNotification)],
]

describe('ScenarioEditor field help and names', () => {
  it('has a case for every drawn path', () => {
    expect([...new Set(drawnCases.map(([path]) => path))].sort()).toEqual([...drawnPaths].sort())
  })

  it.each(drawnCases)('describes the %s field as a %s by its label and help', (path, role, pointer, value) => {
    renderEditor(value)

    const wrapper = screen.getByTestId(`studio-field-${pointer}`)
    expect(within(wrapper).getByRole(role, { name: labelOf(path) })).toHaveAccessibleDescription(helpOf(path))
  })

  it.each(stepTypes)('names no control of a %s step by a raw property name, and shows no text key', (type) => {
    renderEditor(scenario([fixtures[type]]))
    const card = screen.getByTestId('studio-field-/steps/0')

    for (const name of rawPropertyNames) {
      for (const role of ['combobox', 'textbox'] as const) {
        const named = within(card)
          .queryAllByRole(role, { name })
          .filter((control) => control.closest('[data-testid*="/values/"]') === null)
        expect(named, `${role} named ${name}`).toEqual([])
      }
    }
    expect(card.textContent).not.toContain('shell.studio.')
  })
})

describe('ScenarioEditor texts', () => {
  it.each(drawnPaths)('has a label and a help for %s in English and Vietnamese', (path) => {
    for (const catalogue of [english, vietnamese]) {
      expect(catalogue[`shell.studio.field.scenario.${path}`]).toBeTruthy()
      expect(catalogue[`shell.studio.field.scenario.${path}.help`]).toBeTruthy()
    }
  })

  it('has a label for exactly the paths the editor draws', () => {
    const catalogued = Object.keys(english)
      .filter((key) => key.startsWith('shell.studio.field.scenario.') && !key.endsWith('.help'))
      .map((key) => key.slice('shell.studio.field.scenario.'.length))
    expect([...catalogued].sort()).toEqual([...drawnPaths].sort())
  })

  it('shows no catalogue key on the page', () => {
    renderEditor({
      ...scenario(stepTypes.map((type) => fixtures[type])),
      stubs: [
        { connector: { name: 'Payments', operation: 'charge' }, output: { total: 1 } },
        { operation: 'billing/Refund', outcome: 'failed' },
      ],
    })

    expect(document.body.textContent).not.toContain('shell.studio.')
  })

  it.each(stepTypes)('names each property of a %s step by its English label', (type) => {
    renderEditor(scenario([fixtures[type]]))
    const card = screen.getByTestId('studio-field-/steps/0')

    for (const { name } of stepProperties[type]) {
      expect(namedControl(card, labelOf(`steps.${name}`)), name).not.toBeNull()
    }
  })
})

describe('ScenarioEditor sections', () => {
  it('puts the Form tab in General, Users, Stubs and Steps sections, each with its heading and line', () => {
    renderEditor({ ...scenario([fixtures.advanceTime]), stubs: [{ operation: 'billing/Refund', output: {} }] })

    const sections = screen.getAllByTestId(/^studio-section-/)
    expect(sections.map((section) => section.dataset.testid)).toEqual([
      'studio-section-general',
      'studio-section-users',
      'studio-section-stubs',
      'studio-section-steps',
    ])
    const keys = ['general', 'users', 'stubs', 'steps'].map((name) => `shell.studio.section.scenario.${name}`)
    sections.forEach((section, index) => {
      expect(within(section).getByRole('heading', { name: english[keys[index]] })).toBeVisible()
      expect(within(section).getByText(english[`${keys[index]}.line`])).toBeVisible()
    })
  })

  it('puts each Add button on the heading of its section', () => {
    renderEditor(scenario([fixtures.advanceTime]))

    const sections = screen.getAllByTestId(/^studio-section-/)
    expect(within(sections[1]).getByRole('button', { name: 'Add user' })).toBeVisible()
    expect(within(sections[2]).getByRole('button', { name: 'Add stub' })).toBeVisible()
    expect(within(sections[3]).getByRole('button', { name: 'Add step' })).toBeVisible()
  })
})
