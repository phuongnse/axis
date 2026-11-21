import { render, screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { spacingTokens } from './designTokens'
import { PageActions, PageLayout } from './PageLayout'
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

  it('makes the title the level-one heading of the page', () => {
    render(
      <ThemeModeProvider>
        <PageLayout title="Purchase requests">
          <p>Page content</p>
        </PageLayout>
      </ThemeModeProvider>,
    )

    expect(screen.getByRole('heading', { level: 1, name: 'Purchase requests' })).toBeInTheDocument()
  })

  it('draws the heading alone in a band with spacing-md above and below it and spacing-lg at its sides', () => {
    render(
      <ThemeModeProvider>
        <PageLayout title="Users" band>
          <p>Page content</p>
        </PageLayout>
      </ThemeModeProvider>,
    )

    const band = screen.getByTestId('page-band')
    expect(within(band).getByRole('heading', { level: 1, name: 'Users' })).toBeInTheDocument()
    expect(within(band).queryByText('Page content')).not.toBeInTheDocument()
    expect(band).toHaveStyle({
      paddingTop: `${spacingTokens.md}px`,
      paddingBottom: `${spacingTokens.md}px`,
      paddingLeft: `${spacingTokens.lg}px`,
      paddingRight: `${spacingTokens.lg}px`,
    })
  })

  it('draws no band without band', () => {
    render(
      <ThemeModeProvider>
        <PageLayout title="Users" />
      </ThemeModeProvider>,
    )

    expect(screen.getByRole('heading', { level: 1, name: 'Users' })).toBeInTheDocument()
    expect(screen.queryByTestId('page-band')).not.toBeInTheDocument()
  })

  it('draws the tags beside the title and the line under it, and no line when it is empty', () => {
    const { rerender } = render(
      <ThemeModeProvider>
        <PageLayout title="Purchase requests" tags={<span>Live</span>} line="Release" />
      </ThemeModeProvider>,
    )

    const heading = screen.getByTestId('page-heading')
    expect(within(heading).getByRole('heading', { level: 1, name: 'Purchase requests' })).toBeInTheDocument()
    expect(within(heading).getByText('Live')).toBeInTheDocument()
    expect(within(heading).getByText('Release')).toBeInTheDocument()

    rerender(
      <ThemeModeProvider>
        <PageLayout title="Purchase requests" line="" />
      </ThemeModeProvider>,
    )
    expect(screen.queryByText('Release')).not.toBeInTheDocument()
  })

  it('draws the actions of a component deep in the page in the slot on the right of the heading', async () => {
    render(
      <ThemeModeProvider>
        <PageLayout title="Purchase requests">
          <section>
            <div>
              <PageActions>
                <button type="button">Activate…</button>
              </PageActions>
            </div>
          </section>
        </PageLayout>
      </ThemeModeProvider>,
    )

    const actions = await screen.findByTestId('page-actions')
    expect(await within(actions).findByRole('button', { name: 'Activate…' })).toBeInTheDocument()
    expect(within(screen.getByTestId('page-heading')).getByRole('button', { name: 'Activate…' })).toBeInTheDocument()
  })

  it('draws the actions where they stand outside a page', () => {
    render(
      <PageActions>
        <button type="button">Activate…</button>
      </PageActions>,
    )

    expect(screen.getByRole('button', { name: 'Activate…' })).toBeInTheDocument()
  })

  it('draws the actions where they stand inside a page when asked, not on its heading', () => {
    render(
      <ThemeModeProvider>
        <PageLayout title="Purchase requests">
          <PageActions inPlace>
            <button type="button">Activate…</button>
          </PageActions>
        </PageLayout>
      </ThemeModeProvider>,
    )

    expect(screen.getByRole('button', { name: 'Activate…' })).toBeInTheDocument()
    expect(within(screen.getByTestId('page-actions')).queryByRole('button')).not.toBeInTheDocument()
  })
})
