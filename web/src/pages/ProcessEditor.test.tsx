import { render, screen, within } from '@testing-library/react'
import type { ReactNode } from 'react'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { TextProvider } from '../platform/TextProvider'
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
      outcomes: [
        { name: 'approve', label: { textKey: 'task.approve' }, next: 'financeCheck' },
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

/** The theme root of the shell: an element that carries the mode the provider chose. */
function ThemeRoot({ children }: { children: ReactNode }) {
  return <div data-theme-mode={useThemeMode().mode}>{children}</div>
}

let schema: JsonSchema

function renderEditor(value: Record<string, unknown> = process, onChange = vi.fn()) {
  render(
    <ThemeModeProvider>
      <ThemeRoot>
        <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
          <ProcessEditor schema={schema} value={value} onChange={onChange} />
        </TextProvider>
      </ThemeRoot>
    </ThemeModeProvider>,
  )
  return onChange
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

  it('points to the JSON view for the other properties of an operation and a task', () => {
    renderEditor()

    const set = screen.getByTestId('studio-field-/steps/0/set')
    expect(within(set).getByTestId('studio-json-only')).toBeVisible()
    expect(screen.getByTestId('studio-field-/steps/0/next')).toBeVisible()
    expect(within(screen.getByTestId('studio-field-/steps/1/dueIn')).getByTestId('studio-json-only')).toBeVisible()
    expect(within(screen.getByTestId('studio-field-/steps/1/outcomes')).getByTestId('studio-json-only')).toBeVisible()
  })

  it('shows an end step as its name and type only', () => {
    renderEditor()

    const end = screen.getByTestId('studio-step-3')
    expect(within(end).getByRole('textbox', { name: 'Step name' })).toHaveValue('done')
    expect(within(end).queryByTestId('studio-json-only')).toBeNull()
    expect(within(end).queryByRole('combobox', { name: 'Next step' })).toBeNull()
  })
})
