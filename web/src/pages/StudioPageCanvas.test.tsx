import type { DndContextProps } from '@dnd-kit/core'
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useLocation } from 'react-router'
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { chartWidget, kpiWidget } from '../designSystem/fixtures'
import type { ChartProps } from '../platform/Chart'
import type { NavigateAction, PageMetadata, PageWidgetMetadata } from '../platform/site'
import type { StudioDiagnostic, StudioNames, StudioResourceItem } from '../platform/studio'
import { StudioNamesContext } from '../platform/studioNames'
import { newWidget } from '../platform/pageWidgets'
import { loadKindSchema } from '../platform/studioSchemas'
import { ThemeModeProvider } from '../platform/theme'
import { TextProvider } from '../platform/TextProvider'
import { chooseOption } from '../test/antd'
import { StudioEditor } from './StudioEditor'

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
  }
})

// The chart library is not under test here.
vi.mock('../platform/Chart', () => ({
  Chart: (props: ChartProps) => <div role="img" aria-label={props.title} />,
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
  answer: (draft: Draft) => Response = (draft) =>
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

function renderEditor() {
  render(
    <MemoryRouter initialEntries={['/studio?app=Shop&resource=dashboard']}>
      <ThemeModeProvider>
        <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
          <StudioNamesContext.Provider value={names}>
            <StudioEditor
              application="Shop"
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
      <LocationProbe />
    </MemoryRouter>,
  )
}

const slow = { timeout: 5000 }

function openTab(name: string) {
  return userEvent.click(screen.getByRole('tab', { name }))
}

function appRequests(fetchMock: ReturnType<typeof vi.fn>) {
  return fetchMock.mock.calls.filter(([input]) => String(input).startsWith('/api/apps/'))
}

beforeAll(() => loadKindSchema('page'))

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('the Design tab of a page', () => {
  it('opens on the Form tab, then draws the posted draft, and draws again after an edit in the Form tab and the JSON tab', async () => {
    const { fetchMock, posts } = stub(content([{ type: 'kpi' }]))
    renderEditor()

    await screen.findByTestId('studio-page-editor', {}, slow)
    expect(screen.queryByTestId('studio-page-canvas')).not.toBeInTheDocument()

    await openTab('Design')
    const canvas = await screen.findByTestId('studio-page-canvas', {}, slow)
    expect(await within(canvas).findByTestId('kpi-tile', {}, slow)).toBeInTheDocument()
    await waitFor(
      () => expect(within(canvas).getByTestId('kpi-value')).toHaveTextContent('Total amount: 1250.50'),
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

  it('leaves the location where it is when a navigate action is clicked', async () => {
    const { fetchMock } = stub(content([{ type: 'kpi', navigate: { page: 'Tickets' } }]))
    renderEditor()

    await screen.findByTestId('studio-page-editor', {}, slow)
    await openTab('Design')
    const link = await within(await screen.findByTestId('studio-page-canvas')).findByRole('link', {}, slow)
    expect(link).toHaveAttribute('href', '/Shop/tickets')
    const before = screen.getByTestId('location').textContent
    expect(before).toBe('/studio?app=Shop&resource=dashboard')

    await userEvent.click(link)

    expect(screen.getByTestId('location')).toHaveTextContent(before!)
    expect(screen.getByTestId('studio-page-canvas')).toBeInTheDocument()
    expect(appRequests(fetchMock)).toEqual([])
  })

  it('draws a widget that does not compile as a placeholder, even one whose type is columns, and draws the rest', async () => {
    stub(content([{ type: 'kpi' }, { type: 'columns' }]))
    renderEditor()

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

    await screen.findByTestId('studio-page-editor', {}, slow)
    await openTab('Design')
    const canvas = await screen.findByTestId('studio-page-canvas', {}, slow)
    const cells = await within(canvas).findAllByTestId('column', {}, slow)
    expect(cells[0]).toHaveClass('ant-col-md-8')
    expect(cells[1]).toHaveClass('ant-col-md-16')

    await userEvent.click(screen.getByText('Phone'))

    expect(within(canvas).getByTestId('studio-canvas-frame')).toHaveStyle({ width: '375px' })
    for (const cell of cells) {
      expect(cell).toHaveClass('ant-col-md-24')
    }
  })

  describe('selection', () => {
    async function openDesign(widgets: Record<string, unknown>[]) {
      const stubbed = stub(content(widgets))
      renderEditor()
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
      await within(design).findByTestId('chart-widget', {}, slow)
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
      await within(design).findByTestId('chart-widget', {}, slow)

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

      expect(await within(design).findByTestId('chart-widget', {}, slow)).toBeInTheDocument()
      expect(posts.at(-1)!.widgets[0].type).toBe('chart')
    })

    it('moves between the widgets with Tab and selects one with Enter', async () => {
      const { design } = await openDesign([{ type: 'kpi' }, { type: 'chart' }])
      await within(design).findByTestId('chart-widget', {}, slow)

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
      await within(design).findByTestId('chart-widget', {}, slow)
      await userEvent.click(within(design).getByTestId('chart-widget'))

      await openTab('Form')
      await openTab('Design')

      const again = await screen.findByTestId('studio-page-design', {}, slow)
      await within(again).findByTestId('chart-widget', {}, slow)
      expect(frame(again, '/widgets/1')).toHaveAttribute('aria-current', 'true')
    })
  })

  describe('palette', () => {
    async function openDesign(widgets: Record<string, unknown>[]) {
      const stubbed = stub(content(widgets))
      renderEditor()
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
})
