import { act, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { PageScroll } from './PageScroll'
import { useTableView } from './useTableView'

const noFields: never[] = []

function Table() {
  const { view, setView } = useTableView(noFields)
  return (
    <>
      <PageScroll />
      <output data-testid="page">{view.page}</output>
      <button onClick={() => setView({ ...view, page: 2 })}>Push</button>
      <button onClick={() => setView({ ...view, page: 3 }, { replace: true })}>Replace</button>
    </>
  )
}

describe('PageScroll', () => {
  afterEach(() => vi.restoreAllMocks())

  it('keeps the scroll position when a table view changes, by a pushed or a replaced entry', async () => {
    const scrollTo = vi.spyOn(window, 'scrollTo').mockImplementation(() => {})
    const router = createMemoryRouter([{ path: '*', element: <Table /> }], { initialEntries: ['/?page=1'] })
    render(<RouterProvider router={router} />)
    scrollTo.mockClear()

    await userEvent.click(screen.getByRole('button', { name: 'Push' }))
    expect(screen.getByTestId('page')).toHaveTextContent('2')
    expect(router.state.historyAction).toBe('PUSH')
    expect(scrollTo).not.toHaveBeenCalled()

    await userEvent.click(screen.getByRole('button', { name: 'Replace' }))
    expect(screen.getByTestId('page')).toHaveTextContent('3')
    expect(router.state.historyAction).toBe('REPLACE')
    expect(scrollTo).not.toHaveBeenCalled()
  })

  it('scrolls to the top at a new address', async () => {
    const scrollTo = vi.spyOn(window, 'scrollTo').mockImplementation(() => {})
    const router = createMemoryRouter([{ path: '*', element: <Table /> }], { initialEntries: ['/'] })
    render(<RouterProvider router={router} />)
    scrollTo.mockClear()

    await act(() => router.navigate('/other'))
    expect(scrollTo).toHaveBeenLastCalledWith(0, 0)
  })
})
