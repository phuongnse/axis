import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useLocation } from 'react-router'
import { describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { assignLocation } from './navigation'
import { ProcessInspectorWidget } from './ProcessInspectorWidget'
import type { WidgetMetadata } from './site'
import { TextProvider } from './TextProvider'

vi.mock('./navigation')

const instancesPath = '/api/apps/E2eApp/processes/NoteReview/instances'

const inspectorWidget: WidgetMetadata = {
  type: 'processInspector',
  entity: null,
  dataSource: null,
  form: null,
  tasksPath: null,
  process: 'NoteReview',
  instancesPath,
  actions: [],
  rowActions: [],
  toolbarActions: [],
  access: null,
}

const waitingId = '0192f4a9-2d3e-7f40-8a1b-2c3d4e5f6a7b'
const completedId = '0192f4a9-2d3e-7f40-8a1b-2c3d4e5f6a7c'

// In the inspection API's order: newest first.
const instancePage = JSON.stringify({
  items: [
    {
      id: waitingId,
      subjectId: '0192f4a7-3b1c-7e2d-9a4f-1c2d3e4f5a6b',
      state: 'waiting',
      step: 'managerApproval',
      releaseId: '0192f49a-1b2c-7d3e-8f40-5b6c7d8e9f01',
      createdAt: '2026-10-10T02:05:00.123456Z',
      endedAt: null,
    },
    {
      id: completedId,
      subjectId: '0192f4a7-3b1c-7e2d-9a4f-1c2d3e4f5a6d',
      state: 'completed',
      step: 'done',
      releaseId: '0192f49a-1b2c-7d3e-8f40-5b6c7d8e9f01',
      createdAt: '2026-10-09T02:05:00Z',
      endedAt: '2026-10-09T03:05:00Z',
    },
  ],
  page: 1,
  pageSize: 20,
  totalCount: 2,
})

const catalogs = [{ texts: platformEnglish, fallbackTexts: platformEnglish }]

/** Stubs `fetch` with the instance list and returns the requested URLs. */
function stubInstances(body = instancePage, status = 200) {
  const fetchMock = vi.fn(async (_input: string) => new Response(body, { status }))
  vi.stubGlobal('fetch', fetchMock)
  return () => fetchMock.mock.calls.map(([url]) => url)
}

function CurrentSearch() {
  return <output data-testid="search">{useLocation().search}</output>
}

function renderWidget(search = '') {
  return render(
    <MemoryRouter initialEntries={[`/e2e/inspector${search}`]}>
      <TextProvider catalogs={catalogs} development={false}>
        <ProcessInspectorWidget sitePath="e2e" pageName="Inspector" widget={inspectorWidget} locale="en" />
      </TextProvider>
      <CurrentSearch />
    </MemoryRouter>,
  )
}

describe('ProcessInspectorWidget', () => {
  it('lists the instances in the order of the inspection API, each linking to its address under the page', async () => {
    stubInstances()

    renderWidget()

    await screen.findByRole('row', { name: /managerApproval/ })
    for (const title of ['State', 'Step', 'Record', 'Release', 'Started', 'Ended']) {
      expect(screen.getByRole('columnheader', { name: title })).toBeInTheDocument()
    }
    const [waiting, completed] = screen.getAllByRole('row').slice(1)
    expect(within(waiting).getByText('Waiting')).toBeInTheDocument()
    expect(within(waiting).getByRole('link', { name: 'Open' })).toHaveAttribute('href', `/e2e/inspector/${waitingId}`)
    expect(within(completed).getByText('Completed')).toBeInTheDocument()
    expect(within(completed).getByText('done')).toBeInTheDocument()
    expect(within(completed).getByRole('link', { name: 'Open' })).toHaveAttribute(
      'href',
      `/e2e/inspector/${completedId}`,
    )
    // An instance that has not ended has an empty end cell.
    expect(within(waiting).getAllByRole('cell')[5]).toHaveTextContent(/^$/)
  })

  it('sends the state in the URL to the instance list', async () => {
    const requests = stubInstances()

    renderWidget('?state=waiting')

    await screen.findByRole('row', { name: /managerApproval/ })
    expect(requests()).toEqual([`${instancesPath}?state=waiting`])
  })

  it('writes the chosen state to the URL and goes back to page 1', async () => {
    const requests = stubInstances()
    const user = userEvent.setup()

    renderWidget('?state=waiting&page=2&pageSize=10')

    await screen.findByRole('row', { name: /managerApproval/ })
    await user.click(screen.getByRole('combobox', { name: 'State' }))
    await user.click(await screen.findByTitle('Failed'))

    await waitFor(() => expect(screen.getByTestId('search').textContent).toBe('?state=failed&pageSize=10'))
    await waitFor(() => expect(requests()).toContain(`${instancesPath}?state=failed&pageSize=10`))
  })

  it('clears the state filter', async () => {
    const requests = stubInstances()
    const user = userEvent.setup()

    renderWidget('?state=waiting')

    await screen.findByRole('row', { name: /managerApproval/ })
    const select = screen.getByRole('combobox', { name: 'State' })
    await user.hover(select)
    await user.click(await screen.findByLabelText('close-circle'))

    await waitFor(() => expect(screen.getByTestId('search').textContent).toBe(''))
    await waitFor(() => expect(requests()).toContain(instancesPath))
  })

  it('drops a state the API does not know from the URL and requests the unfiltered list', async () => {
    const requests = stubInstances()

    renderWidget('?state=sleeping&page=0')

    await screen.findByRole('row', { name: /managerApproval/ })
    await waitFor(() => expect(screen.getByTestId('search').textContent).toBe(''))
    expect(requests()).toEqual([instancesPath])
  })

  it('shows the may-not-inspect message and no table on a 403', async () => {
    stubInstances('', 403)

    renderWidget()

    expect(await screen.findByTestId('inspector-forbidden')).toHaveTextContent('You may not inspect this process.')
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
  })

  it('shows a load error on any other failure', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    stubInstances('', 500)

    renderWidget()

    expect(await screen.findByTestId('inspector-error')).toHaveTextContent('The instances could not be loaded.')
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
  })

  it('starts sign-in with the current path and query string when nobody is signed in', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    vi.mocked(assignLocation).mockReset()
    history.pushState(null, '', '/e2e/inspector?state=waiting')
    stubInstances('', 401)

    renderWidget('?state=waiting')

    await waitFor(() =>
      expect(assignLocation).toHaveBeenCalledExactlyOnceWith(
        '/api/auth/sign-in?returnUrl=%2Fe2e%2Finspector%3Fstate%3Dwaiting',
      ),
    )
  })

  it('shows the empty text when there are no instances', async () => {
    stubInstances(JSON.stringify({ items: [], page: 1, pageSize: 20, totalCount: 0 }))

    renderWidget()

    expect(await screen.findByText('No instances.')).toBeInTheDocument()
  })
})
