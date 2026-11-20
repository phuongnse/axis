import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useLocation, useNavigate } from 'react-router'
import { describe, expect, it } from 'vitest'
import { useAddressTab } from './useAddressTab'

const keys = ['diagram', 'history', 'data']

function Probe() {
  const [selected, select] = useAddressTab('tab', keys)
  const location = useLocation()
  const navigate = useNavigate()
  return (
    <>
      <output data-testid="selected">{selected}</output>
      <output data-testid="url">{location.pathname + location.search}</output>
      {keys.map((key) => (
        <button key={key} type="button" onClick={() => select(key)}>
          {key}
        </button>
      ))}
      <button type="button" onClick={() => void navigate(-1)}>
        back
      </button>
    </>
  )
}

function renderProbe(url: string) {
  return render(
    <MemoryRouter initialEntries={[url]}>
      <Probe />
    </MemoryRouter>,
  )
}

describe('useAddressTab', () => {
  it('selects the first key without the parameter', () => {
    renderProbe('/page')

    expect(screen.getByTestId('selected')).toHaveTextContent('diagram')
  })

  it('selects the key that the parameter names', () => {
    renderProbe('/page?tab=history')

    expect(screen.getByTestId('selected')).toHaveTextContent('history')
  })

  it.each(['bogus', '', '1', 'History'])('ignores the unknown value %j and keeps the address', (value) => {
    renderProbe(`/page?tab=${value}`)

    expect(screen.getByTestId('selected')).toHaveTextContent('diagram')
    expect(screen.getByTestId('url')).toHaveTextContent(`/page?tab=${value}`)
  })

  it('writes the parameter for a key, keeps the other parameters, and removes it for the first key', async () => {
    renderProbe('/page?q=x')

    await userEvent.click(screen.getByRole('button', { name: 'data' }))

    expect(screen.getByTestId('url')).toHaveTextContent('/page?q=x&tab=data')
    expect(screen.getByTestId('selected')).toHaveTextContent('data')

    await userEvent.click(screen.getByRole('button', { name: 'diagram' }))

    expect(screen.getByTestId('url')).toHaveTextContent(/^\/page\?q=x$/)
    expect(screen.getByTestId('selected')).toHaveTextContent('diagram')
  })

  it('adds one history entry for each choice, so back returns to the previous tab', async () => {
    renderProbe('/page')

    await userEvent.click(screen.getByRole('button', { name: 'history' }))
    await userEvent.click(screen.getByRole('button', { name: 'data' }))
    await userEvent.click(screen.getByRole('button', { name: 'back' }))

    expect(screen.getByTestId('selected')).toHaveTextContent('history')

    await userEvent.click(screen.getByRole('button', { name: 'back' }))

    expect(screen.getByTestId('selected')).toHaveTextContent('diagram')
  })
})
