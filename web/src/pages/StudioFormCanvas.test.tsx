import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import type { EntityWidgetMetadata, FieldMetadata, FieldType } from '../platform/site'
import type {
  StudioDiagnostic,
  StudioNames,
  StudioReference,
  StudioResourceItem,
  StudioResourceReferences,
} from '../platform/studio'
import { StudioNamesContext } from '../platform/studioNames'
import { loadKindSchema } from '../platform/studioSchemas'
import { ThemeModeProvider } from '../platform/theme'
import { TextProvider } from '../platform/TextProvider'
import { StudioEditor } from './StudioEditor'
import type { OpenResource } from './StudioReferences'

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

/** Serves the resource, its references and answers each page-metadata post. */
function stub(
  answer: () => Response = () => respond(widget),
  references: StudioResourceReferences = { uses: [], usedBy: [] },
) {
  const fetchMock = vi.fn(async (input: string, init?: RequestInit) => {
    if (input === `${folder}/resources/${formId}/page-metadata` && init?.method === 'POST') {
      return answer()
    }
    if (input === `${folder}/resources/${formId}/references`) {
      return new Response(JSON.stringify(references), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      })
    }
    if (input === `${folder}/resources/${formId}`) {
      return new Response(JSON.stringify({ ...item, content: draft }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      })
    }
    return new Response(null, { status: 404 })
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

function renderEditor(onOpen: OpenResource = vi.fn()) {
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
              onOpen={onOpen}
              onSaved={vi.fn()}
              onTextsWritten={vi.fn()}
              onDeleted={vi.fn()}
            />
          </StudioNamesContext.Provider>
        </TextProvider>
      </ThemeModeProvider>
    </MemoryRouter>,
  )
  return onOpen
}

const slow = { timeout: 5000 }

async function openDesign() {
  await screen.findByTestId('studio-editor', {}, slow)
  await userEvent.click(screen.getByRole('tab', { name: 'Design' }))
  return screen.findByTestId('studio-form-canvas', {}, slow)
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
})

describe('the Used on list of a form', () => {
  const pageUse: StudioReference = {
    relation: 'widget form',
    file: 'pages/tickets.json',
    path: '/widgets/0/form',
    node: { type: 'resource', id: 'tickets-page', kind: 'page', name: 'Tickets', file: 'pages/tickets.json' },
  }
  const processUse: StudioReference = {
    relation: 'task form',
    file: 'processes/triage.json',
    path: '/steps/1/form',
    node: { type: 'resource', id: 'triage-process', kind: 'process', name: 'Triage', file: 'processes/triage.json' },
  }
  const otherUse: StudioReference = {
    relation: 'widget action',
    file: 'pages/tickets.json',
    path: '/widgets/0/actions/0',
    node: { type: 'resource', id: 'tickets-page', kind: 'page', name: 'Tickets', file: 'pages/tickets.json' },
  }
  const entityUse: StudioReference = {
    relation: 'form entity',
    file: 'forms/ticket-edit.json',
    path: '/entity',
    node: { type: 'resource', id: 'ticket', kind: 'entity', name: 'Ticket', file: 'entities/ticket.json' },
  }

  it('lists only the widget form and task form entries', async () => {
    stub(undefined, { uses: [entityUse], usedBy: [pageUse, otherUse, processUse] })
    renderEditor()

    await openDesign()
    const section = await screen.findByTestId('studio-form-used-on', {}, slow)
    const entries = await within(section).findAllByTestId('studio-form-used-on-entry')
    expect(entries).toHaveLength(2)
    expect(within(entries[0]).getByText('widget form')).toBeInTheDocument()
    expect(within(entries[0]).getByText('/widgets/0/form', { exact: false })).toBeInTheDocument()
    expect(within(entries[1]).getByText('task form')).toBeInTheDocument()
    expect(within(entries[1]).getByText('/steps/1/form', { exact: false })).toBeInTheDocument()
  })

  it('opens the process at the path of its entry when the entry is clicked', async () => {
    stub(undefined, { uses: [], usedBy: [processUse] })
    const onOpen = renderEditor()

    await openDesign()
    const section = await screen.findByTestId('studio-form-used-on', {}, slow)
    await userEvent.click(await within(section).findByRole('button', { name: 'Triage' }))
    expect(onOpen).toHaveBeenCalledWith('triage-process', '/steps/1/form')
  })

  it('says that no page or process uses the form when no entry is a widget or task form', async () => {
    stub(undefined, { uses: [], usedBy: [otherUse] })
    renderEditor()

    await openDesign()
    const section = await screen.findByTestId('studio-form-used-on', {}, slow)
    expect(within(section).getByText(platformEnglish['shell.studio.design.usedOnNone'])).toBeInTheDocument()
    expect(within(section).queryByTestId('studio-form-used-on-entry')).not.toBeInTheDocument()
  })
})
