import { render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { describe, expect, it } from 'vitest'
import { PageLayout } from './PageLayout'
import { TextProvider } from './TextProvider'
import { ThemeModeProvider } from './theme'

describe('PageLayout', () => {
  it.each(['light', 'dark'])('shows the title as a heading and the content in %s mode', (mode) => {
    localStorage.setItem('axis.themeMode', mode)

    render(
      <ThemeModeProvider>
        <PageLayout title="Purchase requests">
          <p>Page content</p>
        </PageLayout>
      </ThemeModeProvider>,
    )

    expect(screen.getByRole('heading', { name: 'Purchase requests' })).toBeInTheDocument()
    expect(screen.getByText('Page content')).toBeInTheDocument()
  })

  it('puts the breadcrumb above the title, with links but the last step, which is the current page', () => {
    render(
      <ThemeModeProvider>
        <TextProvider
          catalogs={[{ texts: { 'shell.breadcrumb.label': 'Breadcrumb' }, fallbackTexts: {} }]}
          development={false}
        >
          <MemoryRouter>
            <PageLayout
              title="New note"
              breadcrumb={[
                { label: 'E2E notes', to: '/e2e' },
                { label: 'Notes', to: '/e2e/notes?page=2' },
                { label: 'New note' },
              ]}
            >
              <p>Page content</p>
            </PageLayout>
          </MemoryRouter>
        </TextProvider>
      </ThemeModeProvider>,
    )

    const breadcrumb = screen.getByRole('navigation', { name: 'Breadcrumb' })
    expect(within(breadcrumb).getByRole('link', { name: 'E2E notes' })).toHaveAttribute('href', '/e2e')
    expect(within(breadcrumb).getByRole('link', { name: 'Notes' })).toHaveAttribute('href', '/e2e/notes?page=2')
    expect(within(breadcrumb).queryByRole('link', { name: 'New note' })).not.toBeInTheDocument()
    expect(within(breadcrumb).getByText('New note')).toHaveAttribute('aria-current', 'page')
    expect(screen.getByRole('heading', { name: 'New note' })).toBeInTheDocument()
  })
})
