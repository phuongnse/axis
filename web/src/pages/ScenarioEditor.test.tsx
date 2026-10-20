import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import type { StudioNames } from '../platform/studio'
import { TextProvider } from '../platform/TextProvider'
import { type Step, stepPropertyNames, stepProperties, type StepType, stepTypes } from '../platform/scenarioSteps'
import { StudioNamesContext } from '../platform/studioNames'
import { ThemeModeProvider } from '../platform/theme'
import { useThemeMode } from '../platform/themeMode'
import { ScenarioEditor } from './ScenarioEditor'

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
        { name: 'state', type: 'enum', target: null, values: ['draft', 'approved'] },
        { name: 'lines', type: 'child-collection', target: 'OrderLine', values: null },
      ],
    },
  ],
  dataSources: [],
  processes: [
    {
      id: 'p1',
      name: 'Approval',
      steps: [
        { name: 'review', type: 'task', outcomes: ['approve', 'reject'] },
        { name: 'notify', type: 'service', outcomes: [] },
      ],
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
  texts: [],
}

const fixtures: Record<StepType, Step> = {
  createRecord: { type: 'createRecord', entity: 'Order', as: 'author', values: { code: 'A1' }, save: 'order' },
  updateRecord: { type: 'updateRecord', record: 'order', as: 'author', values: { size: 2 } },
  deleteRecord: { type: 'deleteRecord', record: 'order', as: 'author' },
  startProcess: { type: 'startProcess', process: 'Approval', record: 'order', as: 'author' },
  completeTask: { type: 'completeTask', record: 'order', step: 'review', outcome: 'approve', as: 'manager' },
  expectRecord: { type: 'expectRecord', record: 'order', values: { code: 'A1' } },
  expectInstance: { type: 'expectInstance', process: 'Approval', record: 'order', state: 'running' },
  expectTask: { type: 'expectTask', record: 'order', step: 'review', state: 'open' },
  advanceTime: { type: 'advanceTime', by: 'PT1H' },
}

function scenario(steps: Step[], users: Record<string, string[]> = { author: ['manager'] }) {
  return {
    id: '6f0c1c3e-2a4b-4d7e-9c1f-5b8a7d6e4f21',
    kind: 'scenario',
    name: 'Approve',
    formatVersion: 1,
    users,
    steps,
  }
}

function ThemeRoot({ children }: { children: ReactNode }) {
  return <div data-theme-mode={useThemeMode().mode}>{children}</div>
}

function renderEditor(value: Record<string, unknown>) {
  const onChange = vi.fn()
  const { container } = render(
    <ThemeModeProvider>
      <ThemeRoot>
        <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
          <StudioNamesContext.Provider value={names}>
            <ScenarioEditor value={value} onChange={onChange} />
          </StudioNamesContext.Provider>
        </TextProvider>
      </ThemeRoot>
    </ThemeModeProvider>,
  )
  return { onChange, container }
}

afterEach(() => localStorage.clear())

describe.each(['light', 'dark'])('ScenarioEditor in %s mode', (mode) => {
  beforeEach(() => localStorage.setItem('axis.themeMode', mode))

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

  it('shows a value row for each entity field the step sets, and keeps a key the entity lacks as text', () => {
    renderEditor(scenario([{ ...fixtures.createRecord, values: { code: 'A1', extra: 'x' } }]))

    expect(screen.getByRole('textbox', { name: 'code' })).toHaveValue('A1')
    expect(screen.getByRole('textbox', { name: 'extra' })).toHaveValue('x')
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

  it('writes only the edited pointers, so unknown keys of the scenario, a stub and a step stay', () => {
    const { onChange } = renderEditor({
      ...scenario([{ ...fixtures.advanceTime, note: 'kept' }]),
      owner: 'hoa',
      stubs: [{ operation: 'billing/Charge', output: {}, comment: 'kept' }],
    })

    fireEvent.change(screen.getByRole('textbox', { name: 'Name' }), { target: { value: 'Renamed' } })
    fireEvent.change(screen.getByRole('textbox', { name: 'operation' }), { target: { value: 'billing/Refund' } })

    expect(onChange.mock.calls).toEqual([
      ['/name', 'Renamed'],
      ['/stubs/0/operation', 'billing/Refund'],
    ])
  })

  it('edits the time an advanceTime step moves by', async () => {
    const { onChange } = renderEditor(scenario([fixtures.advanceTime]))

    await userEvent.type(screen.getByRole('textbox', { name: 'by' }), '0')

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
    await userEvent.type(input, 'lan')
    await userEvent.tab()

    expect(onChange).toHaveBeenCalledWith('/users', { lan: ['manager'] })
  })

  it('refuses a user id that another user has, and shows the id again', async () => {
    const { onChange } = renderEditor(scenario([], { author: [], lan: [] }))

    const input = screen.getAllByRole('textbox', { name: 'User id' })[0]
    await userEvent.clear(input)
    await userEvent.type(input, 'lan')
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

    expect(screen.getByRole('textbox', { name: 'save' })).toBeDisabled()
  })

  it('disables the expect choice of a create step that saves', () => {
    renderEditor(scenario([fixtures.createRecord]))

    expect(screen.getByRole('combobox', { name: 'expect' })).toBeDisabled()
  })

  it('takes an assignee or a state on an expectTask step, not both', () => {
    renderEditor(scenario([fixtures.expectTask]))

    expect(screen.getByRole('combobox', { name: 'assignee kind' })).toBeDisabled()
  })

  it('saves the values of a step whose entity is not in the folder only when they are a JSON object', () => {
    const { onChange } = renderEditor(scenario([{ type: 'expectRecord', record: 'order', values: {} }]))
    const box = screen.getByRole('textbox', { name: 'values' })

    fireEvent.change(box, { target: { value: '{"size": 3}' } })
    expect(onChange).toHaveBeenLastCalledWith('/steps/0/values', { size: 3 })

    onChange.mockClear()
    fireEvent.change(box, { target: { value: '[1]' } })
    expect(onChange).not.toHaveBeenCalled()
    expect(screen.getByText(/not JSON/)).toBeInTheDocument()
  })
})
