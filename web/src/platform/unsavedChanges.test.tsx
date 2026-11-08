import { act, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { describe, expect, it } from 'vitest'
import { followLink, renderGuarded } from '../test/leaveGuard'
import { leaveWithoutAsking, useUnsavedChanges } from './unsavedChanges'

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
