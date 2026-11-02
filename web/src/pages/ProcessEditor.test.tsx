import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useEffect, useState, type ReactNode } from 'react'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { TextProvider } from '../platform/TextProvider'
import { setAt } from '../platform/jsonPatch'
import type { StudioNames } from '../platform/studio'
import { StudioNamesContext } from '../platform/studioNames'
import { loadKindSchema, type JsonSchema } from '../platform/studioSchemas'
import { ThemeModeProvider } from '../platform/theme'
import { useThemeMode } from '../platform/themeMode'
import { chooseOption, openDropdown, optionTexts, selectRoot } from '../test/antd'
import { ExpressionCheckContext } from './expressionCheckContext'
import { ProcessEditor } from './ProcessEditor'

// The expression editor is CodeMirror. The mock shows the names it is offered, which is what these tests check.
vi.mock('./ExpressionEditor', () => ({
  default: ({ name, options }: { name: string; options: { label: string }[] }) => (
    <div role="group" aria-label={name} data-options={options.map((option) => option.label).join(',')} />
  ),
}))

const process = {
  id: '4b60c03e-02d0-4329-80c6-75f972190399',
  kind: 'process',
  name: 'PurchaseRequestApproval',
  formatVersion: 1,
  entity: 'PurchaseRequest',
  start: 'submit',
  steps: [
    {
      name: 'submit',
      type: 'operation',
      operation: 'updateRecord',
      set: { status: "'submitted'" },
      next: 'managerApproval',
    },
    {
      name: 'managerApproval',
      type: 'task',
      label: { textKey: 'purchaseRequest.managerApproval' },
      assignee: { user: 'department.manager' },
      form: 'PurchaseRequestReview',
      dueIn: 'P3D',
      hint: 1,
      outcomes: [
        { name: 'approve', label: { textKey: 'task.approve' }, next: 'financeCheck', note: 'x' },
        { name: 'reject', label: { textKey: 'task.reject' }, next: 'gone' },
      ],
    },
    {
      name: 'financeCheck',
      type: 'decision',
      branches: [{ name: 'branch1', when: 'totalAmount >= FinanceThreshold()', next: 'done' }],
      otherwise: 'missing',
    },
    { name: 'done', type: 'end' },
    { name: 'pause', type: 'timer', wait: 'PT1H', next: 'done' },
  ],
}

const names = {
  locales: ['en'],
  entities: [
    {
      id: 'e1',
      name: 'PurchaseRequest',
      module: null,
      fields: [{ name: 'totalAmount', type: 'decimal', target: null, values: null }],
    },
  ],
  dataSources: [],
  processes: [
    { id: 'p1', name: 'OrderFulfilment', entity: 'PurchaseRequest', parameters: [], steps: [] },
    {
      id: 'p2',
      name: 'DailyReminder',
      entity: null,
      parameters: [
        { name: 'daysOverdue', type: 'integer' },
        { name: 'region', type: 'text' },
      ],
      steps: [],
    },
  ],
  rules: [],
  connectors: [],
  events: [
    { id: 'ev1', name: 'PaymentReceived', entity: 'PurchaseRequest', fields: [{ name: 'amount', type: 'decimal' }] },
    { id: 'ev2', name: 'LeaveApproved', entity: 'LeaveRequest', fields: [] },
  ],
  roles: [{ id: 'r1', name: 'finance-reviewer' }],
  sequences: [],
  forms: [
    { id: 'f1', name: 'PurchaseRequestReview' },
    { id: 'f2', name: 'OtherForm' },
  ],
  pages: [],
  texts: ['purchaseRequest.managerApproval', 'task.approve', 'task.reject', 'task.return'].map((key) => ({
    key,
    texts: { en: key },
    usedBy: 1,
  })),
} as unknown as StudioNames

/** The theme root of the shell: an element that carries the mode the provider chose. */
function ThemeRoot({ children }: { children: ReactNode }) {
  return <div data-theme-mode={useThemeMode().mode}>{children}</div>
}

let schema: JsonSchema

/** Applies each change to its own copy of the value, as the Studio editor does, and exposes the latest value. */
function renderEditor(value: Record<string, unknown> = process, checked = false) {
  const latest = { value }
  function Harness() {
    const [draft, setDraft] = useState(value)
    useEffect(() => {
      latest.value = draft
    })
    const editor = (
      <ProcessEditor
        schema={schema}
        value={draft}
        onChange={(pointer, next) => setDraft((current) => setAt(current, pointer, next) as Record<string, unknown>)}
      />
    )
    return checked ? (
      <ExpressionCheckContext.Provider
        value={{ application: 'Shop', resourceId: '4b60c03e', kind: 'process', draft, names }}
      >
        {editor}
      </ExpressionCheckContext.Provider>
    ) : (
      editor
    )
  }
  render(
    <ThemeModeProvider>
      <ThemeRoot>
        <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
          <StudioNamesContext.Provider value={names}>
            <Harness />
          </StudioNamesContext.Provider>
        </TextProvider>
      </ThemeRoot>
    </ThemeModeProvider>,
  )
  return latest
}

function stepOf(latest: { value: Record<string, unknown> }, index: number) {
  return (latest.value.steps as Record<string, unknown>[])[index]
}

beforeAll(async () => {
  const full = (await loadKindSchema('process'))!
  const { start: _start, steps: _steps, ...properties } = full.properties ?? {}
  schema = { ...full, properties, required: full.required?.filter((name) => name !== 'start' && name !== 'steps') }
})

beforeEach(() =>
  vi.stubGlobal(
    'fetch',
    vi.fn(async () => new Response(JSON.stringify({ type: 'boolean', diagnostics: [] }), { status: 200 })),
  ),
)

afterEach(() => {
  localStorage.clear()
  vi.unstubAllGlobals()
})

describe.each(['light', 'dark'])('ProcessEditor in %s mode', (mode) => {
  beforeEach(() => localStorage.setItem('axis.themeMode', mode))

  it('renders inside the theme root with a card per step and the Add step choice', () => {
    renderEditor()

    const editor = screen.getByTestId('studio-process-editor')
    expect(editor.closest(`[data-theme-mode=${mode}]`)).not.toBeNull()
    expect(editor).toBeVisible()
    expect(screen.getByRole('textbox', { name: 'name' })).toHaveValue('PurchaseRequestApproval')
    expect(selectRoot(screen.getByRole('combobox', { name: 'Start step' }))).toBeVisible()
    for (let index = 0; index < process.steps.length; index++) {
      expect(screen.getByTestId(`studio-step-${index}`)).toBeVisible()
    }
    expect(screen.getByTestId('studio-step-2')).toHaveTextContent('financeCheck · decision')
    expect(selectRoot(screen.getByRole('combobox', { name: 'Add step' }))).toBeVisible()
  })

  it('tags a transition that names no step as unknown', () => {
    renderEditor()

    const otherwise = screen.getByTestId('studio-field-/steps/2/otherwise')
    expect(within(otherwise).getByTestId('studio-reference-unknown')).toHaveTextContent('missing')
    expect(
      within(screen.getByTestId('studio-field-/steps/2/branches/0/next')).queryByTestId('studio-reference-unknown'),
    ).toBeNull()
    const outcome = screen.getByTestId('studio-field-/steps/1/outcomes/1/next')
    expect(within(outcome).getByTestId('studio-reference-unknown')).toHaveTextContent('gone')
  })

  it('shows the operation panel and points to the JSON view for no property of an operation or a task', () => {
    renderEditor()

    const set = screen.getByTestId('studio-field-/steps/0/set')
    expect(within(set).queryByTestId('studio-json-only')).toBeNull()
    expect(
      within(screen.getByTestId('studio-field-/steps/0/set/status')).getByRole('textbox', { name: 'Expression' }),
    ).toHaveValue("'submitted'")
    expect(screen.getByTestId('studio-field-/steps/0/next')).toBeVisible()
    expect(within(screen.getByTestId('studio-step-1')).queryByTestId('studio-json-only')).toBeNull()
  })

  it('renders the task panel inside the theme root with its fields visible', () => {
    renderEditor()

    const card = screen.getByTestId('studio-step-1')
    expect(card.closest(`[data-theme-mode=${mode}]`)).not.toBeNull()
    for (const field of [
      '/steps/1/label/textKey',
      '/steps/1/assignee',
      '/steps/1/assignee/user',
      '/steps/1/form',
      '/steps/1/dueIn',
      '/steps/1/outcomes/0/name',
      '/steps/1/outcomes/0/label/textKey',
      '/steps/1/outcomes/0/next',
    ]) {
      expect(within(card).getByTestId(`studio-field-${field}`)).toBeVisible()
    }
    expect(within(card).getByRole('textbox', { name: 'Due in' })).toHaveValue('P3D')
    expect(within(card).getByRole('radio', { name: 'User' })).toBeChecked()
    expect(within(card).getByRole('button', { name: 'Add outcome' })).toBeVisible()
  })

  it('edits a task through its own fields and keeps the keys the panel does not show', async () => {
    const latest = renderEditor()
    const field = (pointer: string) => screen.getByTestId(`studio-field-${pointer}`)

    await chooseOption('task.return', field('/steps/1/label/textKey'))
    await chooseOption('OtherForm', field('/steps/1/form'))
    await userEvent.clear(within(field('/steps/1/outcomes/0/name')).getByRole('textbox'))
    await userEvent.type(within(field('/steps/1/outcomes/0/name')).getByRole('textbox'), 'accept')
    await chooseOption('task.return', field('/steps/1/outcomes/0/label/textKey'))
    await chooseOption('done', field('/steps/1/outcomes/0/next'))
    await userEvent.click(within(screen.getByTestId('studio-step-1')).getByRole('button', { name: 'Add outcome' }))
    expect((stepOf(latest, 1).outcomes as unknown[]).length).toBe(3)
    await userEvent.click(within(field('/steps/1/outcomes/1')).getByRole('button', { name: 'Remove outcome' }))
    expect((stepOf(latest, 1).outcomes as unknown[])[1]).toEqual({ name: '', label: { textKey: '' }, next: '' })
    await userEvent.click(within(field('/steps/1/outcomes/1')).getByRole('button', { name: 'Remove outcome' }))

    expect(stepOf(latest, 1)).toEqual({
      name: 'managerApproval',
      type: 'task',
      label: { textKey: 'task.return' },
      assignee: { user: 'department.manager' },
      form: 'OtherForm',
      dueIn: 'P3D',
      hint: 1,
      outcomes: [{ name: 'accept', label: { textKey: 'task.return' }, next: 'done', note: 'x' }],
    })

    await userEvent.type(within(field('/steps/1/dueIn')).getByRole('textbox'), 'X')
    expect(stepOf(latest, 1).dueIn).toBe('P3DX')
    await userEvent.clear(within(field('/steps/1/dueIn')).getByRole('textbox'))
    expect(stepOf(latest, 1)).not.toHaveProperty('dueIn')
  })

  it('switches the assignee between a user and a role and keeps only the chosen key', async () => {
    const latest = renderEditor()
    const card = screen.getByTestId('studio-step-1')

    // The radio input of a button group has no pointer events, so the click goes to its label.
    const switchTo = (name: string) => userEvent.click(within(card).getByRole('radio', { name }).closest('label')!)

    await switchTo('Role')
    expect(stepOf(latest, 1).assignee).toEqual({ role: '' })
    await chooseOption('finance-reviewer', screen.getByTestId('studio-field-/steps/1/assignee/role'))
    expect(stepOf(latest, 1).assignee).toEqual({ role: 'finance-reviewer' })
    expect(screen.queryByTestId('studio-field-/steps/1/assignee/user')).toBeNull()

    await switchTo('User')
    expect(stepOf(latest, 1).assignee).toEqual({ user: '' })
    expect(screen.queryByTestId('studio-field-/steps/1/assignee/role')).toBeNull()
    await userEvent.type(within(screen.getByTestId('studio-field-/steps/1/assignee/user')).getByRole('textbox'), 'a')
    expect(stepOf(latest, 1).assignee).toEqual({ user: 'a' })
  })

  it('adds an escalation with a notify and writes to, channels and message without next', async () => {
    const latest = renderEditor()
    const field = (pointer: string) => within(screen.getByTestId(`studio-field-${pointer}`))

    await userEvent.click(field('/steps/1/escalations').getByRole('button', { name: 'Add escalation' }))
    expect(stepOf(latest, 1).escalations).toEqual([{ name: 'escalation1', after: '' }])
    await userEvent.type(field('/steps/1/escalations/0/after').getByRole('textbox'), 'P1D')
    await userEvent.click(field('/steps/1/escalations/0').getByRole('switch', { name: 'Notify' }))
    expect((stepOf(latest, 1).escalations as Record<string, unknown>[])[0].notify).toEqual({
      to: [],
      channels: [],
      message: { subject: { textKey: '' }, body: { textKey: '' } },
    })

    const notify = '/steps/1/escalations/0/notify'
    await userEvent.click(field(`${notify}/to`).getByRole('button', { name: 'Add recipient' }))
    // The radio input of a button group has no pointer events, so the click goes to its label.
    await userEvent.click(field(`${notify}/to/0`).getByRole('radio', { name: 'Role' }).closest('label')!)
    await chooseOption('finance-reviewer', screen.getByTestId(`studio-field-${notify}/to/0/role`))
    await userEvent.click(field(`${notify}/channels`).getByRole('checkbox', { name: 'In-app' }))
    await userEvent.type(field(`${notify}/message/subject`).getByRole('textbox', { name: 'Text key' }), 'task.approve')
    await userEvent.type(field(`${notify}/message/body`).getByRole('textbox', { name: 'Text key' }), 'task.reject')

    expect(stepOf(latest, 1).escalations).toEqual([
      {
        name: 'escalation1',
        after: 'P1D',
        notify: {
          to: [{ role: 'finance-reviewer' }],
          channels: ['inApp'],
          message: { subject: { textKey: 'task.approve' }, body: { textKey: 'task.reject' } },
        },
      },
    ])
    expect((stepOf(latest, 1).escalations as Record<string, unknown>[])[0].notify).not.toHaveProperty('next')

    await userEvent.click(field('/steps/1/escalations/0').getByRole('switch', { name: 'Notify' }))
    expect(stepOf(latest, 1).escalations).toEqual([{ name: 'escalation1', after: 'P1D' }])
  })

  it('turns on reassign for an escalation with its own assignee editor and removes the key when off', async () => {
    const latest = renderEditor()
    const field = (pointer: string) => within(screen.getByTestId(`studio-field-${pointer}`))

    await userEvent.click(field('/steps/1/escalations').getByRole('button', { name: 'Add escalation' }))
    await userEvent.click(field('/steps/1/escalations/0').getByRole('switch', { name: 'Reassign' }))
    expect((stepOf(latest, 1).escalations as Record<string, unknown>[])[0].reassign).toEqual({ user: '' })

    // The task's own assignee stays a user while the escalation's group switches.
    const reassign = field('/steps/1/escalations/0/reassign')
    await userEvent.click(reassign.getByRole('radio', { name: 'Role' }).closest('label')!)
    await chooseOption('finance-reviewer', screen.getByTestId('studio-field-/steps/1/escalations/0/reassign/role'))
    expect((stepOf(latest, 1).escalations as Record<string, unknown>[])[0].reassign).toEqual({
      role: 'finance-reviewer',
    })
    expect(stepOf(latest, 1).assignee).toEqual({ user: 'department.manager' })

    await userEvent.click(field('/steps/1/escalations/0').getByRole('switch', { name: 'Reassign' }))
    expect(stepOf(latest, 1).escalations).toEqual([{ name: 'escalation1', after: '' }])
  })

  it('adds a deadline and removes the key when it is removed', async () => {
    const latest = renderEditor()
    const field = (pointer: string) => within(screen.getByTestId(`studio-field-${pointer}`))

    await userEvent.click(field('/steps/1/deadline').getByRole('button', { name: 'Add deadline' }))
    expect(stepOf(latest, 1).deadline).toEqual({ after: '', next: '' })
    await userEvent.type(field('/steps/1/deadline/after').getByRole('textbox'), 'P5D')
    await chooseOption('submit', screen.getByTestId('studio-field-/steps/1/deadline/next'))
    expect(stepOf(latest, 1).deadline).toEqual({ after: 'P5D', next: 'submit' })

    await userEvent.click(field('/steps/1/deadline').getByRole('button', { name: 'Remove deadline' }))
    expect(stepOf(latest, 1)).not.toHaveProperty('deadline')
    expect(field('/steps/1/deadline').getByRole('button', { name: 'Add deadline' })).toBeInTheDocument()
  })

  it('removes the escalations key when the only escalation is removed, and only the row otherwise', async () => {
    const latest = renderEditor()
    const field = (pointer: string) => within(screen.getByTestId(`studio-field-${pointer}`))
    const add = () => userEvent.click(field('/steps/1/escalations').getByRole('button', { name: 'Add escalation' }))

    await add()
    await userEvent.click(field('/steps/1/escalations/0').getByRole('button', { name: 'Remove escalation' }))
    expect(stepOf(latest, 1)).not.toHaveProperty('escalations')

    await add()
    await add()
    await userEvent.type(field('/steps/1/escalations/1/after').getByRole('textbox'), 'P2D')
    await userEvent.click(field('/steps/1/escalations/0').getByRole('button', { name: 'Remove escalation' }))
    expect(stepOf(latest, 1).escalations).toEqual([{ name: 'escalation2', after: 'P2D' }])
  })

  it('names a new escalation with the first free name, ignoring letter case', async () => {
    const latest = renderEditor({
      ...process,
      steps: (process.steps as Record<string, unknown>[]).map((step, index) =>
        index === 1
          ? { ...step, escalations: [{ name: 'Escalation1', after: 'PT1H', reassign: { user: 'a' } }] }
          : step,
      ),
    })

    await userEvent.click(
      within(screen.getByTestId('studio-field-/steps/1/escalations')).getByRole('button', { name: 'Add escalation' }),
    )

    expect(stepOf(latest, 1).escalations).toEqual([
      { name: 'Escalation1', after: 'PT1H', reassign: { user: 'a' } },
      { name: 'escalation2', after: '' },
    ])
  })

  it('switches the subject between an entity and parameters and keeps only the chosen key', async () => {
    const latest = renderEditor(process, true)
    const offered = async () =>
      ((await screen.findByRole('group', { name: 'When' })).getAttribute('data-options') ?? '').split(',')
    // The radio input of a button group has no pointer events, so the click goes to its label.
    const switchTo = (name: string) => userEvent.click(screen.getByRole('radio', { name }).closest('label')!)

    expect(screen.getByRole('radio', { name: 'Entity' })).toBeChecked()
    expect(await offered()).toContain('totalAmount')

    await switchTo('Parameters')
    expect(latest.value).not.toHaveProperty('entity')
    expect(latest.value.parameters).toEqual([])
    expect(Object.keys(latest.value)).toEqual(['id', 'kind', 'name', 'formatVersion', 'parameters', 'start', 'steps'])
    expect(screen.queryByTestId('studio-field-/entity')).toBeNull()

    await userEvent.click(screen.getByRole('button', { name: 'Add parameter' }))
    expect(latest.value.parameters).toEqual([{ name: '', type: 'text' }])
    await userEvent.type(screen.getByRole('textbox', { name: 'Parameter name' }), 'daysOverdue')
    await chooseOption('integer', screen.getByTestId('studio-field-/parameters/0/type'))
    expect(latest.value.parameters).toEqual([{ name: 'daysOverdue', type: 'integer' }])
    const parameterOptions = await offered()
    expect(parameterOptions).toContain('daysOverdue')
    expect(parameterOptions).not.toContain('totalAmount')
    expect(parameterOptions).not.toContain('id')

    await userEvent.click(screen.getByRole('button', { name: 'Remove parameter' }))
    expect(latest.value.parameters).toEqual([])

    await switchTo('Entity')
    expect(latest.value).not.toHaveProperty('parameters')
    expect(latest.value.entity).toBe('')
    expect(Object.keys(latest.value)).toEqual(['id', 'kind', 'name', 'formatVersion', 'entity', 'start', 'steps'])
    await chooseOption('PurchaseRequest', screen.getByTestId('studio-field-/entity'))
    expect(latest.value.entity).toBe('PurchaseRequest')
    expect(await offered()).toEqual(expect.arrayContaining(['id', 'totalAmount']))
  })

  it('shows a process without a subject in parameters mode', () => {
    renderEditor({ ...process, entity: undefined, parameters: [{ name: 'daysOverdue', type: 'integer' }] })

    expect(screen.getByRole('radio', { name: 'Parameters' })).toBeChecked()
    expect(screen.getByRole('textbox', { name: 'Parameter name' })).toHaveValue('daysOverdue')
    expect(screen.getByTestId('studio-field-/parameters')).toBeVisible()
  })

  it('shows an end step as its name and type only', () => {
    renderEditor()

    const end = screen.getByTestId('studio-step-3')
    expect(within(end).getByRole('textbox', { name: 'Step name' })).toHaveValue('done')
    expect(within(end).queryByTestId('studio-json-only')).toBeNull()
    expect(within(end).queryByRole('combobox', { name: 'Next step' })).toBeNull()
  })

  it('offers every step type of the process schema in the type picker and the Add step choice', async () => {
    const types = schema.$defs!.step.properties!.type.enum
    renderEditor()

    await userEvent.click(within(screen.getByTestId('studio-field-/steps/0/type')).getByRole('combobox'))
    expect(optionTexts(await openDropdown())).toEqual(types)
    await userEvent.keyboard('{Escape}')

    await userEvent.click(screen.getByRole('combobox', { name: 'Add step' }))
    const dropdown = await waitFor(() => {
      const open = [...document.querySelectorAll<HTMLElement>('.ant-select-dropdown')].find(
        (candidate) => !candidate.classList.contains('ant-select-dropdown-hidden') && optionTexts(candidate).length > 0,
      )
      expect(open).toBeDefined()
      return open!
    })
    expect(types).toContain('timer')
    expect(types).toContain('subProcess')
    expect(optionTexts(dropdown)).toEqual(types)
  })

  it('changes a task to a timer by removing the keys the schema forbids and adding the required keys', async () => {
    const latest = renderEditor()

    await chooseOption('timer', screen.getByTestId('studio-field-/steps/1/type'))

    expect(stepOf(latest, 1)).toEqual({ name: 'managerApproval', type: 'timer', hint: 1, wait: '', next: '' })
  })

  it('shows the keys of a step type without a panel as markers for the JSON view', async () => {
    renderEditor({
      ...process,
      steps: [{ name: 'later', type: 'futureStep', event: 'x', next: 'later' }],
    })

    const card = screen.getByTestId('studio-step-0')
    expect(within(card).getByTestId('studio-field-/steps/0/event')).toBeVisible()
    expect(within(card).getByTestId('studio-field-/steps/0/next')).toBeVisible()
    expect(within(card).getAllByTestId('studio-json-only')).toHaveLength(2)
  })

  it('lists only the events about the subject entity in the event picker of a wait for event step', async () => {
    renderEditor({ ...process, steps: [{ name: 'wait', type: 'waitEvent', event: '', next: 'wait' }] })

    await userEvent.click(within(screen.getByTestId('studio-field-/steps/0/event')).getByRole('combobox'))

    expect(optionTexts(await openDropdown())).toEqual(['PaymentReceived'])
  })

  it('offers an event picker with no event to a process without a subject', async () => {
    renderEditor({
      ...process,
      entity: undefined,
      parameters: [],
      steps: [{ name: 'wait', type: 'waitEvent', event: '', next: 'wait' }],
    })

    await userEvent.click(within(screen.getByTestId('studio-field-/steps/0/event')).getByRole('combobox'))

    expect(optionTexts(await openDropdown())).toEqual([])
  })

  it('writes the event, when, timeout and next step of a wait for event step', async () => {
    const latest = renderEditor({
      ...process,
      steps: [
        { name: 'wait', type: 'waitEvent', event: '', next: '' },
        { name: 'chase', type: 'timer', wait: 'PT1H', next: 'done' },
        { name: 'done', type: 'end' },
      ],
    })
    const card = screen.getByTestId('studio-step-0')

    await chooseOption('PaymentReceived', screen.getByTestId('studio-field-/steps/0/event'))
    await userEvent.type(within(screen.getByTestId('studio-field-/steps/0/when')).getByRole('textbox'), 'amount > 0')
    await userEvent.click(within(card).getByRole('button', { name: 'Add timeout' }))
    expect(stepOf(latest, 0).timeout).toEqual({ after: '', next: '' })
    await userEvent.type(within(card).getByRole('textbox', { name: 'After' }), 'P2D')
    await chooseOption('chase', screen.getByTestId('studio-field-/steps/0/timeout/next'))
    await chooseOption('done', screen.getByTestId('studio-field-/steps/0/next'))

    expect(stepOf(latest, 0)).toEqual({
      name: 'wait',
      type: 'waitEvent',
      event: 'PaymentReceived',
      when: 'amount > 0',
      timeout: { after: 'P2D', next: 'chase' },
      next: 'done',
    })
    expect(within(card).queryByTestId('studio-json-only')).toBeNull()
  })

  it('removes the whole timeout of a wait for event step and brings it back empty', async () => {
    const latest = renderEditor({
      ...process,
      steps: [
        {
          name: 'wait',
          type: 'waitEvent',
          event: 'PaymentReceived',
          timeout: { after: 'P2D', next: 'wait' },
          next: 'wait',
        },
      ],
    })
    const card = screen.getByTestId('studio-step-0')
    expect(within(card).getByRole('textbox', { name: 'After' })).toHaveValue('P2D')

    await userEvent.click(within(card).getByRole('button', { name: 'Remove timeout' }))

    expect(stepOf(latest, 0)).not.toHaveProperty('timeout')
    expect(within(card).queryByRole('textbox', { name: 'After' })).toBeNull()
    await userEvent.click(within(card).getByRole('button', { name: 'Add timeout' }))
    expect(stepOf(latest, 0).timeout).toEqual({ after: '', next: '' })
  })

  it('removes the when of a wait for event step when its box is cleared', async () => {
    const latest = renderEditor({
      ...process,
      steps: [{ name: 'wait', type: 'waitEvent', event: 'PaymentReceived', when: 'amount > 0', next: 'wait' }],
    })

    await userEvent.clear(within(screen.getByTestId('studio-field-/steps/0/when')).getByRole('textbox'))

    expect(stepOf(latest, 0)).not.toHaveProperty('when')
  })

  it('offers the when of a wait for event step the payload fields of its event', async () => {
    renderEditor(
      { ...process, steps: [{ name: 'wait', type: 'waitEvent', event: 'PaymentReceived', next: 'wait' }] },
      true,
    )

    const group = await within(screen.getByTestId('studio-field-/steps/0/when')).findByRole('group', { name: 'When' })

    const offered = (group.getAttribute('data-options') ?? '').split(',')
    expect(offered).toContain('amount')
    expect(offered).not.toContain('totalAmount')
  })

  it('writes the process, subject and next step of a sub-process whose child has a subject', async () => {
    const latest = renderEditor({
      ...process,
      steps: [
        { name: 'fulfil', type: 'subProcess', process: '', next: '' },
        { name: 'done', type: 'end' },
      ],
    })
    const card = screen.getByTestId('studio-step-0')

    await chooseOption('OrderFulfilment', screen.getByTestId('studio-field-/steps/0/process'))
    await userEvent.type(within(screen.getByTestId('studio-field-/steps/0/subject')).getByRole('textbox'), 'request')
    await chooseOption('done', screen.getByTestId('studio-field-/steps/0/next'))

    expect(stepOf(latest, 0)).toEqual({
      name: 'fulfil',
      type: 'subProcess',
      process: 'OrderFulfilment',
      subject: 'request',
      next: 'done',
    })
    expect(within(card).queryByTestId('studio-json-only')).toBeNull()
    await userEvent.clear(within(screen.getByTestId('studio-field-/steps/0/subject')).getByRole('textbox'))
    expect(stepOf(latest, 0)).not.toHaveProperty('subject')
  })

  it('shows one row per parameter of a sub-process whose child has no subject and removes the subject', async () => {
    const latest = renderEditor({
      ...process,
      steps: [
        { name: 'fulfil', type: 'subProcess', process: 'OrderFulfilment', subject: 'x', next: 'done' },
        { name: 'done', type: 'end' },
      ],
    })

    await chooseOption('DailyReminder', screen.getByTestId('studio-field-/steps/0/process'))

    expect(stepOf(latest, 0)).toEqual({
      name: 'fulfil',
      type: 'subProcess',
      process: 'DailyReminder',
      parameters: { daysOverdue: '', region: '' },
      next: 'done',
    })
    expect(Object.keys(stepOf(latest, 0))).toEqual(['name', 'type', 'process', 'parameters', 'next'])
    expect(screen.getByTestId('studio-field-/steps/0/parameters/daysOverdue')).toBeVisible()
    expect(screen.getByTestId('studio-field-/steps/0/parameters/region')).toBeVisible()
    expect(screen.queryByTestId('studio-field-/steps/0/subject')).toBeNull()
    expect(within(screen.getByTestId('studio-step-0')).queryByTestId('studio-json-only')).toBeNull()

    await chooseOption('OrderFulfilment', screen.getByTestId('studio-field-/steps/0/process'))
    expect(stepOf(latest, 0)).toEqual({ name: 'fulfil', type: 'subProcess', process: 'OrderFulfilment', next: 'done' })
  })

  it('changes only the process of a sub-process whose child is not in the names list', async () => {
    const latest = renderEditor({
      ...process,
      steps: [{ name: 'fulfil', type: 'subProcess', process: 'Gone', parameters: { a: 'x' }, next: 'fulfil' }],
    })

    expect(screen.getByTestId('studio-field-/steps/0/parameters/a')).toBeVisible()
    await userEvent.clear(within(screen.getByTestId('studio-field-/steps/0/process')).getByRole('combobox'))
    expect(stepOf(latest, 0).parameters).toEqual({ a: 'x' })
  })

  it('edits a timer through its own fields and switches between Wait and Until', async () => {
    const latest = renderEditor()
    const card = screen.getByTestId('studio-step-4')
    // The radio input of a button group has no pointer events, so the click goes to its label.
    const switchTo = (name: string) => userEvent.click(within(card).getByRole('radio', { name }).closest('label')!)

    expect(within(card).getByRole('radio', { name: 'Wait' })).toBeChecked()
    expect(within(card).getByRole('textbox', { name: 'Duration' })).toHaveValue('PT1H')
    await userEvent.type(within(card).getByRole('textbox', { name: 'Duration' }), '2')
    expect(stepOf(latest, 4).wait).toBe('PT1H2')
    await userEvent.clear(within(card).getByRole('textbox', { name: 'Duration' }))
    expect(stepOf(latest, 4).wait).toBe('')
    await userEvent.type(within(card).getByRole('textbox', { name: 'Duration' }), 'PT2H')
    await chooseOption('financeCheck', screen.getByTestId('studio-field-/steps/4/next'))
    expect(stepOf(latest, 4)).toEqual({ name: 'pause', type: 'timer', wait: 'PT2H', next: 'financeCheck' })

    await switchTo('Until')
    expect(stepOf(latest, 4)).toEqual({ name: 'pause', type: 'timer', until: '', next: 'financeCheck' })
    expect(Object.keys(stepOf(latest, 4))).toEqual(['name', 'type', 'until', 'next'])
    expect(screen.queryByTestId('studio-field-/steps/4/wait')).toBeNull()
    await userEvent.type(within(screen.getByTestId('studio-field-/steps/4/until')).getByRole('textbox'), 'dueDate')
    expect(stepOf(latest, 4).until).toBe('dueDate')

    await switchTo('Wait')
    expect(stepOf(latest, 4)).toEqual({ name: 'pause', type: 'timer', wait: '', next: 'financeCheck' })
    expect(stepOf(latest, 4)).not.toHaveProperty('until')
    expect(screen.queryByTestId('studio-field-/steps/4/until')).toBeNull()
  })

  it('opens a timer with only until in Until mode and keeps the value of a key that a switch finds', async () => {
    const latest = renderEditor({
      ...process,
      steps: [
        { name: 'a', type: 'timer', until: 'dueDate', next: 'b' },
        { name: 'b', type: 'timer', wait: 'PT1H', until: 'x', next: 'a' },
        { name: 'c', type: 'timer', next: 'a' },
      ],
    })

    expect(within(screen.getByTestId('studio-step-0')).getByRole('radio', { name: 'Until' })).toBeChecked()
    expect(within(screen.getByTestId('studio-step-1')).getByRole('radio', { name: 'Wait' })).toBeChecked()
    expect(within(screen.getByTestId('studio-step-2')).getByRole('radio', { name: 'Wait' })).toBeChecked()

    await userEvent.click(
      within(screen.getByTestId('studio-step-1')).getByRole('radio', { name: 'Until' }).closest('label')!,
    )
    expect(stepOf(latest, 1)).toEqual({ name: 'b', type: 'timer', until: 'x', next: 'a' })
  })

  it('shows the expression editor for until when the draft can check expressions', async () => {
    renderEditor({ ...process, steps: [{ name: 'a', type: 'timer', until: 'dueDate', next: 'a' }] }, true)

    expect(await screen.findByRole('group', { name: 'Time' })).toBeVisible()
  })
})
