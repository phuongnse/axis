import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { installFixtureFetch } from './fixtureFetch'
import { fixtureScopes, routes } from './fixtures'
import { Preview } from './Preview'
import { chosenRoutes, previews, stateRoutes } from './previews'

describe('the design-system Entity with expressions state', () => {
  const originalFetch = window.fetch

  beforeEach(() => {
    // The bundle installs the same fixture fetch, so the state is drawn as the preview draws it.
    installFixtureFetch({ ...routes, ...stateRoutes(previews), ...chosenRoutes(previews, routes) }, fixtureScopes)
  })

  afterEach(() => {
    window.fetch = originalFetch
  })

  it('reads the computed field and the validation in words, each box with Expand inside it', async () => {
    const state = previews.studio.find((item) => item.caption === 'Entity with expressions')!

    render(<Preview states={[state]} />)

    // The computed field's expression is in its field drawer, which starts closed: open it from the field's name.
    await userEvent.click(await screen.findByRole('link', { name: 'amountWithTax' }, { timeout: 15000 }))
    // Each expression box checks on its own, so the second reading can arrive after the first. Wait for both.
    await waitFor(() => expect(screen.getAllByTestId('studio-expression-reading')).toHaveLength(2), { timeout: 15000 })
    const readings = screen.getAllByTestId('studio-expression-reading')
    // The drawer is drawn in a portal after the page, so the validation's reading comes first in the document.
    const [validation, computed] = readings
    expect(computed).toHaveTextContent('In words: Amount × 1.1')
    expect(validation).toHaveTextContent('In words: Amount is greater than 0 and Title has a value')
    for (const reading of readings) {
      // Each reading sits under its own expression box, and Expand is inside that box.
      const box = reading.parentElement!.querySelector<HTMLElement>('.cm-editor')!
      expect(within(box).getByRole('button', { name: 'Expand' })).toBeInTheDocument()
    }
  }, 30000)
})
