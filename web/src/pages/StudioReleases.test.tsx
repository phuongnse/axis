import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { TextProvider } from '../platform/TextProvider'
import type { StudioReleaseItem, StudioReleases } from '../platform/studio'
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

// Out of order. Release 1 was activated again by a rollback with no label, and release 2 is live.
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

function renderList(overrides: { history?: StudioReleases; canRollBack?: boolean; busy?: boolean } = {}) {
  const onRollBack = vi.fn()
  render(
    <ThemeModeProvider>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <StudioReleasesList
          application="PurchaseRequests"
          history={overrides.history ?? history}
          canRollBack={overrides.canRollBack ?? true}
          busy={overrides.busy ?? false}
          onRollBack={onRollBack}
        />
      </TextProvider>
    </ThemeModeProvider>,
  )
  return onRollBack
}

afterEach(() => localStorage.clear())

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
        { module: 'department-budgets', point: 'Budget/transitions/approve', reason: 'Approvers decide alone' },
      ],
    })
    renderList({ history: { ...history, items: [history.items[0], relaxed] } })

    const rows = screen.getAllByTestId('studio-release-row')
    expect(rows.map((row) => row.getAttribute('data-off-standard'))).toEqual(['true', null])
    const releaseCell = within(rows[0]).getAllByRole('cell')[0]
    expect(within(releaseCell).getByTestId('release-off-standard')).toHaveTextContent('Off-standard')
    const point = within(releaseCell).getByTestId('release-relaxed-point')
    expect(point).toHaveTextContent('department-budgets')
    expect(point).toHaveTextContent('Budget/transitions/approve')
    expect(point).toHaveTextContent('Approvers decide alone')
    expect(within(rows[1]).queryByTestId('release-off-standard')).not.toBeInTheDocument()
    expect(within(rows[1]).queryByTestId('release-relaxed-point')).not.toBeInTheDocument()
  })

  it('shows an empty text without releases', () => {
    renderList({ history: { active: null, folderContentHash: null, changes: null, items: [] } })

    expect(screen.getByText('No release yet.')).toBeInTheDocument()
  })

  it('shows no Activate… button to a user who is not a publisher', () => {
    renderList({ canRollBack: false })

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
    const user = userEvent.setup()
    const onRollBack = renderList()

    await user.click(screen.getByRole('button', { name: 'Activate release 1' }))

    const dialog = await screen.findByRole('dialog')
    expect(dialog).toHaveTextContent('Activate release 1?')
    expect(within(dialog).getByTestId('studio-rollback-release')).toHaveTextContent(
      'Release 1 replaces live release 2.',
    )
    expect(onRollBack).not.toHaveBeenCalled()

    await user.click(within(dialog).getByRole('button', { name: 'Activate' }))

    expect(onRollBack).toHaveBeenCalledTimes(1)
    expect(onRollBack.mock.calls[0][0]).toMatchObject({ releaseId: 'r1', number: 1 })
  })

  it('does not activate when the dialog is cancelled', async () => {
    const user = userEvent.setup()
    const onRollBack = renderList()

    await user.click(screen.getByRole('button', { name: 'Activate release 3' }))
    const dialog = await screen.findByRole('dialog')
    expect(dialog).toHaveTextContent('Release 3 replaces live release 2.')
    await user.click(within(dialog).getByRole('button', { name: 'Cancel' }))

    expect(onRollBack).not.toHaveBeenCalled()
  })
})
