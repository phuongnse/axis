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
import { ProcessEditor } from './ProcessEditor'

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
      branches: [{ when: 'totalAmount >= FinanceThreshold()', next: 'done' }],
      otherwise: 'missing',
    },
    { name: 'done', type: 'end' },
  ],
}

const names = {
  locales: ['en'],
  entities: [],
  dataSources: [],
  processes: [],
  rules: [],
  connectors: [],
  events: [],
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
function renderEditor(value: Record<string, unknown> = process) {
  const latest = { value }
  function Harness() {
    const [draft, setDraft] = useState(value)
    useEffect(() => {
      latest.value = draft
    })
    return (
      <ProcessEditor
        schema={schema}
        value={draft}
        onChange={(pointer, next) => setDraft((current) => setAt(current, pointer, next) as Record<string, unknown>)}
      />
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

/** Opens the picker in a field and clicks one of its options. */
async function choose(field: HTMLElement, option: string) {
  await userEvent.click(within(field).getByRole('combobox'))
  // A dropdown that closed a moment ago may still be in the page, so take the one that is open and has the option.
  const dropdown = await waitFor(() => {
    const open = [
      ...document.querySelectorAll<HTMLElement>(
        '.ant-select-dropdown:not(.ant-select-dropdown-hidden):not(.ant-slide-up-leave)',
      ),
    ].find((candidate) => within(candidate).queryByTitle(option) !== null)
    expect(open).toBeDefined()
    return open!
  })
  await userEvent.click(within(dropdown).getByTitle(option))
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
    expect(screen.getByRole('combobox', { name: 'Start step' }).closest('.ant-select')).toBeVisible()
    for (let index = 0; index < process.steps.length; index++) {
      expect(screen.getByTestId(`studio-step-${index}`)).toBeVisible()
    }
    expect(screen.getByTestId('studio-step-2')).toHaveTextContent('financeCheck · decision')
    expect(screen.getByRole('combobox', { name: 'Add step' }).closest('.ant-select')).toBeVisible()
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

  it('points to the JSON view for the other properties of an operation, and not for a task', () => {
    renderEditor()

    const set = screen.getByTestId('studio-field-/steps/0/set')
    expect(within(set).getByTestId('studio-json-only')).toBeVisible()
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

    await choose(field('/steps/1/label/textKey'), 'task.return')
    await choose(field('/steps/1/form'), 'OtherForm')
    await userEvent.clear(within(field('/steps/1/outcomes/0/name')).getByRole('textbox'))
    await userEvent.type(within(field('/steps/1/outcomes/0/name')).getByRole('textbox'), 'accept')
    await choose(field('/steps/1/outcomes/0/label/textKey'), 'task.return')
    await choose(field('/steps/1/outcomes/0/next'), 'done')
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
    await choose(screen.getByTestId('studio-field-/steps/1/assignee/role'), 'finance-reviewer')
    expect(stepOf(latest, 1).assignee).toEqual({ role: 'finance-reviewer' })
    expect(screen.queryByTestId('studio-field-/steps/1/assignee/user')).toBeNull()

    await switchTo('User')
    expect(stepOf(latest, 1).assignee).toEqual({ user: '' })
    expect(screen.queryByTestId('studio-field-/steps/1/assignee/role')).toBeNull()
    await userEvent.type(within(screen.getByTestId('studio-field-/steps/1/assignee/user')).getByRole('textbox'), 'a')
    expect(stepOf(latest, 1).assignee).toEqual({ user: 'a' })
  })

  it('shows an end step as its name and type only', () => {
    renderEditor()

    const end = screen.getByTestId('studio-step-3')
    expect(within(end).getByRole('textbox', { name: 'Step name' })).toHaveValue('done')
    expect(within(end).queryByTestId('studio-json-only')).toBeNull()
    expect(within(end).queryByRole('combobox', { name: 'Next step' })).toBeNull()
  })
})
