import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { App } from './App'
import { ThemeModeProvider } from './platform/theme'

function renderApp() {
  return render(
    <ThemeModeProvider>
      <App />
    </ThemeModeProvider>,
  )
}

describe('App', () => {
  it('shows the server as ready when the readiness check succeeds', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{}', { status: 200 })))

    renderApp()

    expect(await screen.findByText('Ready')).toBeInTheDocument()
    expect(fetch).toHaveBeenCalledWith('/health/ready', expect.anything())
  })

  it('shows the server as unavailable when the readiness check fails', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{}', { status: 503 })))

    renderApp()

    expect(await screen.findByText('Unavailable')).toBeInTheDocument()
  })

  it('switches between light and dark mode and remembers the choice', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{}', { status: 200 })))
    renderApp()
    const toggle = screen.getByRole('switch', { name: 'Dark mode' })
    expect(toggle).not.toBeChecked()

    await userEvent.click(toggle)

    expect(toggle).toBeChecked()
    expect(localStorage.getItem('axis.themeMode')).toBe('dark')
  })
})
