import { render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { installFixtureFetch } from './fixtureFetch'
import { fixtureScopes, requestId, routes } from './fixtures'
import { Preview } from './Preview'
import { ShellPreview } from './ShellPreview'
import { chosenRoutes, previews, stateRoutes } from './previews'

describe('the design-system shell states', () => {
  const originalFetch = window.fetch

  beforeEach(() => {
    // The bundle installs the same fixture fetch, so the states are drawn as the preview draws them.
    installFixtureFetch({ ...routes, ...stateRoutes(previews), ...chosenRoutes(previews) }, fixtureScopes)
  })

  afterEach(() => {
    window.fetch = originalFetch
    window.history.replaceState({}, '', '/')
  })

  it('draws only the state that the state query names, under a State control', async () => {
    window.history.replaceState({}, '', '/?state=platform-site')

    render(<Preview states={previews.shell} />)

    const control = await screen.findByRole('radiogroup', { name: 'State' }, { timeout: 30000 })
    expect(screen.getByRole('radio', { name: 'Platform site' })).toBeChecked()
    expect(control).toBeInTheDocument()
    const figures = screen.getAllByRole('figure')
    expect(figures).toHaveLength(1)
    expect(figures[0].querySelector('figcaption')).toHaveTextContent('Platform site')
    expect(screen.queryByText('Application site', { selector: 'figcaption' })).toBeNull()
  }, 30000)

  it('opens a saved record on its form page at the site page address', async () => {
    const saved = {
      caption: 'Saved record',
      draw: () => <ShellPreview />,
      entry: `/sample/request/${requestId}`,
      frameHeight: 800,
    }

    render(<Preview states={[saved]} />)

    expect(await screen.findByDisplayValue('Standing desks for the new office', {}, { timeout: 15000 })).toBeVisible()
  }, 30000)
})
