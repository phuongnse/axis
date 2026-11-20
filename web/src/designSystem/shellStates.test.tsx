import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { installFixtureFetch } from './fixtureFetch'
import { categoryId, fixtureScopes, requestId, routes } from './fixtures'
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

  it('opens a category on the default record page, with Requests marked in the sidebar', async () => {
    const recordPage = {
      caption: 'Default record page',
      draw: () => <ShellPreview />,
      entry: { pathname: `/sample/records/category/${categoryId}`, state: { from: '/sample/requests' } },
      frameHeight: 800,
    }

    render(<Preview states={[recordPage]} />)

    expect(await screen.findByDisplayValue('Office furniture', {}, { timeout: 15000 })).toBeVisible()
    expect(screen.getByRole('link', { name: 'Requests' })).toHaveAttribute('aria-current', 'page')
    expect(screen.queryByRole('navigation', { name: 'Breadcrumb' })).not.toBeInTheDocument()
  }, 30000)

  it("opens a saved request's Category on the default record page, not the not-found page", async () => {
    const saved = {
      caption: 'Saved record',
      draw: () => <ShellPreview />,
      entry: `/sample/request/${requestId}`,
      frameHeight: 800,
    }
    render(<Preview states={[saved]} />)
    // Each of the request's collection rows has its own Open link, so the link is found in the field's own group.
    const field = await screen.findByTestId('reference-category', {}, { timeout: 15000 })

    const open = await within(field).findByRole('link', { name: 'Open Office furniture' })
    expect(open).toHaveAttribute('href', `/sample/records/category/${categoryId}`)
    await userEvent.click(open)

    expect(await screen.findByRole('heading', { name: 'Category' }, { timeout: 15000 })).toBeVisible()
    expect(screen.queryByTestId('not-found')).toBeNull()
  }, 30000)

  const state = (caption: string) => previews.shell.find((candidate) => candidate.caption === caption)!

  it('shows a saved record with Submit for approval on the page heading and its history', async () => {
    render(<Preview states={[state('Saved record')]} />)

    const heading = await screen.findByTestId('page-actions', {}, { timeout: 15000 })
    expect(
      await within(heading).findByRole('button', { name: 'Submit for approval' }, { timeout: 15000 }),
    ).toBeVisible()
    expect(screen.queryByTestId('form-save-bar')).toBeNull()
    expect(screen.queryByRole('button', { name: 'Save' })).toBeNull()
  }, 30000)

  it('shows the save bar for a saved record with a change typed into Title', async () => {
    render(<Preview states={[state('Saved record with a change')]} />)

    expect(await screen.findByTestId('form-save-bar', {}, { timeout: 15000 })).toBeVisible()
    expect(screen.getByRole('textbox', { name: 'Title' })).toHaveValue('Standing desks for the new office (revised)')
  }, 30000)

  it('shows the missing-record line with a link to Requests for an id the fixtures answer 404 for', async () => {
    render(<Preview states={[state('Record not found')]} />)

    expect(await screen.findByTestId('record-not-found', {}, { timeout: 15000 })).toBeVisible()
    expect(screen.getByRole('link', { name: 'Go to Requests' })).toBeVisible()
  }, 30000)
})
