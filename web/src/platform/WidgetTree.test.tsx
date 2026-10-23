import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useLocation } from 'react-router'
import { describe, expect, it, vi } from 'vitest'
import type { PageWidgetMetadata, WidgetMetadata } from './site'
import { TableWidget } from './TableWidget'
import { TaskInboxWidget } from './TaskInboxWidget'
import { TextProvider } from './TextProvider'
import { WidgetTree } from './WidgetTree'

function leaf(type: WidgetMetadata['type'], extra: Partial<WidgetMetadata> = {}): WidgetMetadata {
  return {
    type,
    entity: null,
    dataSource: null,
    form: null,
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
    ...extra,
  }
}

const recordsPath = '/api/apps/E2eApp/entities/Note/records'
const tasksPath = '/api/apps/E2eApp/tasks'

const noteTable = leaf('table', {
  entity: {
    name: 'Note',
    labelKey: 'note.label',
    displayField: null,
    recordsPath,
    fields: [
      {
        name: 'title',
        type: 'text',
        labelKey: 'note.title',
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
      },
    ],
  },
  access: { actions: ['read'], createFields: [], updateFields: [] },
})
const inbox = leaf('taskInbox', { tasksPath })

const texts = {
  'shell.table.empty': 'No records yet.',
  'shell.table.loadFailed': 'The records could not be loaded.',
  'shell.tasks.task': 'Task',
  'shell.tasks.subject': 'Record',
  'shell.tasks.dueAt': 'Due',
  'shell.tasks.empty': 'No open tasks.',
  'shell.tasks.loadFailed': 'The tasks could not be loaded.',
  'note.title': 'Title',
  'tab.requests': 'Requests',
  'tab.tasks': 'Tasks',
  'tab.more': 'More',
  'section.totals': 'Totals',
  'section.inner': 'Inner',
}
const catalogs = [{ texts, fallbackTexts: texts }]

// A stand-in leaf that names its widget by `process`, which the tests set to tell the widgets apart.
function Probe({ widget }: { widget: WidgetMetadata }) {
  return <p data-testid="leaf">{widget.process}</p>
}

const named = (name: string) => leaf('processInspector', { process: name })

function CurrentUrl() {
  const location = useLocation()
  return <output data-testid="url">{location.pathname + location.search}</output>
}

function renderTree(
  widgets: PageWidgetMetadata[],
  url = '/e2e/page',
  renderLeaf: (widget: WidgetMetadata) => React.ReactNode = (widget) => <Probe widget={widget} />,
) {
  return render(
    <MemoryRouter initialEntries={[url]}>
      <TextProvider catalogs={catalogs} development={false}>
        <WidgetTree widgets={widgets} renderLeaf={renderLeaf} />
      </TextProvider>
      <CurrentUrl />
    </MemoryRouter>,
  )
}

// A new object each time, as page metadata has one per tabs widget.
const tabsWidget = (): PageWidgetMetadata => ({
  type: 'tabs',
  tabs: [
    { labelKey: 'tab.requests', widgets: [named('first')] },
    { labelKey: 'tab.tasks', widgets: [named('second')] },
    { labelKey: 'tab.more', widgets: [named('third')] },
  ],
})

describe('WidgetTree', () => {
  it('draws the widgets in file order, and a section with its title above its widgets', () => {
    renderTree([
      named('one'),
      { type: 'section', titleKey: 'section.totals', collapsed: false, widgets: [named('two'), named('three')] },
      named('four'),
    ])

    expect(screen.getAllByTestId('leaf').map((element) => element.textContent)).toEqual(['one', 'two', 'three', 'four'])
    const section = screen.getByRole('region', { name: 'Totals' })
    expect(within(section).getByText('Totals')).toBeInTheDocument()
    expect(within(section).getAllByTestId('leaf')).toHaveLength(2)
  })

  it('draws columns side by side in file order, each as wide as its width in twelfths of 24 parts', () => {
    const { container } = renderTree([
      {
        type: 'columns',
        columns: [
          { width: 8, widgets: [named('wide')] },
          { width: 4, widgets: [named('narrow')] },
        ],
      },
    ])

    const columns = container.querySelectorAll('.ant-col')
    expect(columns).toHaveLength(2)
    expect(columns[0]).toHaveClass('ant-col-md-16', 'ant-col-xs-24')
    expect(columns[1]).toHaveClass('ant-col-md-8', 'ant-col-xs-24')
    expect(within(columns[0] as HTMLElement).getByTestId('leaf')).toHaveTextContent('wide')
    expect(within(columns[1] as HTMLElement).getByTestId('leaf')).toHaveTextContent('narrow')
  })

  it('draws containers inside containers', () => {
    renderTree([
      {
        type: 'columns',
        columns: [
          {
            width: 12,
            widgets: [{ type: 'section', titleKey: 'section.inner', collapsed: false, widgets: [named('deep')] }],
          },
        ],
      },
    ])

    expect(within(screen.getByRole('region', { name: 'Inner' })).getByTestId('leaf')).toHaveTextContent('deep')
  })

  describe('tabs', () => {
    it('shows one header per tab with its label, and the first tab without a tab parameter', () => {
      renderTree([tabsWidget()])

      expect(screen.getAllByRole('tab').map((tab) => tab.textContent)).toEqual(['Requests', 'Tasks', 'More'])
      expect(screen.getByRole('tab', { name: 'Requests' })).toHaveAttribute('aria-selected', 'true')
      expect(screen.getByTestId('leaf')).toHaveTextContent('first')
    })

    it('selects the tab that tab names, counted from 1', () => {
      renderTree([tabsWidget()], '/e2e/page?tab=2')

      expect(screen.getByRole('tab', { name: 'Tasks' })).toHaveAttribute('aria-selected', 'true')
      expect(screen.getByTestId('leaf')).toHaveTextContent('second')
    })

    it.each(['9', '0', '-1', 'abc', '', '1.5', '2x'])('ignores the invalid value %j and keeps the address', (value) => {
      renderTree([tabsWidget()], `/e2e/page?tab=${value}`)

      expect(screen.getByRole('tab', { name: 'Requests' })).toHaveAttribute('aria-selected', 'true')
      expect(screen.getByTestId('url')).toHaveTextContent(`/e2e/page?tab=${value}`)
    })

    it('writes tab on a click, removes it for the first tab, and keeps the other parameters', async () => {
      renderTree([tabsWidget()], '/e2e/page?q=x')

      await userEvent.click(screen.getByRole('tab', { name: 'More' }))

      expect(screen.getByTestId('url')).toHaveTextContent('/e2e/page?q=x&tab=3')
      expect(screen.getByRole('tab', { name: 'More' })).toHaveAttribute('aria-selected', 'true')

      await userEvent.click(screen.getByRole('tab', { name: 'Requests' }))

      expect(screen.getByTestId('url')).toHaveTextContent(/^\/e2e\/page\?q=x$/)
      expect(screen.getByRole('tab', { name: 'Requests' })).toHaveAttribute('aria-selected', 'true')
    })

    it('keeps each tabs widget of a page in its own parameter', async () => {
      renderTree([tabsWidget(), tabsWidget()], '/e2e/page?tab2=3')

      const [first, second] = screen.getAllByRole('tablist')
      expect(within(first).getByRole('tab', { name: 'Requests' })).toHaveAttribute('aria-selected', 'true')
      expect(within(second).getByRole('tab', { name: 'More' })).toHaveAttribute('aria-selected', 'true')

      await userEvent.click(within(first).getByRole('tab', { name: 'Tasks' }))

      expect(screen.getByTestId('url')).toHaveTextContent('/e2e/page?tab2=3&tab=2')
    })

    it('draws only the selected tab at first, and the other tab when it is chosen', async () => {
      renderTree([tabsWidget()])

      expect(screen.getAllByTestId('leaf').map((element) => element.textContent)).toEqual(['first'])

      await userEvent.click(screen.getByRole('tab', { name: 'Tasks' }))

      expect(screen.getAllByTestId('leaf').map((element) => element.textContent)).toContain('second')
    })
  })

  describe('sections', () => {
    it('starts open unless the section is collapsed', () => {
      renderTree([{ type: 'section', titleKey: 'section.totals', collapsed: false, widgets: [named('body')] }])

      expect(screen.getByRole('button', { name: 'Totals' })).toHaveAttribute('aria-expanded', 'true')
      expect(screen.getByTestId('leaf')).toBeVisible()
    })

    it('starts closed with its title when collapsed, draws nothing, and opens on a click', async () => {
      renderTree([{ type: 'section', titleKey: 'section.totals', collapsed: true, widgets: [named('body')] }])

      const header = screen.getByRole('button', { name: 'Totals' })
      expect(screen.getByRole('region', { name: 'Totals' })).toBeInTheDocument()
      expect(header).toHaveAttribute('aria-expanded', 'false')
      expect(screen.queryByTestId('leaf')).not.toBeInTheDocument()

      await userEvent.click(header)

      expect(header).toHaveAttribute('aria-expanded', 'true')
      await waitFor(() => expect(screen.getByTestId('leaf')).toBeVisible())

      await userEvent.click(header)

      expect(header).toHaveAttribute('aria-expanded', 'false')
    })
  })

  it('keeps the error of one widget and the empty state of another inside their own columns', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: string) =>
        input.startsWith(recordsPath)
          ? new Response('{}', { status: 500 })
          : new Response('{"items":[],"page":1,"pageSize":20,"totalCount":0}', { status: 200 }),
      ),
    )
    const renderReal = (widget: WidgetMetadata) =>
      widget.type === 'table' ? (
        <TableWidget sitePath="e2e" widget={widget} locale="en" pageValues={{}} />
      ) : (
        <TaskInboxWidget sitePath="e2e" pageName="Tasks" widget={widget} locale="en" fallbackLocale="en" />
      )

    renderTree(
      [
        {
          type: 'columns',
          columns: [
            { width: 8, widgets: [noteTable] },
            { width: 4, widgets: [inbox] },
          ],
        },
      ],
      '/e2e/page',
      renderReal,
    )

    const failed = await screen.findByTestId('records-error')
    expect(failed).toHaveTextContent('The records could not be loaded.')
    expect(await screen.findByText('No open tasks.')).toBeInTheDocument()
    const columns = Array.from(document.querySelectorAll<HTMLElement>('.ant-col'))
    expect(within(columns[0]).getByTestId('records-error')).toBe(failed)
    expect(within(columns[0]).queryByText('No open tasks.')).not.toBeInTheDocument()
    expect(within(columns[1]).getByText('No open tasks.')).toBeInTheDocument()
    expect(within(columns[1]).queryByTestId('records-error')).not.toBeInTheDocument()
  })

  it('asks for no rows from a table in a closed section or in a tab that is not selected', async () => {
    const fetchMock = vi.fn(async () => new Response('{"items":[],"page":1,"pageSize":20,"totalCount":0}'))
    vi.stubGlobal('fetch', fetchMock)
    const renderReal = (widget: WidgetMetadata) => (
      <TableWidget sitePath="e2e" widget={widget} locale="en" pageValues={{}} />
    )

    renderTree(
      [
        { type: 'section', titleKey: 'section.totals', collapsed: true, widgets: [noteTable] },
        {
          type: 'tabs',
          tabs: [
            { labelKey: 'tab.requests', widgets: [] },
            { labelKey: 'tab.tasks', widgets: [noteTable] },
          ],
        },
      ],
      '/e2e/page',
      renderReal,
    )

    await screen.findByRole('tab', { name: 'Tasks' })
    expect(fetchMock).not.toHaveBeenCalled()

    await userEvent.click(screen.getByRole('tab', { name: 'Tasks' }))

    expect(await screen.findByText('No records yet.')).toBeInTheDocument()
    expect(fetchMock).toHaveBeenCalledTimes(1)
  })
})
