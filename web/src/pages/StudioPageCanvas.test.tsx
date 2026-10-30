import type { DndContextProps } from '@dnd-kit/core'
import type { ReactNode } from 'react'
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useLocation } from 'react-router'
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { chartWidget, detailWidget, formWidget, kpiWidget, tableWidget } from '../designSystem/fixtures'
import type { ChartProps } from '../platform/Chart'
import type { NavigateAction, PageMetadata, PageParameter, PageWidgetMetadata } from '../platform/site'
import type { StudioDiagnostic, StudioNames, StudioResourceItem } from '../platform/studio'
import { StudioNamesContext } from '../platform/studioNames'
import { newWidget } from '../platform/pageWidgets'
import { loadKindSchema } from '../platform/studioSchemas'
import { ThemeModeProvider } from '../platform/theme'
import { TextProvider } from '../platform/TextProvider'
import { chooseOption } from '../test/antd'
import { StudioEditor } from './StudioEditor'
import { StudioPageCanvas } from './StudioPageCanvas'

// The drag library draws as it is, and the test calls the drop handler that the page designer gives it, because jsdom
// has no layout to hit-test.
const dnd = vi.hoisted(() => ({ props: undefined as DndContextProps | undefined }))
vi.mock('@dnd-kit/core', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@dnd-kit/core')>()
  return {
    ...actual,
    DndContext: (props: DndContextProps) => {
      dnd.props = props
      return <actual.DndContext {...props} />
    },
    // The real overlay draws only while the library holds a drag, which the test does not start.
    DragOverlay: ({ children }: { children?: ReactNode }) => <div data-testid="studio-drag-overlay">{children}</div>,
  }
})

// The chart library is not under test here. The counter records each draw of a chart.
const chartDraws = vi.hoisted(() => ({ count: 0 }))
vi.mock('../platform/Chart', () => ({
  Chart: (props: ChartProps) => {
    chartDraws.count++
    return <div role="img" aria-label={props.title} />
  },
}))

const folder = '/api/studio/apps/Shop'
const pageId = 'b5a5f1cb-4f0f-4f3d-9a54-9d6e3a1b7f10'
const item: StudioResourceItem = {
  id: pageId,
  kind: 'page',
  name: 'Dashboard',
  file: 'pages/dashboard.json',
  version: 'p1',
  usedBy: 0,
}

type Draft = { widgets: Record<string, unknown>[] }

const names = {
  locales: ['en', 'vi'],
  entities: [],
  dataSources: [],
  processes: [],
  rules: [],
  connectors: [],
  events: [],
  roles: [],
  sequences: [],
  forms: [],
  pages: [{ id: 'g1', name: 'Tickets', parameters: [] }],
  texts: [
    { key: 'dashboard.title', texts: { en: 'Dashboard', vi: 'Bảng điều khiển' }, usedBy: 1 },
    { key: 'kpi.amount', texts: { en: 'Total amount', vi: 'Tổng số tiền' }, usedBy: 1 },
    { key: 'kpi.requests', texts: { en: 'Requests' }, usedBy: 1 },
    { key: 'chart.requestsByStatus', texts: { en: 'Requests by status' }, usedBy: 1 },
    { key: 'tickets.show', texts: { en: 'Show tickets' }, usedBy: 1 },
  ],
} as unknown as StudioNames

const showTickets: NavigateAction = {
  type: 'navigate',
  page: 'Tickets',
  labelKey: 'tickets.show',
  parameters: {},
  record: null,
  opensForm: false,
  paths: {},
  visibleWhen: null,
}

function content(widgets: Record<string, unknown>[]) {
  return {
    id: pageId,
    kind: 'page',
    name: 'Dashboard',
    formatVersion: 1,
    title: { textKey: 'dashboard.title' },
    widgets,
  }
}

/** What the route answers for a draft: each widget by its type, and a placeholder for any other type. */
function metadataOf(draft: Draft): PageMetadata {
  const widgets = draft.widgets.map((widget, index): PageWidgetMetadata => {
    const path = `/widgets/${index}`
    switch (widget.type) {
      case 'kpi':
        return widget.navigate
          ? { ...kpiWidget, path, kpi: { ...kpiWidget.kpi!, navigate: showTickets } }
          : { ...kpiWidget, path }
      case 'chart':
        return { ...chartWidget('bar'), path }
      default:
        return { type: widget.type, path, placeholder: true } as unknown as PageWidgetMetadata
    }
  })
  return { name: 'Dashboard', titleKey: 'dashboard.title', parameters: [], widgets }
}

/** Serves the resource and answers each page-metadata post, recording the posted drafts. */
function stub(
  saved: unknown,
  answer: (draft: Draft) => Response | Promise<Response> = (draft) =>
    new Response(JSON.stringify({ page: metadataOf(draft), diagnostics: [] }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    }),
) {
  const posts: Draft[] = []
  const fetchMock = vi.fn(async (input: string, init?: RequestInit) => {
    if (input === `${folder}/resources/${pageId}/page-metadata` && init?.method === 'POST') {
      const draft = JSON.parse(init.body as string) as Draft
      posts.push(draft)
      return answer(draft)
    }
    if (input === `${folder}/resources/${pageId}`) {
      return new Response(JSON.stringify({ ...item, content: saved }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      })
    }
    return new Response(null, { status: 404 })
  })
  vi.stubGlobal('fetch', fetchMock)
  return { fetchMock, posts }
}

/** The answer to a draft's post with the given page and diagnostics. */
function respond(page: PageMetadata | null, diagnostics: StudioDiagnostic[]) {
  return new Response(JSON.stringify({ page, diagnostics }), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })
}

/** Answers each post at once until `hold`, then holds each answer until `release` gives the oldest one. */
function gatedAnswers() {
  let holding = false
  const held: (() => void)[] = []
  return {
    answer: (draft: Draft): Response | Promise<Response> => {
      if (!holding) {
        return respond(metadataOf(draft), [])
      }
      return new Promise<Response>((resolve) => {
        held.push(() => resolve(respond(metadataOf(draft), [])))
      })
    },
    hold: () => {
      holding = true
    },
    release: () => held.shift()!(),
  }
}

function diagnosticAt(path: string): StudioDiagnostic {
  return {
    code: 'UnknownWidgetDataSource',
    severity: 'error',
    message: "The data source 'MissingSource' was not found. No loaded data source has that name.",
    file: 'pages/dashboard.json',
    path,
    resourceId: pageId,
  }
}

function LocationProbe() {
  const location = useLocation()
  return <span data-testid="location">{`${location.pathname}${location.search}`}</span>
}

function renderEditor(provided: StudioNames = names) {
  render(
    <MemoryRouter initialEntries={['/studio?app=Shop&resource=dashboard']}>
      <ThemeModeProvider>
        <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
          <StudioNamesContext.Provider value={provided}>
            <StudioEditor
              application="Shop"
              item={item}
              resources={[item]}
              path={null}
              diagnosticPaths={[]}
              onOpen={vi.fn()}
              onSaved={vi.fn()}
              onTextsWritten={vi.fn()}
              onDeleted={vi.fn()}
            />
          </StudioNamesContext.Provider>
        </TextProvider>
      </ThemeModeProvider>
      <LocationProbe />
    </MemoryRouter>,
  )
}

const slow = { timeout: 5000 }

async function openTab(name: string) {
  await userEvent.click(await screen.findByRole('tab', { name }, slow))
}

function appRequests(fetchMock: ReturnType<typeof vi.fn>) {
  return fetchMock.mock.calls.filter(([input]) => String(input).startsWith('/api/apps/'))
}

beforeAll(() => loadKindSchema('page'))

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('a module page', () => {
  it('reads the metadata with a GET from the module route, posts nothing and draws the answer', async () => {
    const draft = content([{ type: 'kpi' }])
    const moduleRoute = `${folder}/modules/checklists/resources/${pageId}/page-metadata`
    const fetchMock = vi.fn(async (input: string, init?: RequestInit) =>
      input === moduleRoute && init?.method === undefined
        ? respond(metadataOf(draft), [])
        : new Response(null, { status: 404 }),
    )
    vi.stubGlobal('fetch', fetchMock)

    render(
      <MemoryRouter>
        <ThemeModeProvider>
          <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
            <StudioNamesContext.Provider value={names}>
              <StudioPageCanvas
                application="Shop"
                module="checklists"
                resourceId={pageId}
                draft={draft}
                selected=""
                onSelect={vi.fn()}
              />
            </StudioNamesContext.Provider>
          </TextProvider>
        </ThemeModeProvider>
      </MemoryRouter>,
    )

    expect(await screen.findByTestId('kpi-tile', {}, slow)).toBeInTheDocument()
    expect(fetchMock.mock.calls.map(([input]) => String(input))).toEqual([moduleRoute])
    expect(fetchMock.mock.calls.some(([, init]) => init?.method === 'POST')).toBe(false)
  })
})

describe('the Design tab of a page', () => {
  it('opens on the Design tab, then draws the posted draft, and draws again after an edit in the Form tab and the JSON tab', async () => {
    const { fetchMock, posts } = stub(content([{ type: 'kpi' }]))
    renderEditor()

    const canvas = await screen.findByTestId('studio-page-canvas', {}, slow)
    expect(screen.queryByTestId('studio-page-editor')).not.toBeInTheDocument()
    expect(await within(canvas).findByTestId('kpi-tile', {}, slow)).toBeInTheDocument()
    await waitFor(
      () => expect(within(canvas).getByTestId('kpi-value')).toHaveTextContent('Total amount: 1,250.50'),
      slow,
    )
    expect(within(canvas).getByRole('heading', { name: 'Dashboard' })).toBeInTheDocument()
    expect(posts).toEqual([content([{ type: 'kpi' }])])
    expect(within(canvas).queryByTestId('chart-widget')).not.toBeInTheDocument()

    await openTab('Form')
    await chooseOption(
      'chart',
      await screen
        .findByRole('combobox', { name: 'Add widget' })
        .then((box) => box.closest<HTMLElement>('.ant-select')!),
    )
    await openTab('Design')
    expect(await screen.findByTestId('chart-widget', {}, slow)).toHaveTextContent('Requests by status')
    expect(posts.at(-1)!.widgets.map((widget) => widget.type)).toEqual(['kpi', 'chart'])

    await openTab('JSON')
    const json = screen.getByRole('textbox', { name: 'JSON' })
    fireEvent.change(json, { target: { value: JSON.stringify(content([{ type: 'kpi' }])) } })
    await openTab('Design')
    await waitFor(() => expect(posts.at(-1)!.widgets).toEqual([{ type: 'kpi' }]), slow)
    await waitFor(() => expect(screen.queryByTestId('chart-widget')).not.toBeInTheDocument(), slow)
    expect(screen.getByTestId('kpi-tile')).toBeInTheDocument()

    expect(appRequests(fetchMock)).toEqual([])
  })

  it('draws a navigate action as a button, not a link, and leaves the location where it is when it is clicked', async () => {
    const { fetchMock } = stub(content([{ type: 'kpi', navigate: { page: 'Tickets' } }]))
    renderEditor()

    await openTab('Form')
    await screen.findByTestId('studio-page-editor', {}, slow)
    await openTab('Design')
    const canvas = await screen.findByTestId('studio-page-canvas')
    const button = await within(canvas).findByRole('button', { name: 'Show tickets' }, slow)
    expect(within(canvas).queryByRole('link')).not.toBeInTheDocument()
    const before = screen.getByTestId('location').textContent
    expect(before).toBe('/studio?app=Shop&resource=dashboard')

    await userEvent.click(button)

    expect(screen.getByTestId('location')).toHaveTextContent(before!)
    expect(screen.getByTestId('studio-page-canvas')).toBeInTheDocument()
    expect(appRequests(fetchMock)).toEqual([])
  })

  it('draws a widget that does not compile as a placeholder, even one whose type is columns, and draws the rest', async () => {
    stub(content([{ type: 'kpi' }, { type: 'columns' }]))
    renderEditor()

    await openTab('Form')
    await screen.findByTestId('studio-page-editor', {}, slow)
    await openTab('Design')
    const box = await screen.findByTestId('studio-canvas-placeholder', {}, slow)
    expect(box).toHaveTextContent('This widget does not compile: columns')
    expect(box).toHaveAttribute('data-path', '/widgets/1')
    expect(screen.getByTestId('kpi-tile')).toBeInTheDocument()
  })

  it('shows a notice instead of widgets when the route cannot draw the page', async () => {
    stub(
      content([{ type: 'kpi' }]),
      () => new Response(JSON.stringify({ page: null, diagnostics: [] }), { status: 200 }),
    )
    renderEditor()

    await openTab('Form')
    await screen.findByTestId('studio-page-editor', {}, slow)
    await openTab('Design')
    expect(await screen.findByTestId('studio-canvas-cannot-draw', {}, slow)).toHaveTextContent(
      'The page cannot be drawn until its errors are fixed.',
    )
    expect(screen.queryByTestId('kpi-tile')).not.toBeInTheDocument()
  })

  it('shows a notice when the route fails', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => undefined)
    stub(content([{ type: 'kpi' }]), () => new Response(null, { status: 500 }))
    renderEditor()

    await openTab('Form')
    await screen.findByTestId('studio-page-editor', {}, slow)
    await openTab('Design')
    expect(await screen.findByTestId('studio-canvas-failed', {}, slow)).toHaveTextContent(
      'The page could not be drawn.',
    )
  })

  it('marks the widget that a diagnostic names, and no other', async () => {
    stub(content([{ type: 'kpi' }, { type: 'chart' }]), (draft) =>
      respond(metadataOf(draft), [diagnosticAt('/widgets/1/dataSource')]),
    )
    renderEditor()

    await openTab('Form')
    await screen.findByTestId('studio-page-editor', {}, slow)
    await openTab('Design')
    const canvas = await screen.findByTestId('studio-page-canvas', {}, slow)
    await within(canvas).findByTestId('chart-widget', {}, slow)
    const marked = within(canvas).getByRole('group', { name: /has diagnostics$/ })
    expect(marked).toHaveAttribute('data-path', '/widgets/1')
    expect(marked).toHaveAttribute('data-marked', 'true')
    const unmarked = within(canvas)
      .getAllByTestId('studio-canvas-widget')
      .find((frame) => frame.getAttribute('data-path') === '/widgets/0')!
    expect(unmarked).not.toHaveAttribute('data-marked')
    expect(unmarked).not.toHaveAccessibleName(/has diagnostics/)
  })

  it('lists the diagnostics of a placeholder and marks it, and still draws the rest of the page', async () => {
    const placeholder = { type: 'chart', path: '/widgets/1', placeholder: true } as unknown as PageWidgetMetadata
    stub(content([{ type: 'kpi' }, { type: 'chart' }]), (draft) => {
      const page = metadataOf(draft)
      return respond({ ...page, widgets: [page.widgets[0], placeholder] }, [diagnosticAt('/widgets/1/dataSource')])
    })
    renderEditor()

    await openTab('Form')
    await screen.findByTestId('studio-page-editor', {}, slow)
    await openTab('Design')
    const canvas = await screen.findByTestId('studio-page-canvas', {}, slow)
    const box = await within(canvas).findByTestId('studio-canvas-placeholder', {}, slow)
    expect(box).toHaveTextContent('This widget does not compile: chart')
    expect(within(box).getByTestId('studio-canvas-placeholder-diagnostic')).toHaveTextContent(
      "UnknownWidgetDataSource: The data source 'MissingSource' was not found. No loaded data source has that name.",
    )
    expect(within(canvas).getByRole('group', { name: /has diagnostics$/ })).toContainElement(box)
    expect(within(canvas).getByTestId('kpi-tile')).toBeInTheDocument()
  })

  it('draws the page 375 px wide at phone width, with every column full width', async () => {
    const columns: PageWidgetMetadata = {
      type: 'columns',
      path: '/widgets/0',
      columns: [
        { width: 4, widgets: [{ ...kpiWidget, path: '/widgets/0/columns/0/widgets/0' }] },
        { width: 8, widgets: [{ ...chartWidget('bar'), path: '/widgets/0/columns/1/widgets/0' }] },
      ],
    }
    stub(content([{ type: 'columns' }]), (draft) => respond({ ...metadataOf(draft), widgets: [columns] }, []))
    renderEditor()

    await openTab('Form')
    await screen.findByTestId('studio-page-editor', {}, slow)
    await openTab('Design')
    const canvas = await screen.findByTestId('studio-page-canvas', {}, slow)
    const cells = await within(canvas).findAllByTestId('column', {}, slow)
    expect(cells[0]).toHaveClass('ant-col-8')
    expect(cells[1]).toHaveClass('ant-col-16')

    await userEvent.click(screen.getByText('Phone'))

    expect(within(canvas).getByTestId('studio-canvas-frame')).toHaveStyle({ width: '375px' })
    for (const cell of cells) {
      expect(cell).toHaveClass('ant-col-24')
    }
  })

  describe('selection', () => {
    async function openDesign(widgets: Record<string, unknown>[]) {
      const stubbed = stub(content(widgets))
      renderEditor()
      await openTab('Form')
      await screen.findByTestId('studio-page-editor', {}, slow)
      await openTab('Design')
      const design = await screen.findByTestId('studio-page-design', {}, slow)
      await within(design).findByTestId('kpi-tile', {}, slow)
      return { ...stubbed, design }
    }

    function frame(design: HTMLElement, path: string) {
      return within(design)
        .getAllByTestId('studio-canvas-widget')
        .find((element) => element.getAttribute('data-path') === path)!
    }

    it('selects the widget that is clicked on the canvas in the tree and opens its panel', async () => {
      const { design } = await openDesign([{ type: 'kpi' }, { type: 'chart' }])
      await screen.findByTestId('chart-widget', {}, slow)
      expect(within(design).getByTestId('studio-widget-/widgets/0')).toHaveAttribute('aria-current', 'true')
      expect(within(design).queryByTestId('studio-field-/widgets/1')).not.toBeInTheDocument()

      await userEvent.click(within(design).getByTestId('chart-widget'))

      expect(within(design).getByTestId('studio-widget-/widgets/1')).toHaveAttribute('aria-current', 'true')
      expect(within(design).getByTestId('studio-widget-/widgets/0')).toHaveAttribute('aria-current', 'false')
      expect(within(design).getByTestId('studio-field-/widgets/1')).toBeInTheDocument()
      expect(frame(design, '/widgets/1')).toHaveAttribute('aria-current', 'true')
      expect(screen.getByTestId('location')).toHaveTextContent('/studio?app=Shop&resource=dashboard')
    })

    it('outlines the canvas widget that is picked in the tree, and no other', async () => {
      const { design } = await openDesign([{ type: 'kpi' }, { type: 'chart' }])
      await screen.findByTestId('chart-widget', {}, slow)

      await userEvent.click(within(design).getByTestId('studio-widget-/widgets/1'))

      expect(frame(design, '/widgets/1')).toHaveAttribute('aria-current', 'true')
      expect(frame(design, '/widgets/0')).not.toHaveAttribute('aria-current')
    })

    it('draws the canvas again after the widget type changes in the panel of the Design tab', async () => {
      const { design, posts } = await openDesign([{ type: 'kpi' }])
      expect(within(design).queryByTestId('chart-widget')).not.toBeInTheDocument()

      await chooseOption(
        'chart',
        within(design).getByRole('combobox', { name: 'Widget type' }).closest<HTMLElement>('.ant-select')!,
      )

      expect(await screen.findByTestId('chart-widget', {}, slow)).toBeInTheDocument()
      expect(posts.at(-1)!.widgets[0].type).toBe('chart')
    })

    it('moves between the widgets with Tab and selects one with Enter', async () => {
      const { design } = await openDesign([{ type: 'kpi' }, { type: 'chart' }])
      await screen.findByTestId('chart-widget', {}, slow)

      frame(design, '/widgets/0').focus()
      await userEvent.tab()
      expect(frame(design, '/widgets/1')).toHaveFocus()
      await userEvent.keyboard('{Enter}')

      expect(within(design).getByTestId('studio-widget-/widgets/1')).toHaveAttribute('aria-current', 'true')
      expect(frame(design, '/widgets/1')).toHaveAttribute('aria-current', 'true')
      expect(frame(design, '/widgets/0')).not.toHaveAttribute('aria-current')
    })

    it('names a widget by its type and summary', async () => {
      const { design } = await openDesign([{ type: 'kpi', dataSource: 'RequestTotals' }])

      expect(within(design).getByRole('group', { name: 'kpi · RequestTotals' })).toHaveAttribute(
        'data-path',
        '/widgets/0',
      )
    })

    it('keeps the selection when the author goes to the Form tab and back', async () => {
      const { design } = await openDesign([{ type: 'kpi' }, { type: 'chart' }])
      await screen.findByTestId('chart-widget', {}, slow)
      await userEvent.click(within(design).getByTestId('chart-widget'))

      await openTab('Form')
      await openTab('Design')

      const again = await screen.findByTestId('studio-page-design', {}, slow)
      await within(again).findByTestId('chart-widget', {}, slow)
      expect(frame(again, '/widgets/1')).toHaveAttribute('aria-current', 'true')
    })
  })

  describe('redraws', () => {
    async function openDesign(
      widgets: Record<string, unknown>[],
      answer?: (draft: Draft) => Response | Promise<Response>,
    ) {
      const stubbed = stub(content(widgets), answer)
      renderEditor()
      await openTab('Form')
      await screen.findByTestId('studio-page-editor', {}, slow)
      await openTab('Design')
      const design = await screen.findByTestId('studio-page-design', {}, slow)
      await within(design).findByTestId('chart-widget', {}, slow)
      return { ...stubbed, design }
    }

    it('is busy from an edit until it draws the answer to that draft', async () => {
      vi.useFakeTimers({ shouldAdvanceTime: true })
      try {
        const gate = gatedAnswers()
        const { design, posts } = await openDesign([{ type: 'kpi' }, { type: 'chart' }], gate.answer)
        const canvas = within(design).getByTestId('studio-page-canvas')
        expect(canvas).toHaveAttribute('aria-busy', 'false')

        gate.hold()
        await userEvent.click(within(design).getByTestId('chart-widget'))
        const before = posts.length
        await chooseOption('pie', within(design).getByTestId('studio-field-/widgets/1/kind'))
        await waitFor(() => expect(posts).toHaveLength(before + 1))
        expect(canvas).toHaveAttribute('aria-busy', 'true')

        gate.release()
        await waitFor(() => expect(canvas).toHaveAttribute('aria-busy', 'false'))
      } finally {
        vi.useRealTimers()
      }
    })

    it('stays busy when an answer for an earlier draft arrives after a newer edit', async () => {
      vi.useFakeTimers({ shouldAdvanceTime: true })
      try {
        const gate = gatedAnswers()
        const { design, posts } = await openDesign([{ type: 'kpi' }, { type: 'chart' }], gate.answer)
        const canvas = within(design).getByTestId('studio-page-canvas')

        gate.hold()
        await userEvent.click(within(design).getByTestId('chart-widget'))
        const before = posts.length
        await chooseOption('pie', within(design).getByTestId('studio-field-/widgets/1/kind'))
        await waitFor(() => expect(posts).toHaveLength(before + 1))
        await chooseOption('line', within(design).getByTestId('studio-field-/widgets/1/kind'))
        await waitFor(() => expect(posts).toHaveLength(before + 2))

        gate.release()
        await act(() => vi.advanceTimersByTimeAsync(100))
        expect(canvas).toHaveAttribute('aria-busy', 'true')

        gate.release()
        await waitFor(() => expect(canvas).toHaveAttribute('aria-busy', 'false'))
      } finally {
        vi.useRealTimers()
      }
    })

    it('posts one edit 300 ms after it and draws the chart once, from the answer', async () => {
      vi.useFakeTimers({ shouldAdvanceTime: true })
      try {
        const { design, posts } = await openDesign([{ type: 'kpi' }, { type: 'chart' }])
        await userEvent.click(within(design).getByTestId('chart-widget'))
        const before = posts.length
        chartDraws.count = 0

        await chooseOption('pie', within(design).getByTestId('studio-field-/widgets/1/kind'))
        expect(posts).toHaveLength(before)
        await act(() => vi.advanceTimersByTimeAsync(200))
        expect(posts).toHaveLength(before)
        await act(() => vi.advanceTimersByTimeAsync(100))
        await waitFor(() => expect(posts).toHaveLength(before + 1))
        expect(posts.at(-1)!.widgets[1]).toMatchObject({ type: 'chart', kind: 'pie' })

        await waitFor(() => expect(chartDraws.count).toBe(1))
        await act(() => vi.advanceTimersByTimeAsync(1000))
        expect(chartDraws.count).toBe(1)
      } finally {
        vi.useRealTimers()
      }
    })
  })

  describe('actions', () => {
    const navigateOpen = (labelKey: string) => ({ ...showTickets, labelKey, record: 'id' })
    const deleteRow = { type: 'delete', labelKey: 'request.delete', recordsPath: tableWidget.entity!.recordsPath }

    /** A table, a form, a detail and a KPI, each with actions. The delete row action is hidden on placeholder data. */
    const draftWidgets = [
      {
        type: 'table',
        rowActions: [
          { type: 'navigate', page: 'Tickets' },
          { type: 'delete', visibleWhen: 'false' },
        ],
        toolbarActions: [{ type: 'navigate', page: 'Tickets' }],
      },
      { type: 'form', actions: [{ type: 'startProcess' }, { type: 'navigate', page: 'Tickets' }] },
      { type: 'detail', record: 'request', actions: [{ type: 'startProcess' }] },
      { type: 'kpi', navigate: { type: 'navigate', page: 'Tickets' } },
    ]

    function answer(draft: Draft) {
      const widgets: PageWidgetMetadata[] = [
        {
          ...tableWidget,
          path: '/widgets/0',
          rowActions: [navigateOpen('request.open'), { ...deleteRow, type: 'delete', visibleWhen: 'false' }],
          toolbarActions: [showTickets],
        } as PageWidgetMetadata,
        {
          ...formWidget,
          path: '/widgets/1',
          actions: [formWidget.actions[0], showTickets],
        } as PageWidgetMetadata,
        { ...detailWidget, path: '/widgets/2' } as PageWidgetMetadata,
        { ...kpiWidget, path: '/widgets/3', kpi: { ...kpiWidget.kpi!, navigate: showTickets } },
      ].slice(0, draft.widgets.length)
      const request: PageParameter = { name: 'request', type: 'reference', required: true, values: null, target: null }
      return respond({ name: 'Dashboard', titleKey: 'dashboard.title', parameters: [request], widgets }, [])
    }

    async function openActions() {
      const stubbed = stub(content(draftWidgets), answer)
      renderEditor()
      await openTab('Form')
      await screen.findByTestId('studio-page-editor', {}, slow)
      await openTab('Design')
      const design = await screen.findByTestId('studio-page-design', {}, slow)
      await within(design).findByTestId('kpi-tile', {}, slow)
      await waitFor(() => expect(design.querySelector('[data-action="rowActions/0"]')).not.toBeNull(), slow)
      await waitFor(() => expect(design.querySelector('[data-action="actions/0"]')).not.toBeNull(), slow)
      return { ...stubbed, design }
    }

    /** The button of an action inside the widget at `path`. */
    function button(design: HTMLElement, path: string, key: string) {
      const frame = within(design)
        .getAllByTestId('studio-canvas-widget')
        .find((element) => element.getAttribute('data-path') === path)!
      return frame.querySelector<HTMLElement>(`[data-action="${key}"]`)!
    }

    it.each([
      ['a row action', '/widgets/0', 'rowActions/1', 'studio-field-/widgets/0/rowActions', 'studio-action-1'],
      [
        'a toolbar action',
        '/widgets/0',
        'toolbarActions/0',
        'studio-field-/widgets/0/toolbarActions',
        'studio-action-0',
      ],
      ['a form action', '/widgets/1', 'actions/1', 'studio-field-/widgets/1/actions', 'studio-action-1'],
      ['a detail action', '/widgets/2', 'actions/0', 'studio-field-/widgets/2/actions', 'studio-action-0'],
      ['the navigate action of a KPI', '/widgets/3', 'navigate', 'studio-field-/widgets/3/navigate', undefined],
    ])('selects %s when its button is clicked', async (_name, widget, key, listId, card) => {
      const { design } = await openActions()

      await userEvent.click(button(design, widget, key))

      const list = within(design).getByTestId(listId)
      expect(card === undefined ? list : within(list).getByTestId(card)).toHaveAttribute('aria-current', 'true')
      expect(button(design, widget, key)).toHaveAttribute('aria-current', 'true')
      expect(within(design).getByTestId(`studio-widget-${widget}`)).toHaveAttribute('aria-current', 'true')
      // The widget itself is not the selection, so its frame is not outlined.
      expect(
        within(design)
          .getAllByTestId('studio-canvas-widget')
          .find((element) => element.getAttribute('data-path') === widget),
      ).not.toHaveAttribute('aria-current')
      // A table draws the action on every row, and only the selected action's buttons are marked.
      const marked = [...design.querySelectorAll('[data-action][aria-current="true"]')]
      expect(marked.length).toBeGreaterThan(0)
      expect(marked.map((element) => element.getAttribute('data-action'))).toEqual(marked.map(() => key))
    })

    it('selects the first navigate row action when the identifying link is clicked, and does not navigate', async () => {
      const { design } = await openActions()
      const before = screen.getByTestId('location').textContent
      const link = design.querySelector<HTMLElement>('[data-action="rowActions/0"]')!

      expect(link).toHaveAttribute('role', 'link')
      expect(link).not.toHaveAttribute('href')
      await userEvent.click(link)

      const list = within(design).getByTestId('studio-field-/widgets/0/rowActions')
      expect(within(list).getByTestId('studio-action-0')).toHaveAttribute('aria-current', 'true')
      expect(within(list).getByTestId('studio-action-1')).not.toHaveAttribute('aria-current')
      expect(link).toHaveAttribute('aria-current', 'true')
      expect(screen.getByTestId('location')).toHaveTextContent(before!)
    })

    it('selects an action with Enter on its button', async () => {
      const { design } = await openActions()

      button(design, '/widgets/0', 'rowActions/1').focus()
      await userEvent.keyboard('{Enter}')

      expect(
        within(within(design).getByTestId('studio-field-/widgets/0/rowActions')).getByTestId('studio-action-1'),
      ).toHaveAttribute('aria-current', 'true')
    })

    it('selects the widget again when its frame is clicked after an action', async () => {
      const { design } = await openActions()
      await userEvent.click(button(design, '/widgets/0', 'rowActions/1'))

      await userEvent.click(within(design).getByTestId('kpi-tile'))

      expect(design.querySelector('[data-action][aria-current="true"]')).toBeNull()
      expect(within(design).getByTestId('studio-widget-/widgets/3')).toHaveAttribute('aria-current', 'true')
    })

    it('draws an action that the site would hide, a row action whose visibleWhen fails and a form action with no record', async () => {
      const { design } = await openActions()

      const rows = design.querySelectorAll('[data-action="rowActions/1"]')
      expect(rows).toHaveLength(3)
      expect(rows[0]).toHaveAccessibleName('request.delete')
      expect(button(design, '/widgets/1', 'actions/0')).toHaveTextContent('request.submit')
      expect(button(design, '/widgets/1', 'actions/1')).toHaveTextContent('Show tickets')
    })

    it('marks the card of an added action and selects it, and selects the widget when the action is removed', async () => {
      const { design } = await openActions()
      const list = within(design).getByTestId('studio-field-/widgets/0/rowActions')

      await userEvent.click(within(list).getByRole('button', { name: 'Add navigate action' }))

      expect(within(list).getByTestId('studio-action-2')).toHaveAttribute('aria-current', 'true')
      expect(within(list).getByTestId('studio-action-1')).not.toHaveAttribute('aria-current')

      await userEvent.click(
        within(within(list).getByTestId('studio-action-2')).getByRole('button', { name: 'Remove action' }),
      )

      expect(within(list).queryByTestId('studio-action-2')).not.toBeInTheDocument()
      expect(within(list).getByTestId('studio-action-1')).not.toHaveAttribute('aria-current')
      expect(within(design).getByTestId('studio-widget-/widgets/0')).toHaveAttribute('aria-current', 'true')
    })

    it('runs nothing when an action button is clicked: no request, no navigation and no confirmation', async () => {
      const { fetchMock, design } = await openActions()
      const before = screen.getByTestId('location').textContent
      const clicked = [
        button(design, '/widgets/0', 'rowActions/0'),
        button(design, '/widgets/0', 'rowActions/1'),
        button(design, '/widgets/0', 'toolbarActions/0'),
        button(design, '/widgets/1', 'actions/0'),
        button(design, '/widgets/1', 'actions/1'),
        button(design, '/widgets/2', 'actions/0'),
        button(design, '/widgets/3', 'navigate'),
      ]

      for (const element of clicked) {
        await userEvent.click(element)
      }

      expect(screen.getByTestId('location')).toHaveTextContent(before!)
      expect(document.querySelector('.ant-modal')).toBeNull()
      expect(screen.queryByTestId('table-action-error')).not.toBeInTheDocument()
      expect(appRequests(fetchMock)).toEqual([])
    })

    it('inserts from the palette after the widget of a selected row action, not into its action list', async () => {
      const { design, posts } = await openActions()
      await userEvent.click(button(design, '/widgets/0', 'rowActions/1'))

      await userEvent.click(within(within(design).getByTestId('studio-palette')).getByTestId('studio-palette-kpi'))

      await waitFor(() => expect(posts.at(-1)!.widgets).toHaveLength(5), slow)
      const saved = posts.at(-1)!.widgets
      expect(saved[0].rowActions).toHaveLength(2)
      expect(saved[1]).toEqual(newWidget('kpi'))
      expect(within(design).getByTestId('studio-widget-/widgets/1')).toHaveAttribute('aria-current', 'true')
    })

    it('inserts from the palette after a KPI whose navigate action is selected', async () => {
      const { design, posts } = await openActions()
      await userEvent.click(button(design, '/widgets/3', 'navigate'))

      await userEvent.click(within(within(design).getByTestId('studio-palette')).getByTestId('studio-palette-kpi'))

      await waitFor(() => expect(posts.at(-1)!.widgets).toHaveLength(5), slow)
      expect(posts.at(-1)!.widgets[4]).toEqual(newWidget('kpi'))
      expect(within(design).getByTestId('studio-widget-/widgets/4')).toHaveAttribute('aria-current', 'true')
    })
  })

  describe('palette', () => {
    async function openDesign(widgets: Record<string, unknown>[]) {
      const stubbed = stub(content(widgets))
      renderEditor()
      await openTab('Form')
      await screen.findByTestId('studio-page-editor', {}, slow)
      await openTab('Design')
      const design = await screen.findByTestId('studio-design-panel', {}, slow)
      await screen.findByTestId('studio-page-canvas', {}, slow)
      return { ...stubbed, design, palette: within(within(design).getByTestId('studio-palette')) }
    }

    const section = (widgets: Record<string, unknown>[]) => ({ type: 'section', title: { textKey: '' }, widgets })

    it('inserts the type after the selected leaf on Enter, selects it and names the place', async () => {
      const { design, palette, posts } = await openDesign([{ type: 'kpi' }, { type: 'chart' }])

      palette.getByTestId('studio-palette-kpi').focus()
      await userEvent.keyboard('{Enter}')

      await waitFor(() => expect(posts.at(-1)!.widgets).toHaveLength(3), slow)
      expect(posts.at(-1)!.widgets[1]).toEqual(newWidget('kpi'))
      expect(within(design).getByTestId('studio-widget-/widgets/1')).toHaveAttribute('aria-current', 'true')
      const announcement = palette.getByTestId('studio-palette-announcement')
      expect(announcement).toHaveTextContent('Page')
      expect(announcement).toHaveTextContent('position 2')
    })

    it('undoes a palette insert with Ctrl+Z and brings it back with Ctrl+Shift+Z', async () => {
      const { design, palette } = await openDesign([{ type: 'kpi' }, { type: 'chart' }])
      palette.getByTestId('studio-palette-kpi').focus()
      await userEvent.keyboard('{Enter}')
      await waitFor(() => expect(within(design).getByTestId('studio-widget-/widgets/2')).toBeInTheDocument(), slow)

      await userEvent.keyboard('{Control>}z{/Control}')

      await waitFor(
        () => expect(within(design).queryByTestId('studio-widget-/widgets/2')).not.toBeInTheDocument(),
        slow,
      )
      await userEvent.keyboard('{Control>}{Shift>}z{/Shift}{/Control}')
      await waitFor(() => expect(within(design).getByTestId('studio-widget-/widgets/2')).toBeInTheDocument(), slow)
    })

    it('inserts the type at the end of the selected section and selects it', async () => {
      const { design, palette, posts } = await openDesign([{ type: 'kpi' }, section([{ type: 'chart' }])])
      await userEvent.click(within(design).getByTestId('studio-widget-/widgets/1'))

      await userEvent.click(palette.getByTestId('studio-palette-kpi'))

      await waitFor(
        () => expect(posts.at(-1)!.widgets[1]).toMatchObject({ widgets: [{ type: 'chart' }, newWidget('kpi')] }),
        slow,
      )
      expect(within(design).getByTestId('studio-widget-/widgets/1/widgets/1')).toHaveAttribute('aria-current', 'true')
      expect(palette.getByTestId('studio-palette-announcement')).toHaveTextContent('Page › section 2 · position 2')
    })

    it('disables table with its reason when the page holds a table inside a container', async () => {
      const { palette } = await openDesign([section([{ type: 'table' }])])

      const button = palette.getByTestId('studio-palette-table')
      expect(button).toBeDisabled()
      expect(button).toHaveAccessibleDescription('The page already holds a table. A page holds at most one.')
      expect(palette.getByTestId('studio-palette-kpi')).toBeEnabled()
    })

    it('disables the containers with the depth reason in a list on level 4 and keeps other types', async () => {
      const { design, palette } = await openDesign([section([section([section([{ type: 'kpi' }])])])])
      await userEvent.click(within(design).getByTestId('studio-widget-/widgets/0/widgets/0/widgets/0/widgets/0'))

      for (const type of ['section', 'columns', 'tabs']) {
        const button = palette.getByTestId(`studio-palette-${type}`)
        expect(button).toBeDisabled()
        expect(button).toHaveAccessibleDescription('A container here would hold a list past level 4.')
      }
      expect(palette.getByTestId('studio-palette-kpi')).toBeEnabled()
    })
  })

  describe('dropping', () => {
    const section = (widgets: Record<string, unknown>[]) => ({ type: 'section', title: { textKey: '' }, widgets })

    async function openDesign(widgets: Record<string, unknown>[]) {
      const stubbed = stub(content(widgets))
      renderEditor()
      await openTab('Form')
      await screen.findByTestId('studio-page-editor', {}, slow)
      await openTab('Design')
      const design = await screen.findByTestId('studio-design-panel', {}, slow)
      await screen.findByTestId('studio-page-canvas', {}, slow)
      return { ...stubbed, design, palette: within(within(design).getByTestId('studio-palette')) }
    }

    /** Drops a palette type on a target, as the library reports the end of a drag: the pointer at `clientY` in a box 100 high. */
    function dropOn(type: string, data: { kind: 'widget' | 'list'; pointer: string }, clientY = 10) {
      act(() =>
        dnd.props!.onDragEnd!({
          active: { id: `palette:${type}`, data: { current: { type } } },
          over: { id: `${data.kind}:${data.pointer}`, data: { current: data }, rect: { top: 0, height: 100 } },
          delta: { x: 0, y: 0 },
          activatorEvent: new MouseEvent('pointerdown', { clientY }),
        } as never),
      )
    }

    it('inserts nothing when the depth limit forbids the drop, and says why', async () => {
      const nest = [section([section([section([{ type: 'kpi' }])])])]
      const { design, palette } = await openDesign(nest)

      dropOn('section', { kind: 'list', pointer: '/widgets/0/widgets/0/widgets/0/widgets' })

      expect(palette.getByTestId('studio-palette-announcement')).toHaveTextContent(
        'A container here would hold a list past level 4.',
      )
      expect(
        within(design).queryByTestId('studio-widget-/widgets/0/widgets/0/widgets/0/widgets/1'),
      ).not.toBeInTheDocument()
      await openTab('JSON')
      const json = screen.getByRole<HTMLTextAreaElement>('textbox', { name: 'JSON' })
      expect(JSON.parse(json.value)).toEqual(content(nest))
    })

    it('inserts after a widget dropped on its lower half, selects the new widget and names the place', async () => {
      const { design, palette, posts } = await openDesign([{ type: 'kpi' }, { type: 'chart' }])

      dropOn('detail', { kind: 'widget', pointer: '/widgets/0' }, 90)

      await waitFor(() => expect(posts.at(-1)!.widgets).toHaveLength(3), slow)
      expect(posts.at(-1)!.widgets[1]).toEqual(newWidget('detail'))
      expect(within(design).getByTestId('studio-widget-/widgets/1')).toHaveAttribute('aria-current', 'true')
      const announcement = palette.getByTestId('studio-palette-announcement')
      expect(announcement).toHaveTextContent('Inserted detail')
      expect(announcement).toHaveTextContent('Page')
      expect(announcement).toHaveTextContent('position 2')
    })

    it('inserts before a widget dropped on its upper half', async () => {
      const { posts } = await openDesign([{ type: 'kpi' }, { type: 'chart' }])

      dropOn('detail', { kind: 'widget', pointer: '/widgets/1' }, 10)

      await waitFor(
        () => expect(posts.at(-1)!.widgets.map((widget) => widget.type)).toEqual(['kpi', 'detail', 'chart']),
        slow,
      )
    })

    it('appends to the list of a section it is dropped on', async () => {
      const { design, palette, posts } = await openDesign([{ type: 'kpi' }, section([{ type: 'chart' }])])
      // The stub draws the section as a placeholder, which still has a drop area for its list.
      await screen.findByTestId('studio-canvas-placeholder', {}, slow)
      const lists = screen.getAllByTestId('studio-canvas-list').map((list) => list.getAttribute('data-list'))
      expect(lists).toEqual(['/widgets', '/widgets/1/widgets'])

      dropOn('kpi', { kind: 'list', pointer: '/widgets/1/widgets' })

      await waitFor(
        () => expect(posts.at(-1)!.widgets[1]).toMatchObject({ widgets: [{ type: 'chart' }, newWidget('kpi')] }),
        slow,
      )
      expect(within(design).getByTestId('studio-widget-/widgets/1/widgets/1')).toHaveAttribute('aria-current', 'true')
      expect(palette.getByTestId('studio-palette-announcement')).toHaveTextContent('Page › section 2 · position 2')
    })

    it('does nothing when the draft has no such target', async () => {
      const { palette, posts } = await openDesign([{ type: 'kpi' }])
      await screen.findByTestId('kpi-tile', {}, slow)
      const before = posts.length

      dropOn('kpi', { kind: 'widget', pointer: '/widgets/4' })
      dropOn('kpi', { kind: 'list', pointer: '/widgets/0/widgets' })

      expect(palette.getByTestId('studio-palette-announcement')).toBeEmptyDOMElement()
      await new Promise((resolve) => setTimeout(resolve, 500))
      expect(posts).toHaveLength(before)
    })
  })

  describe('data panel', () => {
    const bound = {
      ...names,
      entities: [{ id: 'e1', name: 'Ticket', module: null, fields: [] }],
      dataSources: [
        {
          id: 'd1',
          name: 'ByStatus',
          module: null,
          parameters: [],
          columns: [
            { name: 'status', type: 'text' },
            { name: 'count', type: 'integer' },
          ],
          groupBy: ['status'],
        },
        {
          id: 'd2',
          name: 'Totals',
          module: null,
          parameters: [],
          columns: [{ name: 'open', type: 'integer' }],
          groupBy: [],
        },
      ],
    } as unknown as StudioNames

    async function openDesign(widgets: Record<string, unknown>[]) {
      const stubbed = stub(content(widgets))
      renderEditor(bound)
      await openTab('Form')
      await screen.findByTestId('studio-page-editor', {}, slow)
      await openTab('Design')
      const design = await screen.findByTestId('studio-design-panel', {}, slow)
      await screen.findByTestId('studio-page-canvas', {}, slow)
      return { ...stubbed, design, data: within(within(design).getByTestId('studio-data-panel')) }
    }

    function dropBinding(binding: { kind: string; name: string }, pointer: string, clientY: number) {
      act(() =>
        dnd.props!.onDragEnd!({
          active: { id: `data:${binding.kind}:${binding.name}`, data: { current: { binding } } },
          over: {
            id: `widget:${pointer}`,
            data: { current: { kind: 'widget', pointer } },
            rect: { top: 0, height: 100 },
          },
          delta: { x: 0, y: 0 },
          activatorEvent: new MouseEvent('pointerdown', { clientY }),
        } as never),
      )
    }

    it('lists the entities and data sources of the names, each as a button', async () => {
      const { data } = await openDesign([{ type: 'kpi' }])

      for (const [kind, name] of [
        ['entity', 'Ticket'],
        ['dataSource', 'ByStatus'],
        ['dataSource', 'Totals'],
      ]) {
        expect(data.getByTestId(`studio-data-${kind}-${name}`)).toHaveRole('button')
      }
      expect(data.getByText('Entities')).toBeInTheDocument()
      expect(data.getByText('Data sources')).toBeInTheDocument()
    })

    it('opens the choice on a drop, and inserts the bound chart after the widget, selected', async () => {
      const { design, posts } = await openDesign([{ type: 'kpi' }, { type: 'detail' }])

      dropBinding({ kind: 'dataSource', name: 'ByStatus' }, '/widgets/0', 90)
      const choice = await screen.findByTestId('studio-binding-choice')
      expect(within(choice).getByTestId('studio-binding-table')).toBeEnabled()
      expect(within(choice).queryByTestId('studio-binding-kpi')).not.toBeInTheDocument()
      await userEvent.click(within(choice).getByTestId('studio-binding-chart'))

      await waitFor(() => expect(posts.at(-1)!.widgets).toHaveLength(3), slow)
      expect(posts.at(-1)!.widgets[1]).toEqual({
        type: 'chart',
        dataSource: 'ByStatus',
        kind: 'bar',
        category: 'status',
        measures: [{ name: 'count', label: { textKey: '' } }],
        title: { textKey: '' },
      })
      expect(within(design).getByTestId('studio-widget-/widgets/1')).toHaveAttribute('aria-current', 'true')
      expect(screen.getByTestId('studio-palette-announcement')).toHaveTextContent('Inserted chart')
    })

    it('inserts nothing when the choice is cancelled', async () => {
      const { posts } = await openDesign([{ type: 'kpi' }])
      await screen.findByTestId('kpi-tile', {}, slow)
      const before = posts.length

      dropBinding({ kind: 'dataSource', name: 'Totals' }, '/widgets/0', 10)
      await userEvent.click(await screen.findByRole('button', { name: 'Cancel' }))

      await new Promise((resolve) => setTimeout(resolve, 300))
      expect(posts).toHaveLength(before)
      expect(screen.getByTestId('studio-palette-announcement')).toBeEmptyDOMElement()
    })

    it('opens the choice on Enter and inserts the bound widget after the selection', async () => {
      const { design, data, posts } = await openDesign([{ type: 'kpi' }, { type: 'detail' }])

      data.getByTestId('studio-data-entity-Ticket').focus()
      await userEvent.keyboard('{Enter}')
      await userEvent.click(await screen.findByTestId('studio-binding-table'))

      await waitFor(() => expect(posts.at(-1)!.widgets).toHaveLength(3), slow)
      expect(posts.at(-1)!.widgets[1]).toEqual({ type: 'table', entity: 'Ticket' })
      expect(within(design).getByTestId('studio-widget-/widgets/1')).toHaveAttribute('aria-current', 'true')
      const announcement = screen.getByTestId('studio-palette-announcement')
      expect(announcement).toHaveTextContent('Inserted table')
      expect(announcement).toHaveTextContent('position 2')
    })

    it('disables table with the one-table reason when the page already holds a table', async () => {
      const { data } = await openDesign([{ type: 'table' }])

      await userEvent.click(data.getByTestId('studio-data-entity-Ticket'))

      const choice = await screen.findByTestId('studio-binding-choice')
      const table = within(choice).getByTestId('studio-binding-table')
      expect(table).toBeDisabled()
      expect(table).toHaveAccessibleDescription('The page already holds a table. A page holds at most one.')
      expect(within(choice).getByTestId('studio-binding-form')).toBeEnabled()
    })
  })

  describe('moving', () => {
    const section = (widgets: Record<string, unknown>[]) => ({ type: 'section', title: { textKey: '' }, widgets })

    async function openDesign(widgets: Record<string, unknown>[]) {
      const stubbed = stub(content(widgets))
      renderEditor()
      await openTab('Form')
      await screen.findByTestId('studio-page-editor', {}, slow)
      await openTab('Design')
      const design = await screen.findByTestId('studio-design-panel', {}, slow)
      await screen.findByTestId('studio-page-canvas', {}, slow)
      return { ...stubbed, design, palette: within(within(design).getByTestId('studio-palette')) }
    }

    function frame(path: string) {
      return screen
        .getAllByTestId('studio-canvas-widget')
        .find((element) => element.getAttribute('data-path') === path)!
    }

    /** The event of a drag of the widget at `from` over a target, with the pointer at `clientY` in a box 100 high. */
    function dragEvent(from: string, data: { kind: 'widget' | 'list'; pointer: string }, clientY = 10) {
      return {
        active: { id: `move:${from}`, data: { current: { kind: 'move', pointer: from, label: 'x' } } },
        over: { id: `${data.kind}:${data.pointer}`, data: { current: data }, rect: { top: 0, height: 100 } },
        delta: { x: 0, y: 0 },
        activatorEvent: new MouseEvent('pointerdown', { clientY }),
      } as never
    }

    /** Drags the widget at `from` over a target, as the library reports a drag: the pointer at `clientY` in a box 100 high. */
    function drag(
      from: string,
      data: { kind: 'widget' | 'list'; pointer: string },
      { clientY = 10, drop = true }: { clientY?: number; drop?: boolean } = {},
    ) {
      const event = dragEvent(from, data, clientY)
      act(() => dnd.props!.onDragStart!(event))
      act(() => dnd.props!.onDragOver!(event))
      if (drop) {
        act(() => dnd.props!.onDragEnd!(event))
      }
    }

    async function expectDraftUnchanged(widgets: Record<string, unknown>[]) {
      await openTab('JSON')
      const json = screen.getByRole<HTMLTextAreaElement>('textbox', { name: 'JSON' })
      expect(JSON.parse(json.value)).toEqual(content(widgets))
    }

    it('moves a widget after another on its lower half and into an empty section, and selects it each time', async () => {
      const { design, palette, posts } = await openDesign([{ type: 'kpi' }, { type: 'chart' }, section([])])
      await screen.findByTestId('studio-canvas-placeholder', {}, slow)

      drag('/widgets/0', { kind: 'widget', pointer: '/widgets/1' }, { clientY: 90 })

      await waitFor(
        () => expect(posts.at(-1)!.widgets.map((widget) => widget.type)).toEqual(['chart', 'kpi', 'section']),
        slow,
      )
      await waitFor(() => expect(frame('/widgets/1')).toHaveAttribute('aria-current', 'true'), slow)
      expect(within(frame('/widgets/1')).getByTestId('kpi-tile')).toBeInTheDocument()
      const announcement = palette.getByTestId('studio-palette-announcement')
      expect(announcement).toHaveTextContent('Moved kpi')
      expect(announcement).toHaveTextContent('position 2')

      drag('/widgets/1', { kind: 'list', pointer: '/widgets/2/widgets' })

      await waitFor(() => expect(posts.at(-1)!.widgets).toEqual([{ type: 'chart' }, section([{ type: 'kpi' }])]), slow)
      expect(within(design).getByTestId('studio-widget-/widgets/1/widgets/0')).toHaveAttribute('aria-current', 'true')
      expect(announcement).toHaveTextContent('Moved kpi · Page › section 2 · position 1')
    })

    it('moves a widget before another on its upper half', async () => {
      const { posts } = await openDesign([{ type: 'kpi' }, { type: 'chart' }])

      drag('/widgets/1', { kind: 'widget', pointer: '/widgets/0' }, { clientY: 10 })

      await waitFor(() => expect(posts.at(-1)!.widgets.map((widget) => widget.type)).toEqual(['chart', 'kpi']), slow)
    })

    it('draws the chart no more for repeated drag-over events on the same target', async () => {
      await openDesign([{ type: 'kpi' }, { type: 'chart' }])
      await screen.findByTestId('chart-widget', {}, slow)
      const event = dragEvent('/widgets/0', { kind: 'widget', pointer: '/widgets/1' })
      act(() => dnd.props!.onDragStart!(event))
      act(() => dnd.props!.onDragOver!(event))

      chartDraws.count = 0
      for (let index = 0; index < 20; index++) {
        act(() => dnd.props!.onDragOver!(event))
      }
      expect(chartDraws.count).toBe(0)

      act(() => dnd.props!.onDragCancel!(event))
    })

    it('moves the selected widget with Alt+ArrowDown and Alt+ArrowUp, keeps it selected and focused, and says where it landed', async () => {
      const { design, palette, posts } = await openDesign([{ type: 'kpi' }, { type: 'chart' }])
      await screen.findByTestId('chart-widget', {}, slow)
      const announcement = palette.getByTestId('studio-palette-announcement')

      act(() => frame('/widgets/0').focus())
      await userEvent.keyboard('{Alt>}{ArrowDown}{/Alt}')

      await waitFor(() => expect(posts.at(-1)!.widgets.map((widget) => widget.type)).toEqual(['chart', 'kpi']), slow)
      expect(within(design).getByTestId('studio-widget-/widgets/1')).toHaveAttribute('aria-current', 'true')
      expect(announcement).toHaveTextContent('Moved kpi · Page · position 2')
      await waitFor(() => expect(frame('/widgets/1')).toHaveFocus(), slow)

      await userEvent.keyboard('{Alt>}{ArrowUp}{/Alt}')

      await waitFor(() => expect(posts.at(-1)!.widgets.map((widget) => widget.type)).toEqual(['kpi', 'chart']), slow)
      expect(within(design).getByTestId('studio-widget-/widgets/0')).toHaveAttribute('aria-current', 'true')
      expect(announcement).toHaveTextContent('Moved kpi · Page · position 1')
      await waitFor(() => expect(frame('/widgets/0')).toHaveFocus(), slow)
    })

    it('says the widget cannot move further at the end of the page and posts no new draft', async () => {
      const { design, palette, posts } = await openDesign([{ type: 'kpi' }, { type: 'chart' }])
      await screen.findByTestId('chart-widget', {}, slow)
      await userEvent.click(within(design).getByTestId('studio-widget-/widgets/1'))
      const before = posts.length

      act(() => frame('/widgets/1').focus())
      await userEvent.keyboard('{Alt>}{ArrowDown}{/Alt}')

      expect(palette.getByTestId('studio-palette-announcement')).toHaveTextContent('The widget cannot move further.')
      await new Promise((resolve) => setTimeout(resolve, 500))
      expect(posts).toHaveLength(before)
    })

    it('does not move a widget with Alt+Arrow when another widget is selected', async () => {
      const { design, posts } = await openDesign([{ type: 'kpi' }, { type: 'chart' }])
      await screen.findByTestId('chart-widget', {}, slow)
      const before = posts.length

      act(() => frame('/widgets/1').focus())
      await userEvent.keyboard('{Alt>}{ArrowUp}{/Alt}')

      await new Promise((resolve) => setTimeout(resolve, 500))
      expect(posts).toHaveLength(before)
      expect(within(design).getByTestId('studio-widget-/widgets/0')).toHaveAttribute('aria-current', 'true')
    })

    it('flags a section dragged over its own list while dragging, and the drop changes nothing', async () => {
      const nest = [section([{ type: 'kpi' }])]
      const { palette } = await openDesign(nest)

      drag('/widgets/0', { kind: 'list', pointer: '/widgets/0/widgets' }, { drop: false })

      expect(screen.getByTestId('studio-drag-refusal')).toHaveTextContent('A widget cannot go inside itself.')
      expect(palette.getByTestId('studio-palette-announcement')).toHaveTextContent('A widget cannot go inside itself.')

      act(() =>
        dnd.props!.onDragEnd!({
          active: { id: 'move:/widgets/0', data: { current: { kind: 'move', pointer: '/widgets/0', label: 'x' } } },
          over: {
            id: 'list:/widgets/0/widgets',
            data: { current: { kind: 'list', pointer: '/widgets/0/widgets' } },
            rect: { top: 0, height: 100 },
          },
          delta: { x: 0, y: 0 },
          activatorEvent: new MouseEvent('pointerdown', { clientY: 10 }),
        } as never),
      )

      expect(screen.queryByTestId('studio-drag-refusal')).not.toBeInTheDocument()
      await expectDraftUnchanged(nest)
    })

    it('flags a container that would go past level 4 while dragging, and the drop changes nothing', async () => {
      const nest = [section([section([{ type: 'kpi' }])]), section([section([{ type: 'chart' }])])]
      const { palette } = await openDesign(nest)

      drag('/widgets/1', { kind: 'list', pointer: '/widgets/0/widgets/0/widgets' })

      expect(palette.getByTestId('studio-palette-announcement')).toHaveTextContent(
        'A container here would hold a list past level 4.',
      )
      await expectDraftUnchanged(nest)
    })

    it('shows the depth reason in the overlay during the drag', async () => {
      const nest = [section([section([{ type: 'kpi' }])]), section([section([{ type: 'chart' }])])]
      await openDesign(nest)

      drag('/widgets/1', { kind: 'list', pointer: '/widgets/0/widgets/0/widgets' }, { drop: false })

      expect(screen.getByTestId('studio-drag-refusal')).toHaveTextContent(
        'A container here would hold a list past level 4.',
      )
    })

    it('changes nothing and says nothing when a widget is dropped on itself', async () => {
      const { palette, posts } = await openDesign([{ type: 'kpi' }, { type: 'chart' }])
      await screen.findByTestId('chart-widget', {}, slow)
      const before = posts.length

      drag('/widgets/0', { kind: 'widget', pointer: '/widgets/0' }, { clientY: 90 })
      drag('/widgets/0', { kind: 'widget', pointer: '/widgets/1' }, { clientY: 10 })

      expect(palette.getByTestId('studio-palette-announcement')).toBeEmptyDOMElement()
      await new Promise((resolve) => setTimeout(resolve, 500))
      expect(posts).toHaveLength(before)
    })
  })
})
