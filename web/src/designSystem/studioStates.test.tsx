import { render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { installFixtureFetch } from './fixtureFetch'
import { fixtureScopes, routes } from './fixtures'
import { Preview } from './Preview'
import { chosenRoutes, previews, stateRoutes } from './previews'

const packageFiles = [
  'packages/approval-rules/adapter.py',
  'packages/approval-rules/new.py',
  'packages/approval-rules/old.py',
]

/** The files that the changes view of the state draws, by their `data-file`, in the order they are drawn. */
function changedFiles(): (string | null)[] {
  return screen.queryAllByTestId('studio-changes-file').map((file) => file.getAttribute('data-file'))
}

describe('the design-system Studio states that draw changed files', () => {
  const originalFetch = window.fetch

  beforeEach(() => {
    // The bundle installs the same fixture fetch, so the states are drawn as the preview draws them.
    installFixtureFetch({ ...routes, ...stateRoutes(previews), ...chosenRoutes(previews, routes) }, fixtureScopes)
  })

  afterEach(() => {
    window.fetch = originalFetch
    vi.restoreAllMocks()
  })

  it.each([
    ['Package file revision', packageFiles],
    ['Authoring request', packageFiles],
    ['Publish page', ['pages/request-list.json', ...packageFiles]],
  ])(
    'draws the changed files of %s, with no missing fixture',
    async (caption, files) => {
      const errors = vi.spyOn(console, 'error')
      const state = previews.studio.find((item) => item.caption === caption)!

      render(<Preview states={[state]} />)

      await waitFor(() => expect(changedFiles()).toEqual(files), { timeout: 15000 })
      const missing = errors.mock.calls.filter(([message]) => String(message).startsWith('No design-system fixture'))
      expect(missing).toEqual([])
    },
    30000,
  )
})
