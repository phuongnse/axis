import { EditorView } from '@codemirror/view'
import { act, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import type { EntityWidgetMetadata, FieldMetadata, FieldType } from '../platform/site'
import type { StudioDiagnostic, StudioNames, StudioResourceItem } from '../platform/studio'
import { StudioNamesContext } from '../platform/studioNames'
import { loadKindSchema } from '../platform/studioSchemas'
import { ThemeModeProvider } from '../platform/theme'
import { TextProvider } from '../platform/TextProvider'
import { StudioEditor } from './StudioEditor'

const folder = '/api/studio/apps/Desk'
const formId = '1db765b0-483d-456b-9939-17e2a36de3b6'
const item: StudioResourceItem = {
  id: formId,
  kind: 'form',
  name: 'TicketEdit',
  file: 'forms/ticket-edit.json',
  version: 'f1',
  usedBy: 0,
}

const names = {
  locales: ['en'],
  entities: [],
  dataSources: [],
  processes: [],
  rules: [],
  connectors: [],
  events: [],
  roles: [],
  sequences: [],
  forms: [],
  pages: [],
  texts: [
    { key: 'ticket.title', texts: { en: 'Title' }, usedBy: 1 },
    { key: 'ticket.status', texts: { en: 'Status' }, usedBy: 1 },
    { key: 'ticket.resolution', texts: { en: 'Resolution' }, usedBy: 1 },
    { key: 'ticket.sections.problem', texts: { en: 'The problem' }, usedBy: 1 },
    { key: 'ticket.sections.details', texts: { en: 'The details' }, usedBy: 1 },
    { key: 'ticket.steps.problem', texts: { en: 'Describe the problem' }, usedBy: 1 },
    { key: 'ticket.steps.details', texts: { en: 'Add details' }, usedBy: 1 },
  ],
} as unknown as StudioNames

function field(name: string, type: FieldType): FieldMetadata {
  return {
    name,
    type,
    labelKey: `ticket.${name}`,
    required: false,
    unique: false,
    computed: false,
    sequence: false,
    creator: false,
    maxLength: null,
    precision: null,
    scale: null,
    values: null,
    target: null,
    fields: null,
  }
}

/** The form widget the route answers: a ticket with a title, a status and a resolution in two steps. */
const widget: EntityWidgetMetadata = {
  type: 'form',
  dataSource: null,
  tasksPath: null,
  process: null,
  instancesPath: null,
  actions: [],
  rowActions: [],
  toolbarActions: [],
  parameters: {},
  access: null,
  detail: null,
  kpi: null,
  chart: null,
  entity: {
    name: 'Ticket',
    labelKey: 'ticket.label',
    displayField: null,
    recordsPath: '/api/apps/Desk/entities/Ticket/records',
    fields: [
      field('title', 'text'),
      { ...field('status', 'enum'), values: ['open', 'resolved'] },
      field('resolution', 'text'),
    ],
  },
  form: {
    name: 'TicketEdit',
    sections: [
      {
        name: 'problem',
        titleKey: 'ticket.sections.problem',
        path: '/sections/0',
        fields: [{ name: 'title', readOnly: false, path: '/sections/0/fields/0' }],
      },
      {
        name: 'details',
        titleKey: 'ticket.sections.details',
        path: '/sections/1',
        fields: [
          { name: 'status', readOnly: false, path: '/sections/1/fields/0' },
          { name: 'resolution', readOnly: false, path: '/sections/1/fields/1' },
        ],
      },
    ],
    steps: [
      { titleKey: 'ticket.steps.problem', sections: ['problem'], path: '/steps/0' },
      { titleKey: 'ticket.steps.details', sections: ['details'], path: '/steps/1' },
    ],
  },
}

const draft = {
  id: formId,
  kind: 'form',
  name: 'TicketEdit',
  formatVersion: 1,
  entity: 'Ticket',
  sections: [
    { name: 'problem', title: { textKey: 'ticket.sections.problem' }, fields: [{ field: 'title' }] },
    {
      name: 'details',
      title: { textKey: 'ticket.sections.details' },
      visibleWhen: "status != 'closed'",
      fields: [
        { field: 'status' },
        { field: 'resolution', visibleWhen: "status == 'resolved'", requiredWhen: "status == 'resolved'" },
      ],
    },
  ],
  steps: [
    { title: { textKey: 'ticket.steps.problem' }, sections: ['problem'] },
    { title: { textKey: 'ticket.steps.details' }, sections: ['details'] },
  ],
}

/** The widget with a required title and a computed number in the first section, and the draft that lists them. */
const requiredWidget: EntityWidgetMetadata = {
  ...widget,
  entity: {
    ...widget.entity,
    fields: [
      { ...field('title', 'text'), required: true },
      ...widget.entity.fields.slice(1),
      { ...field('number', 'integer'), computed: true },
    ],
  },
  form: {
    ...widget.form!,
    sections: [
      {
        ...widget.form!.sections[0],
        fields: [widget.form!.sections[0].fields[0], { name: 'number', readOnly: true, path: '/sections/0/fields/1' }],
      },
      widget.form!.sections[1],
    ],
  },
}

const requiredDraft = {
  ...draft,
  sections: [{ ...draft.sections[0], fields: [{ field: 'title' }, { field: 'number' }] }, draft.sections[1]],
}

function diagnosticAt(path: string): StudioDiagnostic {
  return {
    code: 'AXC0076',
    severity: 'error',
    message: 'The expression does not compile.',
    file: 'forms/ticket-edit.json',
    path,
    resourceId: formId,
  }
}

function respond(answer: EntityWidgetMetadata | null, diagnostics: StudioDiagnostic[] = []) {
  return new Response(JSON.stringify({ widget: answer, diagnostics }), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })
}

/** Serves the resource and answers each page-metadata post. */
function stub(answer: () => Response = () => respond(widget), content: object = draft) {
  const fetchMock = vi.fn(async (input: string, init?: RequestInit) => {
    if (input === `${folder}/resources/${formId}/page-metadata` && init?.method === 'POST') {
      return answer()
    }
    if (input === `${folder}/resources/${formId}`) {
      return new Response(JSON.stringify({ ...item, content }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      })
    }
    return new Response(null, { status: 404 })
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

function renderEditor() {
  render(
    <MemoryRouter initialEntries={['/studio?app=Desk&resource=ticket-edit']}>
      <ThemeModeProvider>
        <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
          <StudioNamesContext.Provider value={names}>
            <StudioEditor
              application="Desk"
              item={item}
              resources={[item]}
              path={null}
              diagnosticPaths={[]}
              onOpenPath={vi.fn()}
              onSaved={vi.fn()}
              onTextsWritten={vi.fn()}
              onDeleted={vi.fn()}
            />
          </StudioNamesContext.Provider>
        </TextProvider>
      </ThemeModeProvider>
    </MemoryRouter>,
  )
}

const slow = { timeout: 5000 }

async function openDesign() {
  await screen.findByTestId('studio-editor', {}, slow)
  await userEvent.click(screen.getByRole('tab', { name: 'Design' }))
  return screen.findByTestId('studio-form-canvas', {}, slow)
}

/** The CodeMirror view of the expression editor with the given name inside `panel`. */
async function expressionView(panel: HTMLElement, name: string): Promise<EditorView> {
  let view: EditorView | null = null
  // The editor chunk loads after the panel shows a read-only box.
  await waitFor(() => {
    view = EditorView.findFromDOM(within(panel).getByRole('textbox', { name }))
    expect(view).not.toBeNull()
  }, slow)
  return view as unknown as EditorView
}

function metadataPosts(fetchMock: ReturnType<typeof vi.fn>): Record<string, any>[] {
  return fetchMock.mock.calls
    .filter(([input, init]) => input === `${folder}/resources/${formId}/page-metadata` && init?.method === 'POST')
    .map(([, init]) => JSON.parse(init.body as string))
}

function appRequests(fetchMock: ReturnType<typeof vi.fn>) {
  return fetchMock.mock.calls.filter(([input]) => String(input).startsWith('/api/apps/'))
}

beforeAll(() => loadKindSchema('form'))

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('the Design tab of a form', () => {
  it('draws the fields with placeholder values and sends no request to the application', async () => {
    const fetchMock = stub()
    renderEditor()

    await screen.findByTestId('studio-editor', {}, slow)
    expect(screen.queryByTestId('studio-form-canvas')).not.toBeInTheDocument()

    const canvas = await openDesign()
    await waitFor(
      () => expect(within(canvas).getByRole('textbox', { name: 'Title' })).toHaveValue('Sample title 1'),
      slow,
    )
    expect(appRequests(fetchMock)).toEqual([])
  })

  it('shows the sections of the step that the switch picks', async () => {
    stub()
    renderEditor()

    const canvas = await openDesign()
    const steps = await screen.findByTestId('studio-form-steps', {}, slow)
    expect(within(steps).getByText('Describe the problem')).toBeInTheDocument()
    await within(canvas).findByRole('textbox', { name: 'Title' }, slow)
    expect(within(canvas).queryByRole('textbox', { name: 'Resolution' })).not.toBeInTheDocument()

    await userEvent.click(within(steps).getByText('Add details'))

    expect(await within(canvas).findByRole('textbox', { name: 'Resolution' }, slow)).toBeInTheDocument()
    expect(within(canvas).queryByRole('textbox', { name: 'Title' })).not.toBeInTheDocument()
  })

  it('shows the text of each condition on the section and the field that has it', async () => {
    stub()
    renderEditor()

    const canvas = await openDesign()
    await userEvent.click(within(await screen.findByTestId('studio-form-steps', {}, slow)).getByText('Add details'))

    const field = await within(canvas).findByRole('group', { name: 'resolution' }, slow)
    const marks = within(field).getAllByTestId('studio-condition-mark')
    expect(marks.map((mark) => mark.dataset.condition)).toEqual(['visibleWhen', 'requiredWhen'])
    expect(marks[0]).toHaveTextContent("Visible when: status == 'resolved'")
    expect(marks[1]).toHaveTextContent("Required when: status == 'resolved'")
    const section = within(canvas).getByRole('group', { name: 'The details' })
    expect(within(section).getAllByTestId('studio-condition-mark')[0]).toHaveTextContent(
      "Visible when: status != 'closed'",
    )
    expect(
      within(within(canvas).getByRole('group', { name: 'status' })).queryByTestId('studio-condition-mark'),
    ).toBeNull()
  })

  it('marks the field, the section and the step that a diagnostic names', async () => {
    stub(() => respond(widget, [diagnosticAt('/sections/1/fields/1/visibleWhen'), diagnosticAt('/steps/0')]))
    renderEditor()

    const canvas = await openDesign()
    const steps = await screen.findByTestId('studio-form-steps', {}, slow)
    await waitFor(
      () => expect(within(steps).getByText(/Describe the problem · has diagnostics/)).toBeInTheDocument(),
      slow,
    )
    expect(within(steps).getByText(/Describe the problem/)).toHaveAttribute('data-marked', 'true')
    expect(within(steps).getByText('Add details')).not.toHaveAttribute('data-marked')

    await userEvent.click(within(steps).getByText('Add details'))
    const field = await within(canvas).findByRole('group', { name: 'resolution · has diagnostics' }, slow)
    expect(field).toHaveAttribute('data-marked', 'true')
    expect(field).toHaveAttribute('data-path', '/sections/1/fields/1')
    expect(within(canvas).getByRole('group', { name: 'status' })).not.toHaveAttribute('data-marked')
    expect(within(canvas).getByRole('group', { name: 'The details · has diagnostics' })).toHaveAttribute(
      'data-marked',
      'true',
    )
  })

  it('tells that the form cannot be drawn when the answer has no widget', async () => {
    stub(() => respond(null, [diagnosticAt('/entity')]))
    renderEditor()

    await openDesign()

    expect(await screen.findByTestId('studio-canvas-cannot-draw', {}, slow)).toHaveTextContent(
      'The form cannot be drawn until its errors are fixed.',
    )
    expect(screen.queryByTestId('studio-form-steps')).not.toBeInTheDocument()
  })

  it('asks the author to select something while nothing is selected', async () => {
    stub()
    renderEditor()

    await openDesign()

    expect(await screen.findByTestId('studio-design-hint', {}, slow)).toHaveTextContent(
      'Select a field, section or step on the canvas.',
    )
  })

  it('edits the visibleWhen of the selected field and posts the new draft', async () => {
    const fetchMock = stub()
    renderEditor()

    const canvas = await openDesign()
    await userEvent.click(within(await screen.findByTestId('studio-form-steps', {}, slow)).getByText('Add details'))
    const field = await within(canvas).findByRole('group', { name: 'resolution' }, slow)
    await userEvent.click(field)

    expect(field).toHaveAttribute('aria-current', 'true')
    const panel = screen.getByTestId('studio-design-panel')
    const view = await expressionView(panel, 'Visible when')
    act(() => view.dispatch({ changes: { from: 0, to: view.state.doc.length, insert: "status != 'open'" } }))

    await waitFor(
      () =>
        expect(
          metadataPosts(fetchMock).some((body) => body.sections[1].fields[1].visibleWhen === "status != 'open'"),
        ).toBe(true),
      slow,
    )
  })

  it('selects a field with Enter', async () => {
    stub()
    renderEditor()

    const canvas = await openDesign()
    const field = await within(canvas).findByRole('group', { name: 'title' }, slow)
    field.focus()
    await userEvent.keyboard('{Enter}')

    expect(field).toHaveAttribute('aria-current', 'true')
    expect(within(screen.getByTestId('studio-design-panel')).getByRole('checkbox', { name: 'Read-only' })).toBeEnabled()
  })

  it('has no visibleWhen box for a field that the entity requires', async () => {
    stub(() => respond(requiredWidget), requiredDraft)
    renderEditor()

    const canvas = await openDesign()
    await userEvent.click(await within(canvas).findByRole('group', { name: 'title' }, slow))

    const panel = screen.getByTestId('studio-design-panel')
    expect(await within(panel).findByRole('textbox', { name: 'Required when' }, slow)).toBeInTheDocument()
    expect(within(panel).getByRole('textbox', { name: 'Read-only when' })).toBeInTheDocument()
    expect(within(panel).queryByRole('textbox', { name: 'Visible when' })).not.toBeInTheDocument()
  })

  it('shows readOnly on and locked for a computed field', async () => {
    stub(() => respond(requiredWidget), requiredDraft)
    renderEditor()

    const canvas = await openDesign()
    await userEvent.click(await within(canvas).findByRole('group', { name: 'number' }, slow))

    const checkbox = within(screen.getByTestId('studio-design-panel')).getByRole('checkbox', { name: 'Read-only' })
    expect(checkbox).toBeChecked()
    expect(checkbox).toBeDisabled()
    expect(screen.getByTestId('studio-design-panel')).toHaveTextContent('The server sets this field')
  })

  it('selects a step in the switch, shows its title and shows its sections', async () => {
    stub()
    renderEditor()

    const canvas = await openDesign()
    const steps = await screen.findByTestId('studio-form-steps', {}, slow)
    await userEvent.click(within(steps).getByText('Add details'))

    const panel = screen.getByTestId('studio-design-panel')
    const title = within(panel).getByTestId('studio-field-/steps/1/title')
    expect(within(title).getByRole('textbox', { name: 'Text key' })).toHaveValue('ticket.steps.details')
    expect(within(steps).getByText('Add details')).toHaveAttribute('aria-current', 'true')
    expect(await within(canvas).findByRole('textbox', { name: 'Resolution' }, slow)).toBeInTheDocument()
  })

  it('selects a section and shows its title and visibleWhen', async () => {
    stub()
    renderEditor()

    const canvas = await openDesign()
    await userEvent.click(within(await screen.findByTestId('studio-form-steps', {}, slow)).getByText('Add details'))
    const section = await within(canvas).findByRole('group', { name: 'The details' }, slow)
    await userEvent.click(section)

    expect(section).toHaveAttribute('aria-current', 'true')
    const panel = screen.getByTestId('studio-design-panel')
    expect(
      within(within(panel).getByTestId('studio-field-/sections/1/title')).getByRole('textbox', { name: 'Text key' }),
    ).toHaveValue('ticket.sections.details')
    expect(await within(panel).findByRole('textbox', { name: 'Visible when' }, slow)).toBeInTheDocument()
  })

  it('keeps the selection across the tabs', async () => {
    stub()
    renderEditor()

    const canvas = await openDesign()
    await userEvent.click(await within(canvas).findByRole('group', { name: 'title' }, slow))
    await userEvent.click(screen.getByRole('tab', { name: 'Form' }))
    await userEvent.click(screen.getByRole('tab', { name: 'Design' }))

    const again = await screen.findByTestId('studio-form-canvas', {}, slow)
    expect(await within(again).findByRole('group', { name: 'title' }, slow)).toHaveAttribute('aria-current', 'true')
  })
})
