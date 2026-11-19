import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useLocation } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { TextProvider } from '../platform/TextProvider'
import type { StudioRelease, StudioReleaseBlocker, StudioReleaseListRow, StudioReleases } from '../platform/studio'
import { ThemeModeProvider } from '../platform/theme'
import { StudioReleasesList } from './StudioReleases'

const listPath = '/api/studio/apps/PurchaseRequests/release-list'

function item(releaseId: string, number: number, details: Partial<StudioReleaseListRow> = {}): StudioReleaseListRow {
  return {
    releaseId,
    number,
    label: null,
    note: null,
    publishedAt: `2026-05-0${number}T10:00:00Z`,
    actor: 'user-1',
    actorName: null,
    live: false,
    offStandard: false,
    relaxed: [],
    ...details,
  }
}

// What the server answers: one row for each release, the highest number first, and release 2 is live.
const answered: StudioReleaseListRow[] = [
  item('r4', 4, { actor: 'unknown' }),
  item('r3', 3, { actor: 'operator' }),
  item('r2', 2, { actor: 'startup', note: 'Second note', live: true }),
  item('r1', 1, { actorName: 'Author User', label: 'First' }),
]

// The list reads only the live number from the band's history.
const history: StudioReleases = {
  active: { releaseId: 'r2', number: 2, contentHash: 'hash-2' },
  folderContentHash: null,
  changes: null,
  partChanges: null,
  items: [],
}

function release(number: number, blockers: StudioReleaseBlocker[] = []): StudioRelease {
  return {
    number,
    previous: 2,
    previousIsLive: true,
    live: false,
    changes: [],
    partChanges: [],
    modules: [],
    packages: [],
    blockers,
  }
}

/**
 * Stubs the server: the release list answers `page` with `total` as its count, and a release's page answers `detail`,
 * or its status when that is a number, or never when it is 'pending'. It returns the URLs asked for the list.
 */
function stubServer(
  page: StudioReleaseListRow[] = answered,
  total = page.length,
  detail: StudioRelease | number | 'pending' = release(1),
) {
  const fetchMock = vi.fn(async (input: string) => {
    if (input.startsWith(listPath)) {
      return new Response(JSON.stringify({ items: page, page: 1, pageSize: 20, totalCount: total }), {
        headers: { 'Content-Type': 'application/json' },
      })
    }
    if (detail === 'pending') {
      return new Promise<Response>(() => {})
    }
    return typeof detail === 'number' ? new Response(null, { status: detail }) : new Response(JSON.stringify(detail))
  })
  vi.stubGlobal('fetch', fetchMock)
  return () => fetchMock.mock.calls.map(([input]) => input).filter((input) => input.startsWith(listPath))
}

const Location = () => <output data-testid="location">{useLocation().search}</output>

async function renderList(
  overrides: { history?: StudioReleases; canActivate?: boolean; busy?: boolean; address?: string; wait?: boolean } = {},
) {
  const onActivate = vi.fn()
  render(
    <ThemeModeProvider>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <MemoryRouter initialEntries={[overrides.address ?? '/studio?app=PurchaseRequests&view=releases']}>
          <StudioReleasesList
            application="PurchaseRequests"
            history={overrides.history ?? history}
            canActivate={overrides.canActivate ?? true}
            busy={overrides.busy ?? false}
            onActivate={onActivate}
          />
          <Location />
        </MemoryRouter>
      </TextProvider>
    </ThemeModeProvider>,
  )
  if (overrides.wait !== false) {
    await waitFor(() =>
      expect(
        screen.queryAllByTestId('studio-release-row').length + screen.queryAllByText('No release yet.').length,
      ).toBeGreaterThan(0),
    )
  }
  return onActivate
}

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
  localStorage.clear()
})

describe.each(['light', 'dark'])('StudioReleasesList in %s mode', (mode) => {
  beforeEach(() => {
    localStorage.setItem('axis.themeMode', mode)
    stubServer()
  })

  it('shows one row for each release the server answers, in its order, with the columns and no Status', async () => {
    await renderList()

    const rows = screen.getAllByTestId('studio-release-row')
    expect(rows.map((row) => row.getAttribute('data-number'))).toEqual(['4', '3', '2', '1'])
    expect(screen.getAllByRole('columnheader').map((header) => header.textContent)).toEqual([
      'Release',
      'Label',
      'Published',
      'Published by',
      'Note',
      'Actions',
    ])
    expect(screen.queryByRole('columnheader', { name: 'Status' })).not.toBeInTheDocument()
  })

  it('shows the label, note and time that the server answers for a release', async () => {
    await renderList()

    const rows = screen.getAllByTestId('studio-release-row')
    expect(within(rows[3]).getByText('First')).toBeInTheDocument()
    expect(within(rows[3]).getByText(/May 1, 2026|1 May 2026|1 thg 5, 2026/)).toBeInTheDocument()
    expect(within(rows[2]).getByText('Second note')).toBeInTheDocument()
  })

  it('shows the Live tag in the Release cell of the live row only', async () => {
    await renderList()

    const rows = screen.getAllByTestId('studio-release-row')
    expect(rows.filter((row) => row.hasAttribute('data-active'))).toEqual([rows[2]])
    expect(screen.getAllByText('Live')).toHaveLength(1)
    const releaseCell = within(rows[2]).getAllByRole('cell')[0]
    expect(within(releaseCell).getByText('Live')).toBeInTheDocument()
    expect(within(releaseCell).getByText('Live').closest('[data-tone]')).toHaveAttribute('data-tone', 'neutral')
    expect(releaseCell).toHaveTextContent('2')
  })

  it('links each release number to the release page', async () => {
    await renderList()

    for (const number of ['1', '2', '3', '4']) {
      expect(screen.getByRole('link', { name: number })).toHaveAttribute(
        'href',
        `/studio?app=PurchaseRequests&view=release&release=${number}`,
      )
    }
  })

  it('shows the em dash for an empty label and an empty note', async () => {
    await renderList()

    const cells = within(screen.getAllByTestId('studio-release-row')[1]).getAllByRole('cell')
    expect(cells[1]).toHaveTextContent('—')
    expect(cells[4]).toHaveTextContent('—')
  })

  it('names who published each release in words, never an id', async () => {
    await renderList()

    const publishedBy = screen
      .getAllByTestId('studio-release-row')
      .map((row) => within(row).getAllByRole('cell')[3].textContent)
    expect(publishedBy).toEqual(['Someone no longer known', 'The platform operator', 'System', 'Author User'])
    expect(screen.queryByText('user-1')).not.toBeInTheDocument()
  })

  it('marks an off-standard release in its Release cell with each relaxed module, point and reason', async () => {
    const relaxed = item('r3', 3, {
      offStandard: true,
      relaxed: [
        {
          module: 'department-budgets',
          point: 'Budget/transitions/approve',
          reason: 'Approvers decide alone',
          moduleTitle: null,
          part: null,
          entityTitles: null,
          titles: null,
        },
        {
          module: 'audit',
          point: 'Entry/fields/note',
          reason: 'Pilot',
          moduleTitle: null,
          part: null,
          entityTitles: null,
          titles: null,
        },
        {
          module: 'department-budgets',
          point: 'Budget/fields/limit',
          reason: 'Limit',
          moduleTitle: null,
          part: null,
          entityTitles: null,
          titles: null,
        },
      ],
    })
    stubServer([relaxed, answered[2]])
    await renderList()

    const rows = screen.getAllByTestId('studio-release-row')
    expect(rows.map((row) => row.getAttribute('data-off-standard'))).toEqual(['true', null])
    const releaseCell = within(rows[0]).getAllByRole('cell')[0]
    expect(within(releaseCell).getByTestId('release-off-standard')).toHaveTextContent('Off-standard')
    const points = within(releaseCell).getAllByTestId('release-relaxed-point')
    expect(points.map((point) => point.textContent)).toEqual([
      'audit Entry/fields/note: Pilot',
      'department-budgets Budget/fields/limit: Limit',
      'department-budgets Budget/transitions/approve: Approvers decide alone',
    ])
    expect(within(rows[1]).queryByTestId('release-off-standard')).not.toBeInTheDocument()
    expect(within(rows[1]).queryByTestId('release-relaxed-point')).not.toBeInTheDocument()
  })

  it('shows an empty text without releases', async () => {
    stubServer([])
    await renderList({ history: { ...history, active: null } })

    expect(screen.getByText('No release yet.')).toBeInTheDocument()
  })

  it('shows no Activate… button to a user who is not a publisher', async () => {
    await renderList({ canActivate: false })

    expect(screen.queryByRole('button', { name: /^Activate release/ })).not.toBeInTheDocument()
  })

  it('shows Activate… on every row but the live one, named with the release number', async () => {
    await renderList()

    const rows = screen.getAllByTestId('studio-release-row')
    expect(
      rows.map((row) => within(row).queryByRole('button', { name: /^Activate release \d+$/ })?.ariaLabel ?? null),
    ).toEqual(['Activate release 4', 'Activate release 3', null, 'Activate release 1'])
  })

  it('turns the buttons off while busy', async () => {
    await renderList({ busy: true })

    const buttons = screen.getAllByRole('button', { name: /^Activate release/ })
    expect(buttons).toHaveLength(3)
    for (const button of buttons) {
      expect(button).toBeDisabled()
    }
  })

  it('names the release that replaces the live one and activates it on confirmation', async () => {
    stubServer(answered, answered.length, release(1))
    const user = userEvent.setup()
    const onActivate = await renderList()

    await user.click(screen.getByRole('button', { name: 'Activate release 1' }))

    const dialog = await screen.findByRole('dialog')
    expect(dialog).toHaveTextContent('Activate release 1?')
    expect(await within(dialog).findByTestId('studio-activate-release')).toHaveTextContent(
      'Release 1 replaces live release 2.',
    )
    expect(onActivate).not.toHaveBeenCalled()

    await waitFor(() => expect(within(dialog).getByRole('button', { name: 'Activate' })).toBeEnabled())
    await user.click(within(dialog).getByRole('button', { name: 'Activate' }))

    expect(onActivate).toHaveBeenCalledTimes(1)
    expect(onActivate.mock.calls[0][0]).toMatchObject({ releaseId: 'r1', number: 1 })
  })

  it('does not activate when the dialog is cancelled', async () => {
    stubServer(answered, answered.length, release(3))
    const user = userEvent.setup()
    const onActivate = await renderList()

    await user.click(screen.getByRole('button', { name: 'Activate release 3' }))
    const dialog = await screen.findByRole('dialog')
    expect(await within(dialog).findByText('Release 3 replaces live release 2.')).toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: 'Cancel' }))

    expect(onActivate).not.toHaveBeenCalled()
  })

  it('titles the dialog with the cannot-replace sentence, lists the blockers and offers Close alone', async () => {
    stubServer(
      answered,
      answered.length,
      release(1, [
        { module: 'a', label: 'Assets', liveVersion: '1.2.0', version: null },
        { module: 'b', label: 'Budgets', liveVersion: '2.0.0', version: '1.0.0' },
      ]),
    )
    const user = userEvent.setup()
    const onActivate = await renderList()

    await user.click(screen.getByRole('button', { name: 'Activate release 1' }))

    const dialog = await screen.findByRole('dialog')
    expect(await within(dialog).findByText('Release 1 cannot replace release 2')).toBeInTheDocument()
    const lines = within(dialog).getAllByTestId('studio-release-blocker')
    expect(lines.map((line) => line.textContent)).toEqual([
      'Assets: the live release has 1.2.0, and this release lacks it',
      'Budgets: the live release has 2.0.0, and 1.0.0 lacks some of its entities or fields',
    ])
    expect(within(dialog).queryByRole('button', { name: 'Activate' })).not.toBeInTheDocument()
    expect(within(dialog).queryByTestId('studio-activate-release')).not.toBeInTheDocument()
    const footer = dialog.querySelector('.ant-modal-footer') as HTMLElement
    expect(
      within(footer)
        .getAllByRole('button')
        .map((button) => button.textContent),
    ).toEqual(['Close'])

    await user.click(within(footer).getByRole('button', { name: 'Close' }))

    expect(onActivate).not.toHaveBeenCalled()
  })

  it('turns Activate off with a spinner while the release loads', async () => {
    stubServer(answered, answered.length, 'pending')
    const user = userEvent.setup()
    await renderList()

    await user.click(screen.getByRole('button', { name: 'Activate release 1' }))

    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByRole('button', { name: 'Activate' })).toBeDisabled()
    expect(dialog.querySelector('.ant-spin')).not.toBeNull()
    expect(within(dialog).queryByTestId('studio-activate-release')).not.toBeInTheDocument()
  })

  it('falls back to the replaces sentence and Activate when the release does not load', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    stubServer(answered, answered.length, 500)
    const user = userEvent.setup()
    const onActivate = await renderList()

    await user.click(screen.getByRole('button', { name: 'Activate release 1' }))

    const dialog = await screen.findByRole('dialog')
    expect(await within(dialog).findByTestId('studio-activate-release')).toHaveTextContent(
      'Release 1 replaces live release 2.',
    )
    await user.click(within(dialog).getByRole('button', { name: 'Activate' }))

    expect(onActivate).toHaveBeenCalledTimes(1)
  })

  it('asks the server for the search, the filter and the sort of the address and shows the rows it answers', async () => {
    const filter = JSON.stringify({ all: [{ field: 'number', op: 'gte', value: 3 }] })
    const requested = stubServer([answered[1]], 1)

    await renderList({
      address: `/studio?app=PurchaseRequests&view=releases&search=cut&filter=${encodeURIComponent(filter)}&sort=-label`,
    })

    expect(screen.getAllByTestId('studio-release-row').map((row) => row.getAttribute('data-number'))).toEqual(['3'])
    const query = new URLSearchParams(requested()[0].slice(listPath.length + 1))
    expect(query.get('search')).toBe('cut')
    expect(query.get('filter')).toBe(filter)
    expect(query.get('sort')).toBe('-label')
    expect(screen.getByRole('searchbox', { name: 'Search' })).toHaveValue('cut')
  })

  it('sorts from the Label header into the address and asks the server again', async () => {
    const requested = stubServer()
    const user = userEvent.setup()
    await renderList()

    await user.click(screen.getByRole('columnheader', { name: 'Label' }))

    await waitFor(() => expect(screen.getByTestId('location')).toHaveTextContent('sort=label'))
    await waitFor(() => expect(requested().some((url) => url.includes('sort=label'))).toBe(true))
  })

  it('does not sort from the Note header', async () => {
    const requested = stubServer()
    const user = userEvent.setup()
    await renderList()

    await user.click(screen.getByRole('columnheader', { name: 'Note' }))
    await new Promise((resolve) => setTimeout(resolve, 50))

    expect(requested()).toHaveLength(1)
    expect(screen.getByTestId('location')).not.toHaveTextContent('sort=')
  })

  it('shows the pager as 1–20 of 45 and keeps a page size in the address', async () => {
    const requested = stubServer(answered, 45)
    const user = userEvent.setup()
    await renderList()

    expect(screen.getByText('1–20 of 45')).toBeInTheDocument()

    await user.click(screen.getByRole('combobox'))
    await user.click(await screen.findByTitle('50 / page'))

    await waitFor(() => expect(screen.getByTestId('location')).toHaveTextContent('pageSize=50'))
    await waitFor(() => expect(requested()).toContain(`${listPath}?pageSize=50`))
  })

  it('says the releases could not be loaded when the list fails', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => new Response(null, { status: 500 })),
    )
    await renderList({ wait: false })

    expect(await screen.findByText('The releases could not be loaded.')).toBeInTheDocument()
  })
})
