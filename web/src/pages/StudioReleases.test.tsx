import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { TextProvider } from '../platform/TextProvider'
import type { StudioRelease, StudioReleaseBlocker, StudioReleaseItem, StudioReleases } from '../platform/studio'
import { ThemeModeProvider } from '../platform/theme'
import { StudioReleasesList } from './StudioReleases'

function item(
  releaseId: string,
  number: number,
  activatedAt: string,
  details: Partial<StudioReleaseItem> = {},
): StudioReleaseItem {
  return {
    releaseId,
    number,
    contentHash: `hash-${number}`,
    activatedAt,
    actor: 'user-1',
    actorName: null,
    label: null,
    note: null,
    requestId: null,
    offStandard: false,
    relaxed: [],
    ...details,
  }
}

// Out of order. Release 1 was activated again with no label, and release 2 is live.
const history: StudioReleases = {
  active: { releaseId: 'r2', number: 2, contentHash: 'hash-2' },
  folderContentHash: null,
  changes: null,
  items: [
    item('r2', 2, '2026-05-02T10:00:00Z', { actor: 'startup', note: 'Second note' }),
    item('r4', 4, '2026-05-04T10:00:00Z', { actor: 'unknown' }),
    item('r1', 1, '2026-05-05T10:00:00Z'),
    item('r1', 1, '2026-05-01T10:00:00Z', { actorName: 'Author User', label: 'First' }),
    item('r3', 3, '2026-05-03T10:00:00Z', { actor: 'operator' }),
  ],
}

function renderList(overrides: { history?: StudioReleases; canActivate?: boolean; busy?: boolean } = {}) {
  const onActivate = vi.fn()
  render(
    <ThemeModeProvider>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <MemoryRouter>
          <StudioReleasesList
            application="PurchaseRequests"
            history={overrides.history ?? history}
            canActivate={overrides.canActivate ?? true}
            busy={overrides.busy ?? false}
            onActivate={onActivate}
          />
        </MemoryRouter>
      </TextProvider>
    </ThemeModeProvider>,
  )
  return onActivate
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

function answer(body: StudioRelease | number) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async () =>
      typeof body === 'number' ? new Response(null, { status: body }) : new Response(JSON.stringify(body)),
    ),
  )
}

afterEach(() => {
  vi.unstubAllGlobals()
  localStorage.clear()
})

describe.each(['light', 'dark'])('StudioReleasesList in %s mode', (mode) => {
  beforeEach(() => localStorage.setItem('axis.themeMode', mode))

  it('has one row for each release, the highest number first, with the columns and no Status', () => {
    renderList()

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

  it('takes the label, note and time of a release from its first activation', () => {
    renderList()

    const rows = screen.getAllByTestId('studio-release-row')
    expect(within(rows[3]).getByText('First')).toBeInTheDocument()
    expect(within(rows[3]).getByText(/May 1, 2026|1 May 2026|1 thg 5, 2026/)).toBeInTheDocument()
    expect(within(rows[2]).getByText('Second note')).toBeInTheDocument()
  })

  it('shows the Live tag in the Release cell of the live row only', () => {
    renderList()

    const rows = screen.getAllByTestId('studio-release-row')
    expect(rows.filter((row) => row.hasAttribute('data-active'))).toEqual([rows[2]])
    expect(screen.getAllByText('Live')).toHaveLength(1)
    const releaseCell = within(rows[2]).getAllByRole('cell')[0]
    expect(within(releaseCell).getByText('Live')).toBeInTheDocument()
    expect(releaseCell).toHaveTextContent('2')
  })

  it('links each release number to the release page', () => {
    renderList()

    for (const number of ['1', '2', '3', '4']) {
      expect(screen.getByRole('link', { name: number })).toHaveAttribute(
        'href',
        `/studio?app=PurchaseRequests&view=release&release=${number}`,
      )
    }
  })

  it('shows the em dash for an empty label and an empty note', () => {
    renderList()

    const cells = within(screen.getAllByTestId('studio-release-row')[1]).getAllByRole('cell')
    expect(cells[1]).toHaveTextContent('—')
    expect(cells[4]).toHaveTextContent('—')
  })

  it('names who published each release in words, never an id', () => {
    renderList()

    const publishedBy = screen
      .getAllByTestId('studio-release-row')
      .map((row) => within(row).getAllByRole('cell')[3].textContent)
    expect(publishedBy).toEqual(['Someone no longer known', 'The platform operator', 'System', 'Author User'])
    expect(screen.queryByText('user-1')).not.toBeInTheDocument()
  })

  it('marks an off-standard release in its Release cell with each relaxed module, point and reason', () => {
    const relaxed = item('r3', 3, '2026-05-03T10:00:00Z', {
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
    renderList({ history: { ...history, items: [history.items[0], relaxed] } })

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

  it('shows an empty text without releases', () => {
    renderList({ history: { active: null, folderContentHash: null, changes: null, items: [] } })

    expect(screen.getByText('No release yet.')).toBeInTheDocument()
  })

  it('shows no Activate… button to a user who is not a publisher', () => {
    renderList({ canActivate: false })

    expect(screen.queryByRole('button', { name: /^Activate release/ })).not.toBeInTheDocument()
  })

  it('shows Activate… on every row but the live one, named with the release number', () => {
    renderList()

    const rows = screen.getAllByTestId('studio-release-row')
    expect(
      rows.map((row) => within(row).queryByRole('button', { name: /^Activate release \d+$/ })?.ariaLabel ?? null),
    ).toEqual(['Activate release 4', 'Activate release 3', null, 'Activate release 1'])
  })

  it('turns the buttons off while busy', () => {
    renderList({ busy: true })

    const buttons = screen.getAllByRole('button', { name: /^Activate release/ })
    expect(buttons).toHaveLength(3)
    for (const button of buttons) {
      expect(button).toBeDisabled()
    }
  })

  it('names the release that replaces the live one and activates it on confirmation', async () => {
    answer(release(1))
    const user = userEvent.setup()
    const onActivate = renderList()

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
    answer(release(3))
    const user = userEvent.setup()
    const onActivate = renderList()

    await user.click(screen.getByRole('button', { name: 'Activate release 3' }))
    const dialog = await screen.findByRole('dialog')
    expect(await within(dialog).findByText('Release 3 replaces live release 2.')).toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: 'Cancel' }))

    expect(onActivate).not.toHaveBeenCalled()
  })

  it('titles the dialog with the cannot-replace sentence, lists the blockers and offers Close alone', async () => {
    answer(
      release(1, [
        { module: 'a', label: 'Assets', liveVersion: '1.2.0', version: null },
        { module: 'b', label: 'Budgets', liveVersion: '2.0.0', version: '1.0.0' },
      ]),
    )
    const user = userEvent.setup()
    const onActivate = renderList()

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
    vi.stubGlobal(
      'fetch',
      vi.fn(() => new Promise<Response>(() => {})),
    )
    const user = userEvent.setup()
    renderList()

    await user.click(screen.getByRole('button', { name: 'Activate release 1' }))

    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByRole('button', { name: 'Activate' })).toBeDisabled()
    expect(dialog.querySelector('.ant-spin')).not.toBeNull()
    expect(within(dialog).queryByTestId('studio-activate-release')).not.toBeInTheDocument()
  })

  it('falls back to the replaces sentence and Activate when the release does not load', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    answer(500)
    const user = userEvent.setup()
    const onActivate = renderList()

    await user.click(screen.getByRole('button', { name: 'Activate release 1' }))

    const dialog = await screen.findByRole('dialog')
    expect(await within(dialog).findByTestId('studio-activate-release')).toHaveTextContent(
      'Release 1 replaces live release 2.',
    )
    await user.click(within(dialog).getByRole('button', { name: 'Activate' }))

    expect(onActivate).toHaveBeenCalledTimes(1)
  })
})
