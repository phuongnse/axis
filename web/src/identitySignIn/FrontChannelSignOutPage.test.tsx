import { fireEvent, render, screen } from '@testing-library/react'
import { StrictMode } from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { identityFrontChannelSignOutContext } from '../designSystem/fixtures'
import { ThemeModeProvider } from '../platform/theme'
import { FrontChannelSignOutPage } from './FrontChannelSignOutPage'
import type { FrontChannelSignOutContext } from './signInContext'

const context: FrontChannelSignOutContext = {
  ...identityFrontChannelSignOutContext,
  realmName: 'Contoso',
  clients: [
    { name: 'First', url: 'https://one.example/logout' },
    { name: 'Second', url: 'https://two.example/logout' },
  ],
  redirectUrl: 'https://home.example/',
}

function mockReplace() {
  const replace = vi.fn()
  vi.spyOn(window, 'location', 'get').mockReturnValue({ ...window.location, replace })
  return replace
}

describe('FrontChannelSignOutPage', () => {
  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('loads one hidden frame per client', () => {
    mockReplace()

    const { container } = render(<FrontChannelSignOutPage context={context} />, { wrapper: ThemeModeProvider })
    const frames = container.querySelectorAll('iframe')

    expect(frames).toHaveLength(2)
    expect(frames[0]).toHaveAttribute('src', 'https://one.example/logout')
    expect(frames[0]).toHaveAttribute('title', 'First')
    expect(frames[1]).toHaveAttribute('src', 'https://two.example/logout')
    for (const frame of frames) {
      expect(frame).toHaveAttribute('hidden')
    }
  })

  it('goes on once, after every frame has loaded', () => {
    const replace = mockReplace()
    const { container } = render(
      <StrictMode>
        <ThemeModeProvider>
          <FrontChannelSignOutPage context={context} />
        </ThemeModeProvider>
      </StrictMode>,
    )
    const frames = container.querySelectorAll('iframe')

    fireEvent.load(frames[0])
    expect(replace).not.toHaveBeenCalled()
    fireEvent.load(frames[1])

    expect(replace).toHaveBeenCalledTimes(1)
    expect(replace).toHaveBeenCalledWith('https://home.example/')
  })

  it('goes on at once when there are no clients', () => {
    const replace = mockReplace()

    render(<FrontChannelSignOutPage context={{ ...context, clients: [] }} />, { wrapper: ThemeModeProvider })

    expect(replace).toHaveBeenCalledTimes(1)
    expect(replace).toHaveBeenCalledWith('https://home.example/')
  })

  it('stays when Keycloak gives no redirect address', () => {
    const replace = mockReplace()

    render(<FrontChannelSignOutPage context={{ ...context, clients: [], redirectUrl: undefined }} />, {
      wrapper: ThemeModeProvider,
    })

    expect(replace).not.toHaveBeenCalled()
    expect(screen.queryByRole('link', { name: 'Continue' })).not.toBeInTheDocument()
  })

  it('links Continue to the redirect address as a way out', () => {
    mockReplace()

    render(<FrontChannelSignOutPage context={context} />, { wrapper: ThemeModeProvider })

    expect(screen.getByRole('link', { name: 'Continue' })).toHaveAttribute('href', 'https://home.example/')
  })
})
