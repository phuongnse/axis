import { act, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { describe, expect, it } from 'vitest'
import { followLink, renderGuarded } from '../test/leaveGuard'
import { leaveWithoutAsking, onlyTableViewChanged, useUnsavedChanges, withoutTableView } from './unsavedChanges'

function Editor({ counted = false }: { counted?: boolean }) {
  const [unsaved, setUnsaved] = useState(counted)
  useUnsavedChanges(unsaved)
  return <button onClick={() => setUnsaved((current) => !current)}>{unsaved ? 'Save' : 'Edit'}</button>
}

function beforeUnload() {
  const event = new Event('beforeunload', { cancelable: true })
  window.dispatchEvent(event)
  return event
}

/** Moves to an address, so there is a history entry to go Back to. */
async function startStudio(router: ReturnType<typeof renderGuarded>) {
  await act(() => router.navigate('/studio?app=A'))
  expect(router.state.location.search).toBe('?app=A')
}

describe('useUnsavedChanges', () => {
  it('lets a move that only changes a table view through, and still asks for any other address change', async () => {
    const router = renderGuarded(<Editor />)
    await startStudio(router)
    await userEvent.click(screen.getByRole('button', { name: 'Edit' }))

    await act(() => router.navigate('/studio?app=A&search=pa&sort=-name&page=2&pageSize=50'))
    expect(router.state.location.search).toBe('?app=A&search=pa&sort=-name&page=2&pageSize=50')
    await act(() => router.navigate(-1))
    await waitFor(() => expect(router.state.location.search).toBe('?app=A'))
    expect(screen.queryByText('Leave without saving?')).not.toBeInTheDocument()

    void router.navigate('/studio?app=B&search=pa')
    expect(await screen.findByText('Leave without saving?')).toBeInTheDocument()
    expect(router.state.location.search).toBe('?app=A')
  })

  it('asks "Leave without saving?" before a link while an editor is counted in, and Stay keeps the address', async () => {
    const router = renderGuarded(<Editor counted />)

    await followLink()

    expect(await screen.findByText('Leave without saving?')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Stay' }))
    expect(router.state.location.pathname).toBe('/studio')
  })

  it('moves on when the person picks Leave', async () => {
    const router = renderGuarded(<Editor counted />)

    await followLink()
    await userEvent.click(await screen.findByRole('button', { name: 'Leave' }))

    await waitFor(() => expect(router.state.location.pathname).toBe('/elsewhere'))
  })

  it('asks before a move that the app makes, and before Back', async () => {
    const router = renderGuarded(<Editor counted />)

    act(() => {
      void router.navigate('/studio?app=A')
    })
    expect(await screen.findByText('Leave without saving?')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Stay' }))
    expect(router.state.location.search).toBe('')

    await userEvent.click(screen.getByRole('button', { name: 'Save' }))
    await startStudio(router)
    await userEvent.click(screen.getByRole('button', { name: 'Edit' }))
    act(() => {
      void router.navigate(-1)
    })

    expect(await screen.findByText('Leave without saving?')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Stay' }))
    expect(router.state.location.search).toBe('?app=A')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))
    act(() => {
      void router.navigate(-1)
    })
    await waitFor(() => expect(router.state.location.search).toBe(''))
  })

  it('does not ask while no editor is counted in, or after the changes are saved', async () => {
    const router = renderGuarded(<Editor />)
    await startStudio(router)
    expect(screen.queryByText('Leave without saving?')).not.toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Edit' }))
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))
    await followLink()

    expect(router.state.location.pathname).toBe('/elsewhere')
    expect(screen.queryByText('Leave without saving?')).not.toBeInTheDocument()
  })

  it('does not ask about a move that keeps the address', async () => {
    const router = renderGuarded(<Editor counted />)

    await act(() => router.navigate('/studio#top'))

    expect(router.state.location.hash).toBe('#top')
    expect(screen.queryByText('Leave without saving?')).not.toBeInTheDocument()
  })

  it('lets a move through that carries leaveWithoutAsking', async () => {
    const router = renderGuarded(<Editor counted />)

    await act(() => router.navigate('/studio?app=A', { state: leaveWithoutAsking }))

    expect(router.state.location.search).toBe('?app=A')
    expect(screen.queryByText('Leave without saving?')).not.toBeInTheDocument()
  })

  it('asks when Back returns to an entry that carries leaveWithoutAsking', async () => {
    const router = renderGuarded(<Editor />)
    await act(() => router.navigate('/studio?app=A', { state: leaveWithoutAsking }))
    await act(() => router.navigate('/studio?app=B'))
    await userEvent.click(screen.getByRole('button', { name: 'Edit' }))

    act(() => {
      void router.navigate(-1)
    })

    expect(await screen.findByText('Leave without saving?')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Stay' }))
    expect(router.state.location.search).toBe('?app=B')
  })

  it('cancels the reload and the close of the tab only while changes are unsaved', async () => {
    renderGuarded(<Editor />)
    expect(beforeUnload().defaultPrevented).toBe(false)

    await userEvent.click(screen.getByRole('button', { name: 'Edit' }))
    expect(beforeUnload().defaultPrevented).toBe(true)

    await userEvent.click(screen.getByRole('button', { name: 'Save' }))
    expect(beforeUnload().defaultPrevented).toBe(false)
  })

  it('does nothing where no guard is mounted', () => {
    render(<Editor counted />)

    expect(screen.getByRole('button', { name: 'Save' })).toBeInTheDocument()
    expect(beforeUnload().defaultPrevented).toBe(false)
  })
})

describe('onlyTableViewChanged', () => {
  const filter = encodeURIComponent(JSON.stringify({ all: [{ field: 'kind', op: 'eq', value: 'form' }] }))

  it('treats a change of only the filter as a change of the table view', () => {
    expect(
      onlyTableViewChanged(
        { pathname: '/studio', search: '?app=A' },
        { pathname: '/studio', search: `?app=A&filter=${filter}` },
      ),
    ).toBe(true)
  })

  it('treats a change of only a prefixed table view parameter as a change of the table view', () => {
    expect(
      onlyTableViewChanged(
        { pathname: '/studio', search: '?app=A' },
        { pathname: '/studio', search: `?app=A&fields.filter=${filter}` },
      ),
    ).toBe(true)
    expect(
      onlyTableViewChanged(
        { pathname: '/studio', search: '?app=A&records.page=2' },
        { pathname: '/studio', search: `?app=A&records.pageSize=50&fields.sort=name` },
      ),
    ).toBe(true)
    expect(
      onlyTableViewChanged(
        { pathname: '/studio', search: '?app=A' },
        { pathname: '/studio', search: `?app=B&fields.filter=${filter}` },
      ),
    ).toBe(false)
  })

  it('does not treat a change of another parameter as a change of the table view', () => {
    expect(
      onlyTableViewChanged(
        { pathname: '/studio', search: '?app=A' },
        { pathname: '/studio', search: `?app=B&filter=${filter}` },
      ),
    ).toBe(false)
  })
})

describe('withoutTableView', () => {
  it('drops the view of the table and of every prefixed table, and keeps the other parameters and the columns', () => {
    const params = new URLSearchParams(
      'app=A&resource=r1&search=x&filter=f&sort=name&page=2&pageSize=50&columns=name&fields.search=code&fields.filter=f&text.page=3',
    )

    expect(withoutTableView(params).toString()).toBe('app=A&resource=r1&columns=name')
    expect(params.get('search')).toBe('x')
  })

  it('returns the address as it is when it holds no table view', () => {
    expect(withoutTableView(new URLSearchParams('app=A&resource=r1')).toString()).toBe('app=A&resource=r1')
  })
})
